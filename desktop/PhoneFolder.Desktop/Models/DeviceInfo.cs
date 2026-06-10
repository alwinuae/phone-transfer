namespace PhoneFolder.Desktop.Models;

public sealed class DeviceInfo
{
    public string Name { get; set; } = "Android phone";
    public string Version { get; set; } = string.Empty;
    public int ProtocolVersion { get; set; }
    public int Port { get; set; }
    public string Transport { get; set; } = "https";
    public string CertificateFingerprint { get; set; } = string.Empty;
    public bool Sharing { get; set; }
}
