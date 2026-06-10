using PhoneFolder.Desktop.Services;
using System.Net;
using System.Net.Sockets;

using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
var accept = listener.AcceptTcpClientAsync();
var success = await NetworkDiagnostics.ProbeAsync(
    "127.0.0.1",
    port,
    TimeSpan.FromSeconds(2));
Require(success.Success, $"Loopback listener probe failed: {success.Message}");
using (await accept)
{
}
listener.Stop();

var closed = await NetworkDiagnostics.ProbeAsync(
    "127.0.0.1",
    port,
    TimeSpan.FromSeconds(2));
Require(!closed.Success, "A closed loopback port was reported as reachable.");
Require(
    !string.IsNullOrWhiteSpace(closed.Message),
    "A closed loopback port returned no connection guidance.");

Console.WriteLine($"PASS: Reachable and failed-port network diagnostics ({closed.SocketError}).");

var hotspot = HotspotService.GetStatus();
Console.WriteLine(hotspot.Active
    ? $"PC hotspot active: {hotspot.InterfaceName} {hotspot.Address}/{hotspot.PrefixLength}."
    : "PC hotspot inactive: hotspot-only discovery will wait until Windows Mobile Hotspot is enabled.");
if (!hotspot.Active)
{
    var hotspotDevices = await new DiscoveryService().DiscoverAsync(
        TimeSpan.FromMilliseconds(200),
        hotspotOnly: true);
    Require(hotspotDevices.Count == 0, "Hotspot-only discovery used a non-hotspot adapter.");
    Console.WriteLine("PASS: Hotspot-only discovery ignored router and VPN adapters.");
}

var credentialTarget = $"PhoneFolder/NetworkTest-{Guid.NewGuid():N}";
Environment.SetEnvironmentVariable("PHONEFOLDER_CREDENTIAL_TARGET", credentialTarget);
try
{
    var expectedProfile = new RememberedConnection(
        "192.0.2.10",
        8765,
        "12345678",
        new string('A', 64),
        "Credential QA");
    ConnectionProfileStore.Save(expectedProfile);
    var loadedProfile = ConnectionProfileStore.Load();
    Require(loadedProfile == expectedProfile, "Windows Credential Manager changed the remembered profile.");
    var secondProfile = expectedProfile with
    {
        Host = "192.0.2.11",
        CertificateFingerprint = new string('B', 64),
        DeviceName = "Second Phone",
        TrustedToken = "trusted-second",
        LastConnectedAt = DateTimeOffset.UtcNow
    };
    ConnectionProfileStore.Save(secondProfile);
    var profiles = ConnectionProfileStore.LoadAll();
    Require(profiles.Count == 2, "Multiple trusted phone profiles were not retained.");
    ConnectionProfileStore.Delete(expectedProfile.CertificateFingerprint);
    profiles = ConnectionProfileStore.LoadAll();
    Require(
        profiles.Count == 1 && profiles[0].CertificateFingerprint == secondProfile.CertificateFingerprint,
        "Deleting one trusted phone removed the wrong profile.");
    ConnectionProfileStore.Delete();
    Require(ConnectionProfileStore.Load() is null, "The remembered profile was not deleted.");
    Console.WriteLine("PASS: Multiple trusted phone profiles were securely saved, switched, and deleted.");
}
finally
{
    try
    {
        ConnectionProfileStore.Delete();
    }
    catch
    {
    }
    Environment.SetEnvironmentVariable("PHONEFOLDER_CREDENTIAL_TARGET", null);
}

if (args.Length >= 2 && int.TryParse(args[1], out var diagnosticPort))
{
    var diagnostic = await NetworkDiagnostics.ProbeAsync(
        args[0],
        diagnosticPort,
        TimeSpan.FromSeconds(5));
    Console.WriteLine($"Target: {args[0]}:{diagnosticPort}");
    Console.WriteLine($"Reachable: {diagnostic.Success}");
    Console.WriteLine($"Matching LAN: {diagnostic.LocalInterfaceName} {diagnostic.LocalAddress}");
    Console.WriteLine($"Selected route: {diagnostic.RoutedInterfaceName}");
    Console.WriteLine(diagnostic.Success ? "Connection probe succeeded." : diagnostic.Message);
    if (diagnostic.Success)
    {
        using var client = new RemoteClient(args[0], diagnosticPort, "network-diagnostic");
        var info = await client.GetInfoAsync();
        Console.WriteLine($"HTTPS identity: {info.Name}, PhoneFolder {info.Version}");
    }

    var discovery = new DiscoveryService();
    var devices = await discovery.DiscoverAsync(TimeSpan.FromSeconds(3));
    foreach (var device in devices)
    {
        Console.WriteLine($"Discovered: {device.DisplayName}");
    }
}

return 0;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
