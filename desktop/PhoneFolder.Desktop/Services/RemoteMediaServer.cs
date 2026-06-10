using PhoneFolder.Desktop.Models;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PhoneFolder.Desktop.Services;

public sealed class RemoteMediaServer : IAsyncDisposable
{
    private readonly RemoteClient _client;
    private readonly RemoteItem _item;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _routeToken = Guid.NewGuid().ToString("N");
    private readonly Task _acceptLoop;

    public RemoteMediaServer(RemoteClient client, RemoteItem item)
    {
        _client = client;
        _item = item;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        StreamUri = new Uri(
            $"http://127.0.0.1:{port}/{_routeToken}/{Uri.EscapeDataString(item.Name)}");
        _acceptLoop = AcceptLoopAsync(_cancellation.Token);
    }

    public Uri StreamUri { get; }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var socket = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleAsync(socket, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task HandleAsync(TcpClient socket, CancellationToken cancellationToken)
    {
        using (socket)
        await using (var stream = socket.GetStream())
        {
            try
            {
                var request = await ReadRequestAsync(stream, cancellationToken);
                if (request is null
                    || (!request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                        && !request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                    || !request.Path.StartsWith($"/{_routeToken}/", StringComparison.Ordinal))
                {
                    await WriteSimpleResponseAsync(
                        stream,
                        404,
                        "Not Found",
                        cancellationToken);
                    return;
                }

                request.Headers.TryGetValue("Range", out var range);
                using var response = await _client.OpenContentStreamAsync(
                    _item.Id,
                    range,
                    cancellationToken);
                var reason = response.ReasonPhrase ?? "OK";
                var header = new StringBuilder()
                    .Append("HTTP/1.1 ")
                    .Append((int)response.StatusCode)
                    .Append(' ')
                    .Append(reason)
                    .Append("\r\n")
                    .Append("Content-Type: ")
                    .Append(string.IsNullOrWhiteSpace(_item.MimeType)
                        ? "application/octet-stream"
                        : _item.MimeType)
                    .Append("\r\n")
                    .Append("Accept-Ranges: bytes\r\n")
                    .Append("Connection: close\r\n");
                if (response.Content.Headers.ContentLength is long length)
                {
                    header.Append("Content-Length: ").Append(length).Append("\r\n");
                }
                if (response.Content.Headers.ContentRange is { } contentRange)
                {
                    header.Append("Content-Range: ")
                        .Append(contentRange)
                        .Append("\r\n");
                }
                header.Append("\r\n");
                await stream.WriteAsync(
                    Encoding.ASCII.GetBytes(header.ToString()),
                    cancellationToken);
                if (request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
                await remote.CopyToAsync(stream, 1024 * 1024, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                try
                {
                    await WriteSimpleResponseAsync(
                        stream,
                        502,
                        "Bad Gateway",
                        CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }

    private static async Task<Request?> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            false,
            4096,
            leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return null;
        }
        var parts = requestLine.Split(' ', 3);
        if (parts.Length < 2)
        {
            return null;
        }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
            {
                break;
            }
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }
        return new Request(parts[0], parts[1], headers);
    }

    private static Task WriteSimpleResponseAsync(
        NetworkStream stream,
        int status,
        string reason,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        return stream.WriteAsync(bytes, cancellationToken).AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch
        {
        }
        _cancellation.Dispose();
    }

    private sealed record Request(
        string Method,
        string Path,
        IReadOnlyDictionary<string, string> Headers);
}
