using PhoneFolder.Desktop.Models;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PhoneFolder.Desktop.Services;

public sealed class RemoteClient : IDisposable
{
    private const int TransferBufferSize = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _host;
    private readonly int _port;
    private readonly string _token;
    private readonly string _expectedFingerprint;
    private readonly string _trustedToken;
    private readonly string _deviceName;
    private string _connectedFingerprint = string.Empty;

    public RemoteClient(
        string host,
        int port,
        string token,
        string? expectedFingerprint = null,
        string? trustedToken = null,
        string? deviceName = null)
    {
        _host = host;
        _port = port;
        _token = token;
        var normalizedHost = host.StartsWith('[') || !host.Contains(':') ? host : $"[{host}]";
        _expectedFingerprint = NormalizeFingerprint(expectedFingerprint);
        _trustedToken = trustedToken ?? string.Empty;
        _deviceName = string.IsNullOrWhiteSpace(deviceName) ? host : deviceName.Trim();
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            MaxConnectionsPerServer = 8,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = ValidateServerCertificate
            }
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{normalizedHost}:{port}/api/v1/"),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Timeout = TimeSpan.FromMinutes(30)
        };
        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Add("X-PhoneFolder-Token", token);
        }
        if (!string.IsNullOrWhiteSpace(trustedToken))
        {
            _httpClient.DefaultRequestHeaders.Add(
                "X-Phone-Transfer-Trusted-Token",
                trustedToken);
        }
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Phone-Transfer-Desktop/0.7.3");
        _httpClient.DefaultRequestHeaders.ExpectContinue = false;
    }

    public string DeviceName => _deviceName;
    public string ConnectionKey => string.IsNullOrEmpty(_expectedFingerprint)
        ? $"{_host}:{_port}"
        : _expectedFingerprint;

    public RemoteClient CreateSibling() =>
        new(_host, _port, _token, _expectedFingerprint, _trustedToken, _deviceName);

    public async Task<string> TrustThisPcAsync(
        string clientId,
        string clientName,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "trust");
        request.Headers.Add("X-Phone-Transfer-Client-Id", clientId);
        request.Headers.Add("X-Phone-Transfer-Client-Name", clientName);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var grant = await ReadAsync<TrustGrant>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(grant.TrustedToken))
        {
            throw new InvalidDataException("The phone returned an empty trusted-device token.");
        }
        return grant.TrustedToken;
    }

    public async Task<DeviceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("info", cancellationToken);
        var info = await ReadAsync<DeviceInfo>(response, cancellationToken);
        var advertisedFingerprint = NormalizeFingerprint(info.CertificateFingerprint);
        if (string.IsNullOrEmpty(_connectedFingerprint)
            || string.IsNullOrEmpty(advertisedFingerprint)
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(_connectedFingerprint),
                Convert.FromHexString(advertisedFingerprint)))
        {
            throw new InvalidOperationException(
                "The phone certificate did not match its advertised fingerprint.");
        }
        return info;
    }

    public async Task<IReadOnlyList<RemoteItem>> GetRootsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("roots", cancellationToken);
        return await ReadAsync<List<RemoteItem>>(response, cancellationToken);
    }

    public async Task<StorageInfo> GetStorageInfoAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("storage", cancellationToken);
        return await ReadAsync<StorageInfo>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteItem>> GetChildrenAsync(string itemId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"items/{Uri.EscapeDataString(itemId)}/children", cancellationToken);
        return await ReadAsync<List<RemoteItem>>(response, cancellationToken);
    }

    public async Task DownloadAsync(
        RemoteItem item,
        string destinationPath,
        Action<double> progress,
        CancellationToken cancellationToken = default)
    {
        var temporaryPath = destinationPath + ".phonefolder-part";
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var existingLength = File.Exists(temporaryPath) ? new FileInfo(temporaryPath).Length : 0;
        if (item.Size <= 0 || existingLength >= item.Size)
        {
            existingLength = 0;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"items/{Uri.EscapeDataString(item.Id)}/content");
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var resumed = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
        {
            existingLength = 0;
        }

        var total = item.Size > 0
            ? item.Size
            : existingLength + (response.Content.Headers.ContentLength ?? 0);
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
                         temporaryPath,
                         resumed ? FileMode.Append : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         TransferBufferSize,
                         true))
        {
            await CopyWithProgressAsync(
                input,
                output,
                total,
                existingLength,
                progress,
                cancellationToken);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(temporaryPath, destinationPath);
    }

    public async Task DownloadSelectionAsync(
        IReadOnlyList<RemoteItem> selectedItems,
        string destinationDirectory,
        Action<string, double> progress,
        CancellationToken cancellationToken = default)
    {
        await DownloadSelectionAsync(
            selectedItems,
            destinationDirectory,
            (name, value, _) => progress(name, value),
            cancellationToken);
    }

    public async Task DownloadSelectionAsync(
        IReadOnlyList<RemoteItem> selectedItems,
        string destinationDirectory,
        Action<string, double, long> progress,
        CancellationToken cancellationToken = default)
    {
        var directories = new List<string>();
        var files = new List<DownloadPlanItem>();
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in selectedItems)
        {
            await BuildDownloadPlanAsync(
                item,
                FileNameSanitizer.Sanitize(item.Name),
                directories,
                files,
                reservedPaths,
                cancellationToken);
        }

        foreach (var relativeDirectory in directories.OrderBy(path => path.Length))
        {
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativeDirectory));
        }

        var totalBytes = files.Sum(file => Math.Max(0, file.Item.Size));
        long completedBytes = 0;
        for (var index = 0; index < files.Count; index++)
        {
            var plan = files[index];
            var destination = Path.Combine(destinationDirectory, plan.RelativePath);
            var completedBeforeFile = completedBytes;
            await DownloadAsync(plan.Item, destination, value =>
            {
                var overall = totalBytes > 0
                    ? (completedBeforeFile + plan.Item.Size * value / 100d) * 100d / totalBytes
                    : (index + value / 100d) * 100d / Math.Max(1, files.Count);
                progress(plan.Item.Name, overall, totalBytes);
            }, cancellationToken);
            completedBytes += Math.Max(0, plan.Item.Size);
        }

        progress("Complete", 100, totalBytes);
    }

    public async Task UploadAsync(
        string parentId,
        string filePath,
        Action<double> progress,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            TransferBufferSize,
            true);
        using var content = new ProgressStreamContent(stream, progress);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await _httpClient.PostAsync(
            $"items/{Uri.EscapeDataString(parentId)}/upload?name={Uri.EscapeDataString(Path.GetFileName(filePath))}",
            content,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task UploadDirectoryAsync(
        string parentId,
        string directoryPath,
        Action<string, double> progress,
        CancellationToken cancellationToken = default)
    {
        var rootName = new DirectoryInfo(directoryPath).Name;
        var remoteRoot = await CreateFolderAsync(parentId, rootName, cancellationToken);
        var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
        var totalBytes = files.Sum(path => new FileInfo(path).Length);
        long completedBytes = 0;
        var remoteDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar)] = remoteRoot.Id
        };

        foreach (var localDirectory in Directory.EnumerateDirectories(
                     directoryPath,
                     "*",
                     SearchOption.AllDirectories)
                     .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar)))
        {
            var parentDirectory = Directory.GetParent(localDirectory)
                ?? throw new InvalidOperationException("A local folder has no parent.");
            var parentKey = parentDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar);
            var created = await CreateFolderAsync(
                remoteDirectories[parentKey],
                Path.GetFileName(localDirectory),
                cancellationToken);
            remoteDirectories[Path.GetFullPath(localDirectory).TrimEnd(Path.DirectorySeparatorChar)] = created.Id;
        }

        for (var index = 0; index < files.Count; index++)
        {
            var filePath = files[index];
            var fileInfo = new FileInfo(filePath);
            var parentKey = fileInfo.Directory!.FullName.TrimEnd(Path.DirectorySeparatorChar);
            var completedBeforeFile = completedBytes;
            await UploadAsync(remoteDirectories[parentKey], filePath, value =>
            {
                var overall = totalBytes > 0
                    ? (completedBeforeFile + fileInfo.Length * value / 100d) * 100d / totalBytes
                    : (index + value / 100d) * 100d / Math.Max(1, files.Count);
                progress(fileInfo.Name, overall);
            }, cancellationToken);
            completedBytes += fileInfo.Length;
        }

        progress("Complete", 100);
    }

    public async Task<RemoteItem> CreateFolderAsync(
        string parentId,
        string name,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync(
            $"items/{Uri.EscapeDataString(parentId)}/folder?name={Uri.EscapeDataString(name)}",
            null,
            cancellationToken);
        return await ReadAsync<RemoteItem>(response, cancellationToken);
    }

    public async Task RenameAsync(string itemId, string name, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"items/{Uri.EscapeDataString(itemId)}?name={Uri.EscapeDataString(name)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<RemoteItem> MoveAsync(
        string itemId,
        string destinationParentId,
        CancellationToken cancellationToken = default)
        => await MoveAsync(
            itemId,
            destinationParentId,
            "keepBoth",
            cancellationToken);

    public async Task<RemoteItem> MoveAsync(
        string itemId,
        string destinationParentId,
        string conflict,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync(
            $"items/{Uri.EscapeDataString(itemId)}/move"
            + $"?parentId={Uri.EscapeDataString(destinationParentId)}"
            + $"&conflict={Uri.EscapeDataString(conflict)}",
            null,
            cancellationToken);
        return await ReadAsync<RemoteItem>(response, cancellationToken);
    }

    public async Task<RemoteItem> CopyAsync(
        string itemId,
        string destinationParentId,
        CancellationToken cancellationToken = default)
        => await CopyAsync(
            itemId,
            destinationParentId,
            "keepBoth",
            cancellationToken);

    public async Task<RemoteItem> CopyAsync(
        string itemId,
        string destinationParentId,
        string conflict,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync(
            $"items/{Uri.EscapeDataString(itemId)}/copy"
            + $"?parentId={Uri.EscapeDataString(destinationParentId)}"
            + $"&conflict={Uri.EscapeDataString(conflict)}",
            null,
            cancellationToken);
        return await ReadAsync<RemoteItem>(response, cancellationToken);
    }

    public async Task<byte[]?> GetThumbnailAsync(
        string itemId,
        int size = 256,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"items/{Uri.EscapeDataString(itemId)}/thumbnail?size={Math.Clamp(size, 64, 512)}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<int> GetRotationAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"items/{Uri.EscapeDataString(itemId)}/rotation",
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound
            or HttpStatusCode.MethodNotAllowed)
        {
            return 0;
        }
        var metadata = await ReadAsync<RotationMetadata>(response, cancellationToken);
        var normalized = metadata.Rotation % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    public Task<HttpResponseMessage> OpenContentStreamAsync(
        string itemId,
        string? range,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"items/{Uri.EscapeDataString(itemId)}/content");
        if (!string.IsNullOrWhiteSpace(range))
        {
            request.Headers.TryAddWithoutValidation("Range", range);
        }
        return _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    public async Task DeleteAsync(string itemId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"items/{Uri.EscapeDataString(itemId)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The phone returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, cancellationToken);
            throw new RemoteApiException(
                response.StatusCode,
                error?.Code ?? "HTTP_ERROR",
                error?.Message ?? $"Phone returned HTTP {(int)response.StatusCode}.");
        }
        catch (JsonException)
        {
            throw new RemoteApiException(
                response.StatusCode,
                "HTTP_ERROR",
                $"Phone returned HTTP {(int)response.StatusCode}.");
        }
    }

    private static async Task CopyWithProgressAsync(
        Stream input,
        Stream output,
        long total,
        long alreadyCopied,
        Action<double> progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[TransferBufferSize];
        long copied = alreadyCopied;
        var lastReport = Environment.TickCount64;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            var now = Environment.TickCount64;
            if (now - lastReport >= 100)
            {
                progress(total > 0 ? copied * 100d / total : 0);
                lastReport = now;
            }
        }

        progress(100);
    }

    private async Task BuildDownloadPlanAsync(
        RemoteItem item,
        string relativePath,
        List<string> directories,
        List<DownloadPlanItem> files,
        HashSet<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        relativePath = ReserveUniquePath(relativePath, item.IsDirectory, reservedPaths);
        if (!item.IsDirectory)
        {
            files.Add(new DownloadPlanItem(item, relativePath));
            return;
        }

        directories.Add(relativePath);
        foreach (var child in await GetChildrenAsync(item.Id, cancellationToken))
        {
            await BuildDownloadPlanAsync(
                child,
                Path.Combine(relativePath, FileNameSanitizer.Sanitize(child.Name)),
                directories,
                files,
                reservedPaths,
                cancellationToken);
        }
    }

    private static string ReserveUniquePath(
        string relativePath,
        bool isDirectory,
        HashSet<string> reservedPaths)
    {
        if (reservedPaths.Add(relativePath))
        {
            return relativePath;
        }

        var parent = Path.GetDirectoryName(relativePath);
        var name = Path.GetFileName(relativePath);
        var extension = isDirectory ? string.Empty : Path.GetExtension(name);
        var stem = extension.Length == 0 ? name : name[..^extension.Length];
        for (var suffix = 2; ; suffix++)
        {
            var candidateName = $"{stem} ({suffix}){extension}";
            var candidate = string.IsNullOrEmpty(parent)
                ? candidateName
                : Path.Combine(parent, candidateName);
            if (reservedPaths.Add(candidate))
            {
                return candidate;
            }
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        if (certificate is null)
        {
            return false;
        }

        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        if (!string.IsNullOrEmpty(_expectedFingerprint)
            && !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(fingerprint),
                Convert.FromHexString(_expectedFingerprint)))
        {
            return false;
        }

        _connectedFingerprint = fingerprint;
        return true;
    }

    private static string NormalizeFingerprint(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
    }

    private sealed class ProgressStreamContent(Stream source, Action<double> progress) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[TransferBufferSize];
            long copied = 0;
            var lastReport = Environment.TickCount64;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                var now = Environment.TickCount64;
                if (now - lastReport >= 100)
                {
                    progress(source.Length > 0 ? copied * 100d / source.Length : 0);
                    lastReport = now;
                }
            }

            progress(100);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = source.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                source.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed record DownloadPlanItem(RemoteItem Item, string RelativePath);
    private sealed record RotationMetadata(int Rotation);
}
