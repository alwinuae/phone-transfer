namespace PhoneFolder.Desktop.Models;

public sealed class StorageInfo
{
    public string ScopeName { get; set; } = "Shared storage";
    public long? TotalBytes { get; set; }
    public long? AvailableBytes { get; set; }
    public long? UsedBytes { get; set; }
}
