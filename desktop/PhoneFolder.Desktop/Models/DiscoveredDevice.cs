namespace PhoneFolder.Desktop.Models;

public sealed record DiscoveredDevice(
    string Name,
    string Address,
    int Port,
    string CertificateFingerprint,
    bool IsHotspot = false)
{
    public string DisplayName =>
        $"{Name} ({Address}:{Port}){(IsHotspot ? " - PC hotspot" : string.Empty)}";
}
