using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhoneFolder.Desktop.Models;

public sealed class TransferJob : INotifyPropertyChanged
{
    private readonly DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private readonly CancellationTokenSource _cancellation = new();
    private double _progress;
    private string _status = "Waiting";
    private string _speed = string.Empty;
    private string _remaining = string.Empty;
    private bool _isComplete;

    public TransferJob(string name, string direction, long totalBytes)
    {
        Name = name;
        Direction = direction;
        TotalBytes = Math.Max(0, totalBytes);
    }

    public string Name { get; }
    public string Direction { get; }
    public long TotalBytes { get; }
    public string SizeLabel => FormatSize(TotalBytes);
    public CancellationToken CancellationToken => _cancellation.Token;
    public double Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }
    public string Speed
    {
        get => _speed;
        private set => SetField(ref _speed, value);
    }
    public string Remaining
    {
        get => _remaining;
        private set => SetField(ref _remaining, value);
    }
    public bool IsComplete
    {
        get => _isComplete;
        private set
        {
            if (SetField(ref _isComplete, value))
            {
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }
    public bool CanCancel => !IsComplete && !_cancellation.IsCancellationRequested;

    public void MarkRunning() => Status = "Running";

    public void Report(double progress)
    {
        Progress = Math.Clamp(progress, 0, 100);
        var elapsed = DateTimeOffset.UtcNow - _createdAt;
        if (TotalBytes <= 0 || Progress <= 0 || elapsed.TotalSeconds <= 0)
        {
            Speed = string.Empty;
            Remaining = string.Empty;
            return;
        }

        var transferred = TotalBytes * Progress / 100d;
        var bytesPerSecond = transferred / elapsed.TotalSeconds;
        Speed = FormatRate(bytesPerSecond);
        var seconds = bytesPerSecond <= 0
            ? 0
            : Math.Max(0, TotalBytes - transferred) / bytesPerSecond;
        Remaining = seconds >= 60
            ? $"{Math.Ceiling(seconds / 60):0} min"
            : $"{Math.Ceiling(seconds):0} sec";
    }

    public void Complete()
    {
        Progress = 100;
        Status = "Completed";
        Remaining = string.Empty;
        IsComplete = true;
    }

    public void Fail(string message)
    {
        Status = message;
        Remaining = string.Empty;
        IsComplete = true;
    }

    public void Cancel()
    {
        if (!CanCancel)
        {
            return;
        }
        _cancellation.Cancel();
        Status = "Cancelling";
        OnPropertyChanged(nameof(CanCancel));
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    private static string FormatRate(double bytesPerSecond) =>
        bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / 1024 / 1024:0.##} MB/s"
            : $"{bytesPerSecond / 1024:0} KB/s";

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
