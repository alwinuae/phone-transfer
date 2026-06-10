using PhoneFolder.Desktop.Models;
using PhoneFolder.Desktop.Services;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length < 3)
{
    Console.Error.WriteLine(
        "Usage: PhoneFolder.PerformanceTests <host> <port> <access-code> [artifact-directory] [source-file]");
    return 2;
}

if (!int.TryParse(args[1], out var port))
{
    Console.Error.WriteLine("The port must be a number.");
    return 2;
}

var artifactRoot = args.Length >= 4
    ? Path.GetFullPath(args[3])
    : Path.Combine(Path.GetTempPath(), "PhoneFolderPerformance");
Directory.CreateDirectory(artifactRoot);

const int repetitions = 3;
var sourcePath = args.Length >= 5
    ? Path.GetFullPath(args[4])
    : Path.Combine(artifactRoot, "five-megabytes.bin");
if (args.Length < 5 && (!File.Exists(sourcePath) || new FileInfo(sourcePath).Length != 5 * 1024 * 1024))
{
    var bytes = new byte[5 * 1024 * 1024];
    new Random(5032026).NextBytes(bytes);
    await File.WriteAllBytesAsync(sourcePath, bytes);
}
if (!File.Exists(sourcePath))
{
    Console.Error.WriteLine($"Source file not found: {sourcePath}");
    return 2;
}
var byteCount = new FileInfo(sourcePath).Length;

var sourceHash = Hash(sourcePath);
var samples = new List<Sample>();
RemoteItem? testFolder = null;

try
{
    using var bootstrap = new RemoteClient(args[0], port, args[2]);
    var info = await bootstrap.GetInfoAsync();
    using var client = new RemoteClient(args[0], port, args[2], info.CertificateFingerprint);
    var root = (await client.GetRootsAsync()).First();
    testFolder = await client.CreateFolderAsync(
        root.Id,
        $"PhoneFolder-Performance-{DateTime.UtcNow:yyyyMMdd-HHmmss}");

    for (var iteration = 1; iteration <= repetitions; iteration++)
    {
        var uploadWatch = Stopwatch.StartNew();
        await client.UploadAsync(testFolder.Id, sourcePath, _ => { });
        uploadWatch.Stop();

        var uploaded = (await client.GetChildrenAsync(testFolder.Id))
            .Where(item => !item.IsDirectory && item.Name == Path.GetFileName(sourcePath))
            .OrderByDescending(item => item.ModifiedAt)
            .First();
        samples.Add(CreateSample("upload", iteration, uploadWatch.Elapsed, byteCount));
        if (iteration == 1
            && Path.GetExtension(sourcePath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            var thumbnail = await client.GetThumbnailAsync(uploaded.Id);
            if (thumbnail is not { Length: > 2 }
                || thumbnail[0] != 0xff
                || thumbnail[1] != 0xd8)
            {
                throw new InvalidDataException("The uploaded video did not produce a JPEG thumbnail.");
            }
        }

        var destination = Path.Combine(artifactRoot, $"download-{iteration}.bin");
        var downloadWatch = Stopwatch.StartNew();
        await client.DownloadAsync(uploaded, destination, _ => { });
        downloadWatch.Stop();
        if (Hash(destination) != sourceHash)
        {
            throw new InvalidDataException($"Downloaded file {iteration} failed its SHA-256 check.");
        }
        samples.Add(CreateSample("download", iteration, downloadWatch.Elapsed, byteCount));
        await client.DeleteAsync(uploaded.Id);
    }

    var report = new Report(
        DateTimeOffset.UtcNow,
        info.Name,
        byteCount,
        samples,
        Average("upload"),
        Average("download"));
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(Path.Combine(artifactRoot, "results.json"), json);
    Console.WriteLine(json);
}
finally
{
    if (testFolder is not null)
    {
        try
        {
            using var cleanup = new RemoteClient(args[0], port, args[2]);
            await cleanup.DeleteAsync(testFolder.Id);
        }
        catch
        {
        }
    }
}

return 0;

double Average(string operation) => samples
    .Where(sample => sample.Operation == operation)
    .Average(sample => sample.MebibytesPerSecond);

static Sample CreateSample(string operation, int iteration, TimeSpan elapsed, long bytes)
{
    var speed = bytes / 1024d / 1024d / elapsed.TotalSeconds;
    return new Sample(operation, iteration, elapsed.TotalMilliseconds, speed);
}

static string Hash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

sealed record Sample(
    string Operation,
    int Iteration,
    double ElapsedMilliseconds,
    double MebibytesPerSecond);

sealed record Report(
    DateTimeOffset RunAt,
    string Device,
    long BytesPerTransfer,
    IReadOnlyList<Sample> Samples,
    double AverageUploadMebibytesPerSecond,
    double AverageDownloadMebibytesPerSecond);
