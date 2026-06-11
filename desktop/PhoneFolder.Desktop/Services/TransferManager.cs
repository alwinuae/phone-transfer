using PhoneFolder.Desktop.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

namespace PhoneFolder.Desktop.Services;

public sealed class TransferManager
{
    private readonly SemaphoreSlim _slots = new(3, 3);

    private TransferManager()
    {
        Jobs.CollectionChanged += Jobs_CollectionChanged;
    }

    public static TransferManager Instance { get; } = new();
    public ObservableCollection<TransferJob> Jobs { get; } = [];
    public int ActiveCount => Jobs.Count(job => !job.IsComplete);
    public event EventHandler? Changed;

    public TransferJob Enqueue(
        RemoteClient sourceClient,
        string name,
        string direction,
        long totalBytes,
        Func<RemoteClient, Action<double>, CancellationToken, Task> operation,
        Action? completed = null)
    {
        var job = new TransferJob(name, direction, totalBytes);
        job.PropertyChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        RunOnUi(() => Jobs.Insert(0, job));
        _ = RunAsync(sourceClient.CreateSibling(), job, operation, completed);
        return job;
    }

    public void ClearCompleted()
    {
        RunOnUi(() =>
        {
            foreach (var job in Jobs.Where(item => item.IsComplete).ToArray())
            {
                Jobs.Remove(job);
            }
        });
    }

    private async Task RunAsync(
        RemoteClient client,
        TransferJob job,
        Func<RemoteClient, Action<double>, CancellationToken, Task> operation,
        Action? completed)
    {
        var entered = false;
        try
        {
            await _slots.WaitAsync(job.CancellationToken);
            entered = true;
            RunOnUi(job.MarkRunning);
            await operation(
                client,
                value => RunOnUi(() => job.Report(value)),
                job.CancellationToken);
            RunOnUi(() =>
            {
                job.Complete();
                completed?.Invoke();
            });
        }
        catch (OperationCanceledException)
        {
            RunOnUi(() => job.Fail("Cancelled"));
        }
        catch (Exception exception)
        {
            RunOnUi(() => job.Fail($"Failed: {exception.Message}"));
        }
        finally
        {
            if (entered)
            {
                _slots.Release();
            }
            client.Dispose();
        }
    }

    private void Jobs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Changed?.Invoke(this, EventArgs.Empty);

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
