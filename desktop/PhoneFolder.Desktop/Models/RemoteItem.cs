using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace PhoneFolder.Desktop.Models;

public sealed class RemoteItem : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private bool _isChecked;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public long ModifiedAt { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public bool CanWrite { get; set; }
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }
            _isChecked = value;
            OnPropertyChanged();
        }
    }
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    public string TypeLabel => IsDirectory ? "Folder" : string.IsNullOrWhiteSpace(MimeType) ? "File" : MimeType;
    public bool IsVideo => !IsDirectory
        && MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
    public bool IsImage => !IsDirectory
        && MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public bool IsAudio => !IsDirectory
        && MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
    public bool IsMedia => IsImage || IsVideo || IsAudio;
    public bool SupportsThumbnail => !IsDirectory
        && (MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase));
    public string SizeLabel => IsDirectory ? string.Empty : FormatSize(Size);
    public string ModifiedLabel => ModifiedAt <= 0
        ? string.Empty
        : DateTimeOffset.FromUnixTimeMilliseconds(ModifiedAt).LocalDateTime.ToString("g");

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
