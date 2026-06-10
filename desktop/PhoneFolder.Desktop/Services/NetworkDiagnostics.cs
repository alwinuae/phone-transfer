using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace PhoneFolder.Desktop.Services;

public static class NetworkDiagnostics
{
    public static async Task<NetworkProbeResult> ProbeAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return NetworkProbeResult.Failed(
                host,
                port,
                null,
                $"The phone address \"{host}\" could not be resolved. Check the address shown in the Android app.");
        }

        var target = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault();
        if (target is null)
        {
            return NetworkProbeResult.Failed(
                host,
                port,
                null,
                $"The phone address \"{host}\" did not resolve to a usable network address.");
        }

        var route = AnalyzeRoute(target);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(timeout);
        try
        {
            using var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            await socket.ConnectAsync(new IPEndPoint(target, port), linkedCancellation.Token);
            return new NetworkProbeResult(
                true,
                host,
                port,
                target,
                string.Empty,
                route.LocalInterfaceName,
                route.LocalAddress,
                route.RoutedInterfaceName,
                null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(
                host,
                port,
                target,
                FailureMessage(host, port, SocketError.TimedOut, route),
                route,
                SocketError.TimedOut);
        }
        catch (SocketException exception)
        {
            return Failed(
                host,
                port,
                target,
                FailureMessage(host, port, exception.SocketErrorCode, route),
                route,
                exception.SocketErrorCode);
        }
    }

    private static NetworkProbeResult Failed(
        string host,
        int port,
        IPAddress? target,
        string message,
        RouteAnalysis route,
        SocketError? socketError)
    {
        return new NetworkProbeResult(
            false,
            host,
            port,
            target,
            message,
            route.LocalInterfaceName,
            route.LocalAddress,
            route.RoutedInterfaceName,
            socketError);
    }

    private static string FailureMessage(
        string host,
        int port,
        SocketError socketError,
        RouteAnalysis route)
    {
        if (route.HasConflict)
        {
            return $"Windows is routing {host} through \"{route.RoutedInterfaceName}\" instead of "
                + $"the local network interface \"{route.LocalInterfaceName}\" "
                + $"({route.LocalAddress}). Pause that VPN, or disable its local-subnet route, then connect again.\n\n"
                + $"Target: {host}:{port}";
        }

        if (socketError == SocketError.ConnectionRefused)
        {
            return $"The phone at {host} was reached, but port {port} is not accepting connections. "
                + "Open Phone Transfer on Android, choose a folder, and tap Start sharing.";
        }

        if (socketError is SocketError.HostUnreachable
            or SocketError.NetworkUnreachable
            or SocketError.NetworkDown)
        {
            return $"Windows has no working route to {host}:{port}. Verify the phone's currently displayed "
                + "HTTPS address, connect both devices to the same non-guest Wi-Fi network, and pause any VPN.";
        }

        return $"The phone did not answer at {host}:{port} within a few seconds.\n\n"
            + "Check that Phone Transfer says \"Sharing is active\" and use the address currently shown on Android. "
            + "Both devices must be on the same non-guest Wi-Fi network. For a faster direct path, turn on "
            + "Windows Mobile Hotspot, connect the phone to it, and use PC Hotspot in the Windows app. "
            + "Router settings named AP isolation, "
            + "client isolation, wireless isolation, or guest isolation must be disabled.";
    }

    private static RouteAnalysis AnalyzeRoute(IPAddress target)
    {
        if (target.AddressFamily != AddressFamily.InterNetwork)
        {
            return RouteAnalysis.Empty;
        }

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Select(network => new InterfaceSnapshot(network))
            .Where(snapshot => snapshot.Index > 0)
            .ToArray();

        InterfaceSnapshot? localMatch = null;
        IPAddress? localAddress = null;
        foreach (var snapshot in interfaces)
        {
            foreach (var unicast in snapshot.Unicast)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork
                    && SameSubnet(target, unicast.Address, unicast.PrefixLength))
                {
                    localMatch = snapshot;
                    localAddress = unicast.Address;
                    break;
                }
            }
            if (localMatch is not null)
            {
                break;
            }
        }

        var routedIndex = BestInterfaceIndex(target);
        var routed = routedIndex is null
            ? null
            : interfaces.FirstOrDefault(snapshot => snapshot.Index == routedIndex.Value);
        return new RouteAnalysis(
            localMatch?.Index,
            localMatch?.Name ?? string.Empty,
            localAddress,
            routed?.Index,
            routed?.Name ?? string.Empty);
    }

    private static int? BestInterfaceIndex(IPAddress target)
    {
        if (!OperatingSystem.IsWindows() || target.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        var bytes = target.GetAddressBytes();
        var destination = BitConverter.ToUInt32(bytes, 0);
        return GetBestInterface(destination, out var index) == 0 ? (int)index : null;
    }

    private static bool SameSubnet(IPAddress left, IPAddress right, int prefixLength)
    {
        if (prefixLength is < 1 or > 32)
        {
            return false;
        }

        var leftBytes = left.GetAddressBytes();
        var rightBytes = right.GetAddressBytes();
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        for (var index = 0; index < fullBytes; index++)
        {
            if (leftBytes[index] != rightBytes[index])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xff << (8 - remainingBits));
        return (leftBytes[fullBytes] & mask) == (rightBytes[fullBytes] & mask);
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetBestInterface(uint destinationAddress, out uint bestInterfaceIndex);

    private sealed class InterfaceSnapshot
    {
        public InterfaceSnapshot(NetworkInterface network)
        {
            Name = network.Name;
            try
            {
                var properties = network.GetIPProperties();
                Index = properties.GetIPv4Properties()?.Index ?? -1;
                Unicast = properties.UnicastAddresses.ToArray();
            }
            catch (NetworkInformationException)
            {
                Index = -1;
                Unicast = [];
            }
        }

        public int Index { get; }
        public string Name { get; }
        public IReadOnlyList<UnicastIPAddressInformation> Unicast { get; }
    }

    private sealed record RouteAnalysis(
        int? LocalInterfaceIndex,
        string LocalInterfaceName,
        IPAddress? LocalAddress,
        int? RoutedInterfaceIndex,
        string RoutedInterfaceName)
    {
        public static RouteAnalysis Empty { get; } = new(null, string.Empty, null, null, string.Empty);

        public bool HasConflict => LocalInterfaceIndex is not null
            && RoutedInterfaceIndex is not null
            && LocalInterfaceIndex != RoutedInterfaceIndex;
    }
}

public sealed record NetworkProbeResult(
    bool Success,
    string Host,
    int Port,
    IPAddress? TargetAddress,
    string Message,
    string LocalInterfaceName,
    IPAddress? LocalAddress,
    string RoutedInterfaceName,
    SocketError? SocketError)
{
    internal static NetworkProbeResult Failed(
        string host,
        int port,
        IPAddress? target,
        string message)
    {
        return new NetworkProbeResult(
            false,
            host,
            port,
            target,
            message,
            string.Empty,
            null,
            string.Empty,
            null);
    }
}
