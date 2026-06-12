using PhoneFolder.Desktop.Models;
using PhoneFolder.Desktop.Services;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length < 3)
{
    Console.Error.WriteLine(
        "Usage: PhoneFolder.IntegrationTests <host> <port> <access-code> [artifact-directory]");
    return 2;
}

var host = args[0];
if (!int.TryParse(args[1], out var port))
{
    Console.Error.WriteLine("The port must be a number.");
    return 2;
}

var token = args[2];
var artifactRoot = args.Length >= 4
    ? Path.GetFullPath(args[3])
    : Path.Combine(Path.GetTempPath(), "PhoneFolderIntegration");
Directory.CreateDirectory(artifactRoot);

var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var sourceParent = Path.Combine(artifactRoot, $"source-{runId}");
var sourceDirectory = Path.Combine(sourceParent, $"PhoneFolder-Integration-{runId}");
var nestedSource = Path.Combine(sourceDirectory, "Nested");
var downloadParent = Path.Combine(artifactRoot, $"download-{runId}");
var resumeDirectory = Path.Combine(artifactRoot, $"resume-{runId}");
Directory.CreateDirectory(nestedSource);
Directory.CreateDirectory(downloadParent);
Directory.CreateDirectory(resumeDirectory);

var alphaPath = Path.Combine(sourceDirectory, "alpha.txt");
var betaPath = Path.Combine(nestedSource, "beta.bin");
var emptyPath = Path.Combine(sourceDirectory, "empty.bin");
var unicodePath = Path.Combine(nestedSource, "unicode-\u0928\u092e\u0938\u094d\u0924\u0947-\u4f60\u597d.txt");
var pdfPath = Path.Combine(sourceDirectory, "preview.pdf");
var wordPath = Path.Combine(sourceDirectory, "preview.docx");
var excelPath = Path.Combine(sourceDirectory, "preview.xlsx");
var powerPointPath = Path.Combine(sourceDirectory, "preview.pptx");
var textPreviewPath = Path.Combine(sourceDirectory, "preview-notes.txt");
var archivePath = Path.Combine(sourceDirectory, "preview.zip");
await File.WriteAllTextAsync(alphaPath, $"PhoneFolder integration test {runId}\n");
var betaBytes = new byte[2 * 1024 * 1024 + 173];
new Random(78123).NextBytes(betaBytes);
await File.WriteAllBytesAsync(betaPath, betaBytes);
await File.WriteAllBytesAsync(emptyPath, []);
await File.WriteAllTextAsync(unicodePath, "Unicode filename and contents: \u0928\u092e\u0938\u094d\u0924\u0947 \u4f60\u597d\n");
await File.WriteAllBytesAsync(pdfPath, CreateMinimalPdf());
await File.WriteAllTextAsync(wordPath, "Document thumbnail placeholder");
await File.WriteAllTextAsync(excelPath, "Spreadsheet thumbnail placeholder");
await File.WriteAllTextAsync(powerPointPath, "Presentation thumbnail placeholder");
await File.WriteAllTextAsync(textPreviewPath, "Text thumbnail placeholder");
await File.WriteAllBytesAsync(archivePath, [0x50, 0x4b, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

RemoteItem? uploadedRoot = null;
var checks = new List<string>();

try
{
    await VerifyDesktopDiscoveryAsync();
    checks.Add("Desktop UDP discovery parsed a valid responder and ignored malformed data.");

    using var bootstrapClient = new RemoteClient(host, port, token);
    var info = await bootstrapClient.GetInfoAsync();
    Require(info.Transport.Equals("https", StringComparison.OrdinalIgnoreCase), "Phone did not advertise HTTPS.");
    Require(info.CertificateFingerprint.Length >= 64, "Phone did not provide a certificate fingerprint.");
    checks.Add($"TLS certificate matched advertised SHA-256 fingerprint {info.CertificateFingerprint}.");

    var trustedToken = await bootstrapClient.TrustThisPcAsync(
        $"integration-{Guid.NewGuid():N}",
        "Phone Transfer integration test");
    using (var trustedClient = new RemoteClient(
               host,
               port,
               "",
               info.CertificateFingerprint,
               trustedToken))
    {
        Require(
            (await trustedClient.GetRootsAsync()).Count > 0,
            "A newly trusted PC could not connect without the access code.");
    }
    checks.Add("Cryptographic trusted-PC authentication worked without the access code.");

    await ExpectFailureAsync(
        async () =>
        {
            using var wrongToken = new RemoteClient(host, port, "00000000", info.CertificateFingerprint);
            await wrongToken.GetRootsAsync();
        },
        "Wrong access code was accepted.");
    checks.Add("Wrong access code was rejected.");

    await ExpectFailureAsync(
        async () =>
        {
            using var wrongTrust = new RemoteClient(
                host,
                port,
                "",
                info.CertificateFingerprint,
                "not-a-valid-trusted-token");
            await wrongTrust.GetRootsAsync();
        },
        "An invalid trusted-PC token was accepted.");
    checks.Add("Invalid trusted-PC token was rejected.");

    await ExpectFailureAsync(
        async () =>
        {
            using var wrongCertificate = new RemoteClient(host, port, token, new string('0', 64));
            await wrongCertificate.GetInfoAsync();
        },
        "Incorrect certificate fingerprint was accepted.");
    checks.Add("Incorrect certificate fingerprint was rejected.");

    using var client = new RemoteClient(host, port, token, info.CertificateFingerprint);
    var roots = await client.GetRootsAsync();
    Require(roots.Count > 0, "The phone returned no shared roots.");
    var root = roots[0];

    using (var rawClient = CreateRawClient(host, port, token))
    using (var storageResponse = await rawClient.GetAsync("storage"))
    {
        Require(storageResponse.IsSuccessStatusCode, "The authenticated storage endpoint failed.");
        using var storageJson = JsonDocument.Parse(
            await storageResponse.Content.ReadAsByteArrayAsync());
        var storageRoot = storageJson.RootElement;
        Require(
            storageRoot.GetProperty("scopeName").GetString()?.Length > 0,
            "The storage endpoint returned no scope name.");
        ValidateNullableCapacity(storageRoot, "totalBytes");
        ValidateNullableCapacity(storageRoot, "availableBytes");
        ValidateNullableCapacity(storageRoot, "usedBytes");
        if (storageRoot.GetProperty("totalBytes").ValueKind == JsonValueKind.Number
                && storageRoot.GetProperty("availableBytes").ValueKind == JsonValueKind.Number
                && storageRoot.GetProperty("usedBytes").ValueKind == JsonValueKind.Number)
        {
            var total = storageRoot.GetProperty("totalBytes").GetInt64();
            var available = storageRoot.GetProperty("availableBytes").GetInt64();
            var used = storageRoot.GetProperty("usedBytes").GetInt64();
            Require(total >= available, "Storage available bytes exceeded total bytes.");
            Require(used == total - available, "Storage used bytes did not equal total minus available.");
        }
    }
    checks.Add("Authenticated storage utilization returned a valid known-or-null capacity model.");

    await client.UploadDirectoryAsync(root.Id, sourceDirectory, (_, _) => { });
    var rootChildren = await client.GetChildrenAsync(root.Id);
    uploadedRoot = rootChildren.SingleOrDefault(item =>
        item.IsDirectory && item.Name.Equals(Path.GetFileName(sourceDirectory), StringComparison.Ordinal));
    var uploadedRootItem = uploadedRoot
        ?? throw new InvalidOperationException("The recursively uploaded folder was not listed.");
    checks.Add("Recursive folder upload completed.");

    await client.DownloadSelectionAsync([uploadedRootItem], downloadParent, (_, _) => { });
    var downloadedRoot = Path.Combine(downloadParent, uploadedRootItem.Name);
    var downloadedAlpha = Path.Combine(downloadedRoot, "alpha.txt");
    var downloadedBeta = Path.Combine(downloadedRoot, "Nested", "beta.bin");
    var downloadedEmpty = Path.Combine(downloadedRoot, "empty.bin");
    var downloadedUnicode = Path.Combine(downloadedRoot, "Nested", Path.GetFileName(unicodePath));
    Require(File.Exists(downloadedAlpha), "Downloaded alpha.txt is missing.");
    Require(File.Exists(downloadedBeta), "Downloaded nested beta.bin is missing.");
    Require(File.Exists(downloadedEmpty), "Downloaded empty.bin is missing.");
    Require(File.Exists(downloadedUnicode), "Downloaded Unicode file is missing.");
    Require(Hash(alphaPath) == Hash(downloadedAlpha), "alpha.txt hash mismatch.");
    Require(Hash(betaPath) == Hash(downloadedBeta), "beta.bin hash mismatch.");
    Require(new FileInfo(downloadedEmpty).Length == 0, "Empty-file download was not empty.");
    Require(Hash(unicodePath) == Hash(downloadedUnicode), "Unicode file hash mismatch.");
    checks.Add("Recursive download preserved binary, empty, and Unicode-named files.");

    var uploadedChildren = await client.GetChildrenAsync(uploadedRootItem.Id);
    var uploadedAlpha = uploadedChildren.Single(item => item.Name == "alpha.txt");
    var uploadedNested = uploadedChildren.Single(item => item.IsDirectory && item.Name == "Nested");
    var nestedChildren = await client.GetChildrenAsync(uploadedNested.Id);
    var uploadedBeta = nestedChildren.Single(item => item.Name == "beta.bin");

    await using (var mediaServer = new RemoteMediaServer(client, uploadedBeta))
    using (var localPlayer = new HttpClient())
    using (var streamRequest = new HttpRequestMessage(HttpMethod.Get, mediaServer.StreamUri))
    {
        var streamOffset = betaBytes.Length / 3;
        var streamEnd = streamOffset + 4095;
        streamRequest.Headers.Range =
            new System.Net.Http.Headers.RangeHeaderValue(streamOffset, streamEnd);
        using var streamResponse = await localPlayer.SendAsync(streamRequest);
        Require(
            streamResponse.StatusCode == HttpStatusCode.PartialContent,
            "The local direct-playback proxy did not preserve byte-range responses.");
        var streamedBytes = await streamResponse.Content.ReadAsByteArrayAsync();
        Require(
            streamedBytes.AsSpan().SequenceEqual(
                betaBytes.AsSpan(streamOffset, streamEnd - streamOffset + 1)),
            "The direct-playback proxy changed streamed content.");
    }
    checks.Add("Authenticated loopback playback streamed remote byte ranges without a local file.");

    using (var unauthenticatedClient = CreateRawClient(host, port))
    using (var response = await unauthenticatedClient.GetAsync("roots"))
    {
        Require(response.StatusCode == HttpStatusCode.Unauthorized, "A protected endpoint accepted no access code.");
    }
    using (var unauthenticatedClient = CreateRawClient(host, port))
    using (var response = await unauthenticatedClient.GetAsync("storage"))
    {
        Require(
            response.StatusCode == HttpStatusCode.Unauthorized,
            "The storage utilization endpoint accepted no access code.");
    }

    using (var rawClient = CreateRawClient(host, port, token))
    {
        await RequireStatusAsync(
            rawClient.GetAsync("items/not-a-real-item/children"),
            HttpStatusCode.NotFound,
            "A missing item did not return 404.");
        await RequireStatusAsync(
            rawClient.DeleteAsync("items/root"),
            HttpStatusCode.BadRequest,
            "The shared root could be deleted.");
        await RequireStatusAsync(
            rawClient.PostAsync($"items/{uploadedRootItem.Id}/folder?name=.", null),
            HttpStatusCode.BadRequest,
            "An invalid dot folder name was accepted.");
        await RequireStatusAsync(
            rawClient.PostAsync(
                $"items/{uploadedNested.Id}/copy?parentId={Uri.EscapeDataString(uploadedNested.Id)}",
                null),
            HttpStatusCode.BadRequest,
            "A folder was allowed to copy into itself.");
        await RequireStatusAsync(
            rawClient.PostAsync(
                $"items/{uploadedNested.Id}/move?parentId={Uri.EscapeDataString(uploadedNested.Id)}",
                null),
            HttpStatusCode.BadRequest,
            "A folder was allowed to move into itself.");
        using (var unsupported = new HttpRequestMessage(HttpMethod.Put, $"items/{uploadedRootItem.Id}"))
        {
            await RequireStatusAsync(
                rawClient.SendAsync(unsupported),
                HttpStatusCode.MethodNotAllowed,
                "An unsupported method did not return 405.");
        }

        using (var invalidRange = new HttpRequestMessage(
                   HttpMethod.Get,
                   $"items/{uploadedBeta.Id}/content"))
        {
            invalidRange.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(uploadedBeta.Size, null);
            await RequireStatusAsync(
                rawClient.SendAsync(invalidRange),
                HttpStatusCode.BadRequest,
                "An unavailable byte range was accepted.");
        }

        var offset = uploadedBeta.Size / 2;
        using (var validRange = new HttpRequestMessage(
                   HttpMethod.Get,
                   $"items/{uploadedBeta.Id}/content"))
        {
            validRange.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
            using var response = await rawClient.SendAsync(validRange);
            Require(response.StatusCode == HttpStatusCode.PartialContent, "A valid range did not return 206.");
            var rangedBytes = await response.Content.ReadAsByteArrayAsync();
            Require(
                rangedBytes.AsSpan().SequenceEqual(betaBytes.AsSpan((int)offset)),
                "Valid range contents did not match the source suffix.");
        }

        var reservedPayload = Encoding.UTF8.GetBytes("Windows reserved filename test\n");
        using var reservedContent = new ByteArrayContent(reservedPayload);
        using var reservedResponse = await rawClient.PostAsync(
            $"items/{uploadedRootItem.Id}/upload?name=NUL",
            reservedContent);
        Require(reservedResponse.StatusCode == HttpStatusCode.Created, "The provider rejected the NUL test item.");
        var reservedItem = await reservedResponse.Content.ReadFromJsonAsync<RemoteItem>()
            ?? throw new InvalidDataException("The NUL upload returned no metadata.");
        Require(reservedItem.Name == "NUL", "The provider changed the NUL test name.");

        var reservedDownload = Path.Combine(artifactRoot, $"reserved-name-{runId}");
        Directory.CreateDirectory(reservedDownload);
        await client.DownloadSelectionAsync([reservedItem], reservedDownload, (_, _) => { });
        var sanitizedReservedPath = Path.Combine(reservedDownload, "_NUL");
        Require(File.Exists(sanitizedReservedPath), "The Windows-reserved NUL name was not sanitized.");
        Require(
            (await File.ReadAllBytesAsync(sanitizedReservedPath)).AsSpan().SequenceEqual(reservedPayload),
            "The sanitized reserved-name download changed file contents.");
        checks.Add("Malformed requests were rejected and Windows-reserved names downloaded safely.");

        var truncatedName = $"truncated-{runId}.bin";
        await SendTruncatedUploadAsync(host, port, token, uploadedRootItem.Id, truncatedName);
        await Task.Delay(500);
        uploadedChildren = await client.GetChildrenAsync(uploadedRootItem.Id);
        Require(
            uploadedChildren.All(item => item.Name != truncatedName),
            "A truncated upload left a partial remote file.");
        checks.Add("A disconnected short upload was rolled back.");
    }

    var concurrentRoots = await Task.WhenAll(
        Enumerable.Range(0, 12).Select(_ => client.GetRootsAsync()));
    Require(concurrentRoots.All(items => items.Count > 0), "A concurrent root request failed.");
    checks.Add("Concurrent authenticated requests completed without listener failure.");

    using (var persistentClient = CreateRawClient(host, port, token))
    {
        for (var index = 0; index < 3; index++)
        {
            using var response = await persistentClient.GetAsync("roots");
            Require(response.IsSuccessStatusCode, "A persistent authenticated request failed.");
            Require(
                response.Headers.Connection.Any(value =>
                    value.Equals("keep-alive", StringComparison.OrdinalIgnoreCase)),
                "The Android server did not advertise an HTTP keep-alive connection.");
            await response.Content.ReadAsByteArrayAsync();
        }
    }
    checks.Add("HTTP/1.1 keep-alive remained available across consecutive requests.");

    var resumedDestination = Path.Combine(resumeDirectory, "beta-resumed.bin");
    var partialPath = resumedDestination + ".phonefolder-part";
    await File.WriteAllBytesAsync(partialPath, betaBytes[..(betaBytes.Length / 2)]);
    await client.DownloadAsync(uploadedBeta, resumedDestination, _ => { });
    Require(Hash(betaPath) == Hash(resumedDestination), "Resumed download hash mismatch.");
    Require(!File.Exists(partialPath), "Partial file remained after successful resume.");
    checks.Add("Interrupted download resumed from its existing partial file.");

    var copyTarget = await client.CreateFolderAsync(uploadedRootItem.Id, "Copy target");
    var copiedNested = await client.CopyAsync(uploadedNested.Id, copyTarget.Id);
    var copiedNestedChildren = await client.GetChildrenAsync(copiedNested.Id);
    var copiedBeta = copiedNestedChildren.SingleOrDefault(item => item.Name == "beta.bin")
        ?? throw new InvalidOperationException("The recursively copied file was not listed.");
    var copiedDestination = Path.Combine(artifactRoot, $"copied-beta-{runId}.bin");
    await client.DownloadAsync(copiedBeta, copiedDestination, _ => { });
    Require(Hash(betaPath) == Hash(copiedDestination), "Copying the remote folder changed file contents.");
    var copiedNestedAgain = await client.CopyAsync(uploadedNested.Id, copyTarget.Id);
    Require(
        copiedNestedAgain.Name == "Nested (2)",
        $"Repeated folder copy used '{copiedNestedAgain.Name}' instead of Explorer-style keep-both naming.");
    var copiedNestedAgainChildren = await client.GetChildrenAsync(copiedNestedAgain.Id);
    Require(
        copiedNestedAgainChildren.Any(item => item.Name == "beta.bin"),
        "The keep-both folder copy did not preserve its nested file.");

    var sameFolderCopy = await client.CopyAsync(uploadedAlpha.Id, uploadedRootItem.Id);
    Require(
        sameFolderCopy.Name == "alpha (2).txt",
        $"Same-folder copy used '{sameFolderCopy.Name}' instead of 'alpha (2).txt'.");
    var sameFolderMove = await client.MoveAsync(uploadedAlpha.Id, uploadedRootItem.Id);
    Require(
        sameFolderMove.Id == uploadedAlpha.Id && sameFolderMove.Name == uploadedAlpha.Name,
        "Moving an item within its current folder was not a no-op.");
    checks.Add("Copy conflicts used deterministic keep-both names and same-folder move was a no-op.");

    var moveTarget = await client.CreateFolderAsync(uploadedRootItem.Id, "Move target");
    var conflictingBeta = await client.CopyAsync(uploadedBeta.Id, moveTarget.Id);
    Require(conflictingBeta.Name == "beta.bin", "The move-conflict fixture used an unexpected name.");
    var movedResult = await client.MoveAsync(uploadedBeta.Id, moveTarget.Id);
    Require(
        movedResult.Name == "beta (2).bin",
        $"Conflicting move used '{movedResult.Name}' instead of 'beta (2).bin'.");
    nestedChildren = await client.GetChildrenAsync(uploadedNested.Id);
    var movedChildren = await client.GetChildrenAsync(moveTarget.Id);
    var movedBeta = movedChildren.SingleOrDefault(item => item.Name == "beta (2).bin")
        ?? throw new InvalidOperationException("The moved file was not listed in its destination.");
    Require(
        nestedChildren.All(item => item.Id != uploadedBeta.Id),
        "The moved file remained listed in its source folder.");
    var movedDestination = Path.Combine(artifactRoot, $"moved-beta-{runId}.bin");
    await client.DownloadAsync(movedBeta, movedDestination, _ => { });
    Require(Hash(betaPath) == Hash(movedDestination), "Moving the remote file changed its contents.");
    Require(
        movedChildren.Count(item => item.Name.StartsWith("beta", StringComparison.Ordinal)) == 2,
        "The move conflict did not preserve both destination files.");
    checks.Add("Conflicting remote move kept both files without changing their contents.");

    var previewPath = Path.Combine(artifactRoot, $"thumbnail-{runId}.png");
    await File.WriteAllBytesAsync(
        previewPath,
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwC"
            + "AAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
    await client.UploadAsync(uploadedRootItem.Id, previewPath, _ => { });
    uploadedChildren = await client.GetChildrenAsync(uploadedRootItem.Id);
    var previewItem = uploadedChildren.Single(item => item.Name == Path.GetFileName(previewPath));
    var thumbnail = await client.GetThumbnailAsync(previewItem.Id);
    Require(
        thumbnail is { Length: > 2 } && thumbnail[0] == 0xff && thumbnail[1] == 0xd8,
        "The image thumbnail endpoint did not return a JPEG.");
    checks.Add("Image thumbnail generation returned a valid JPEG preview.");
    uploadedChildren = await client.GetChildrenAsync(uploadedRootItem.Id);
    foreach (var documentName in new[]
             {
                 Path.GetFileName(pdfPath),
                 Path.GetFileName(wordPath),
                 Path.GetFileName(excelPath),
                 Path.GetFileName(powerPointPath),
                 Path.GetFileName(textPreviewPath),
                 Path.GetFileName(archivePath)
             })
    {
        var documentItem = uploadedChildren.Single(item => item.Name == documentName);
        var documentThumbnail = await client.GetThumbnailAsync(documentItem.Id);
        Require(
            documentThumbnail is { Length: > 2 }
            && documentThumbnail[0] == 0xff
            && documentThumbnail[1] == 0xd8,
            $"The {documentName} thumbnail endpoint did not return a JPEG.");
    }
    checks.Add("PDF, Word, Excel, PowerPoint, text, and archive previews returned JPEG thumbnails.");
    Require(
        await client.GetRotationAsync(previewItem.Id) == 0,
        "Non-video rotation metadata was not normalized to zero.");
    checks.Add("Media rotation metadata endpoint returned a normalized value.");

    await client.RenameAsync(uploadedAlpha.Id, "alpha-renamed.txt");
    uploadedChildren = await client.GetChildrenAsync(uploadedRootItem.Id);
    Require(
        uploadedChildren.Any(item => item.Name == "alpha-renamed.txt")
        && uploadedChildren.All(item => item.Name != "alpha.txt"),
        "Remote rename did not take effect.");
    checks.Add("Remote rename completed.");

    await client.DeleteAsync(uploadedRootItem.Id);
    uploadedRoot = null;
    rootChildren = await client.GetChildrenAsync(root.Id);
    Require(
        rootChildren.All(item => item.Name != Path.GetFileName(sourceDirectory)),
        "Remote folder remained after delete.");
    checks.Add("Recursive remote folder deletion completed.");

    Console.WriteLine("PhoneFolder integration verification passed:");
    foreach (var check in checks)
    {
        Console.WriteLine($"  PASS: {check}");
    }
    Console.WriteLine($"  Artifacts: {artifactRoot}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"PhoneFolder integration verification failed: {exception}");
    return 1;
}
finally
{
    if (uploadedRoot is not null)
    {
        try
        {
            using var cleanupClient = new RemoteClient(host, port, token);
            await cleanupClient.DeleteAsync(uploadedRoot.Id);
        }
        catch
        {
            // The failed run's unique folder can be removed manually from the shared test root.
        }
    }
}

static async Task ExpectFailureAsync(Func<Task> action, string failureMessage)
{
    try
    {
        await action();
    }
    catch
    {
        return;
    }
    throw new InvalidOperationException(failureMessage);
}

static HttpClient CreateRawClient(string host, int port, string? token = null)
{
    var normalizedHost = host.StartsWith('[') || !host.Contains(':') ? host : $"[{host}]";
    var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        UseProxy = false
    };
    var client = new HttpClient(handler)
    {
        BaseAddress = new Uri($"https://{normalizedHost}:{port}/api/v1/"),
        Timeout = TimeSpan.FromSeconds(30)
    };
    if (!string.IsNullOrEmpty(token))
    {
        client.DefaultRequestHeaders.Add("X-PhoneFolder-Token", token);
    }
    return client;
}

static async Task RequireStatusAsync(
    Task<HttpResponseMessage> responseTask,
    HttpStatusCode expected,
    string message)
{
    using var response = await responseTask;
    Require(response.StatusCode == expected, $"{message} Received HTTP {(int)response.StatusCode}.");
}

static async Task SendTruncatedUploadAsync(
    string host,
    int port,
    string token,
    string parentId,
    string name)
{
    using var tcp = new TcpClient();
    await tcp.ConnectAsync(host, port);
    using var tls = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
    await tls.AuthenticateAsClientAsync(host);
    var target = $"/api/v1/items/{Uri.EscapeDataString(parentId)}/upload"
        + $"?name={Uri.EscapeDataString(name)}";
    var request = $"POST {target} HTTP/1.1\r\n"
        + $"Host: {host}:{port}\r\n"
        + $"X-PhoneFolder-Token: {token}\r\n"
        + "Content-Type: application/octet-stream\r\n"
        + "Content-Length: 128\r\n"
        + "Connection: close\r\n\r\n"
        + "short";
    await tls.WriteAsync(Encoding.ASCII.GetBytes(request));
    await tls.FlushAsync();
}

static async Task VerifyDesktopDiscoveryAsync()
{
    using var responder = new UdpClient(AddressFamily.InterNetwork);
    responder.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    responder.Client.Bind(new IPEndPoint(IPAddress.Any, 8766));
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    IPAddress? responseSource = null;
    var responderTask = Task.Run(async () =>
    {
        while (true)
        {
            var request = await responder.ReceiveAsync(cancellation.Token);
            var text = Encoding.UTF8.GetString(request.Buffer);
            if (text != "PHONEFOLDER_DISCOVER_V1")
            {
                continue;
            }

            responseSource = request.RemoteEndPoint.Address;
            var malformed = Encoding.UTF8.GetBytes("not-a-phone");
            await responder.SendAsync(malformed, request.RemoteEndPoint, cancellation.Token);
            var valid = Encoding.UTF8.GetBytes(
                "PHONEFOLDER_V1|QA Phone|203.0.113.77|8765|"
                + new string('A', 64));
            await responder.SendAsync(valid, request.RemoteEndPoint, cancellation.Token);
            return;
        }
    }, cancellation.Token);

    var discovery = new DiscoveryService();
    var devices = await discovery.DiscoverAsync(TimeSpan.FromSeconds(1));
    await responderTask;
    var synthetic = devices.SingleOrDefault(device => device.Name == "QA Phone");
    Require(
        synthetic is not null
        && synthetic.Address == responseSource?.ToString()
        && synthetic.Address != "203.0.113.77"
        && synthetic.Port == 8765
        && synthetic.CertificateFingerprint == new string('A', 64),
        "Desktop discovery did not return the valid synthetic device.");
}

static string Hash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static void ValidateNullableCapacity(JsonElement storage, string propertyName)
{
    var value = storage.GetProperty(propertyName);
    Require(
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Number,
        $"Storage {propertyName} was neither a number nor null.");
    if (value.ValueKind == JsonValueKind.Number)
    {
        Require(value.GetInt64() >= 0, $"Storage {propertyName} was negative.");
    }
}

static byte[] CreateMinimalPdf()
{
    var streamText = "BT /F1 22 Tf 36 110 Td (Phone Transfer PDF preview) Tj ET\n";
    var objects = new[]
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 320 180] "
            + "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
        $"<< /Length {Encoding.ASCII.GetByteCount(streamText)} >>\nstream\n{streamText}endstream",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
    };

    using var output = new MemoryStream();
    WriteAscii(output, "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
    var offsets = new List<long> { 0 };
    for (var index = 0; index < objects.Length; index++)
    {
        offsets.Add(output.Position);
        WriteAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
    }

    var xrefOffset = output.Position;
    WriteAscii(output, $"xref\n0 {objects.Length + 1}\n");
    WriteAscii(output, "0000000000 65535 f \n");
    for (var index = 1; index < offsets.Count; index++)
    {
        WriteAscii(output, $"{offsets[index]:D10} 00000 n \n");
    }
    WriteAscii(
        output,
        $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\n"
        + $"startxref\n{xrefOffset}\n%%EOF\n");
    return output.ToArray();
}

static void WriteAscii(Stream output, string value)
{
    var bytes = Encoding.Latin1.GetBytes(value);
    output.Write(bytes);
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
