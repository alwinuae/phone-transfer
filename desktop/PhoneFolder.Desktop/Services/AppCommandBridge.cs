using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace PhoneFolder.Desktop.Services;

public sealed class AppCommandBridge : IDisposable
{
    private const string NamePrefix = "PhoneTransferDesktop";

    private readonly Mutex _mutex;
    private readonly Action<IReadOnlyList<string>> _handler;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _listenerTask;
    private bool _disposed;

    private AppCommandBridge(
        Mutex mutex,
        Action<IReadOnlyList<string>> handler)
    {
        _mutex = mutex;
        _handler = handler;
        _listenerTask = Task.Run(ListenAsync);
    }

    public static bool TryCreatePrimary(
        Action<IReadOnlyList<string>> handler,
        out AppCommandBridge? bridge)
    {
        var mutex = new Mutex(
            initiallyOwned: true,
            name: MutexName,
            createdNew: out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            bridge = null;
            return false;
        }

        bridge = new AppCommandBridge(mutex, handler);
        return true;
    }

    public static async Task<bool> TrySendAsync(
        IReadOnlyList<string> args,
        TimeSpan timeout)
    {
        try
        {
            using var timeoutSource = new CancellationTokenSource(timeout);
            await using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            await client.ConnectAsync((int)Math.Max(1, timeout.TotalMilliseconds));
            await JsonSerializer.SerializeAsync(
                client,
                args.ToArray(),
                cancellationToken: timeoutSource.Token);
            await client.FlushAsync(timeoutSource.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_cancellation.Token);
                var args = await JsonSerializer.DeserializeAsync<string[]>(
                    server,
                    cancellationToken: _cancellation.Token);
                _handler(args ?? []);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), _cancellation.Token)
                    .ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        try
        {
            if (_listenerTask.Wait(TimeSpan.FromSeconds(1)))
            {
                _listenerTask.Dispose();
            }
        }
        catch
        {
        }
        _cancellation.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
        }
        _mutex.Dispose();
    }

    private static string MutexName => $@"Local\{NamePrefix}-{Scope}";

    private static string PipeName => $"{NamePrefix}-{Scope}";

    private static string Scope
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("PHONEFOLDER_INSTANCE_SCOPE");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Sanitize(configured);
            }

            var user = WindowsIdentity.GetCurrent().User?.Value
                ?? Environment.UserName
                ?? "default";
            return Sanitize(user);
        }
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string(value
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "default" : cleaned;
    }
}
