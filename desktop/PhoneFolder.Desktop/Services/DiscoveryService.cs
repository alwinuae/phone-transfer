using PhoneFolder.Desktop.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace PhoneFolder.Desktop.Services;

public sealed class DiscoveryService
{
    private const int DiscoveryPort = 8766;
    private const int AnnouncementPort = 8767;
    private static readonly byte[] Request = Encoding.UTF8.GetBytes("PHONEFOLDER_DISCOVER_V1");

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        TimeSpan timeout,
        bool hotspotOnly = false)
    {
        var clients = CreateClients(hotspotOnly);
        if (!hotspotOnly)
        {
            var listener = CreateAnnouncementListener();
            if (listener is not null)
            {
                clients.Add(listener);
            }
        }
        if (clients.Count == 0)
        {
            return [];
        }
        var devices = new ConcurrentDictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        using var cancellation = new CancellationTokenSource(timeout);

        try
        {
            var receivers = clients
                .Select(client => ReceiveLoopAsync(client, devices, cancellation.Token))
                .ToArray();

            var sender = SendRequestsAsync(clients, cancellation.Token);

            try
            {
                await Task.Delay(timeout, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            cancellation.Cancel();
            await Task.WhenAll(receivers.Append(sender));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Client.Dispose();
            }
        }

        return devices.Values.OrderBy(device => device.Name).ToList();
    }

    private static async Task SendRequestsAsync(
        IReadOnlyList<DiscoveryClient> clients,
        CancellationToken cancellationToken)
    {
        var firstPass = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var client in clients.Where(item => item.CanSend))
            {
                var targets = firstPass
                    ? DiscoveryTargets(client)
                    : [client.BroadcastAddress];
                foreach (var target in targets)
                {
                    try
                    {
                        await client.Client.SendAsync(
                            Request,
                            new IPEndPoint(target, DiscoveryPort),
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        // Another active adapter or the passive listener can still find the phone.
                    }
                }
            }

            firstPass = false;
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task ReceiveLoopAsync(
        DiscoveryClient discoveryClient,
        ConcurrentDictionary<string, DiscoveredDevice> devices,
        CancellationToken cancellationToken)
    {
        var client = discoveryClient.Client;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await client.ReceiveAsync(cancellationToken);
                var text = Encoding.UTF8.GetString(response.Buffer);
                var parts = text.Split('|');
                if (parts.Length < 4
                    || parts[0] != "PHONEFOLDER_V1"
                    || !int.TryParse(parts[3], out var port)
                    || port is < 1 or > 65535)
                {
                    continue;
                }

                // The UDP source is the address Windows can actually route back to.
                var address = response.RemoteEndPoint.Address.ToString();
                var fingerprint = parts.Length >= 5 ? parts[4] : string.Empty;
                var key = string.IsNullOrWhiteSpace(fingerprint)
                    ? $"{address}:{port}"
                    : fingerprint;
                var discovered = new DiscoveredDevice(
                    parts[1],
                    address,
                    port,
                    fingerprint,
                    discoveryClient.IsHotspot);
                devices.AddOrUpdate(
                    key,
                    discovered,
                    (_, current) => discovered.IsHotspot && !current.IsHotspot
                        ? discovered
                        : current);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static List<DiscoveryClient> CreateClients(bool hotspotOnly)
    {
        var clients = new List<DiscoveryClient>();
        var localAddresses = new HashSet<IPAddress>();
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up
                || network.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var unicast in network.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork
                    || IPAddress.IsLoopback(unicast.Address)
                    || unicast.PrefixLength is < 1 or > 30
                    || (hotspotOnly && !HotspotService.IsHotspotInterface(network, unicast.Address))
                    || !localAddresses.Add(unicast.Address))
                {
                    continue;
                }

                try
                {
                    var client = new UdpClient(new IPEndPoint(unicast.Address, 0))
                    {
                        EnableBroadcast = true
                    };
                    clients.Add(new DiscoveryClient(
                        client,
                        unicast.Address,
                        unicast.PrefixLength,
                        BroadcastAddress(unicast.Address, unicast.PrefixLength),
                        HotspotService.IsHotspotInterface(network, unicast.Address),
                        true));
                }
                catch (SocketException)
                {
                }
            }
        }

        if (clients.Count == 0 && !hotspotOnly)
        {
            var fallback = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true
            };
            fallback.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            clients.Add(new DiscoveryClient(
                fallback,
                IPAddress.Any,
                0,
                IPAddress.Broadcast,
                false,
                true));
        }
        return clients;
    }

    private static DiscoveryClient? CreateAnnouncementListener()
    {
        try
        {
            var listener = new UdpClient(AddressFamily.InterNetwork);
            listener.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, AnnouncementPort));
            return new DiscoveryClient(
                listener,
                IPAddress.Any,
                0,
                IPAddress.Broadcast,
                false,
                false);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static IEnumerable<IPAddress> DiscoveryTargets(DiscoveryClient client)
    {
        yield return client.BroadcastAddress;
        if (client.LocalAddress.Equals(IPAddress.Any))
        {
            yield break;
        }

        // Some routers and Windows network bridges suppress client broadcasts.
        // Probe at most the local /24 with tiny UDP discovery packets as a fallback.
        var scanPrefix = Math.Max(24, client.PrefixLength);
        var local = ToUInt32(client.LocalAddress);
        var mask = scanPrefix == 0 ? 0u : uint.MaxValue << (32 - scanPrefix);
        var network = local & mask;
        var addressCount = 1u << (32 - scanPrefix);
        for (uint offset = 1; offset < addressCount - 1; offset++)
        {
            var candidate = FromUInt32(network + offset);
            if (!candidate.Equals(client.LocalAddress))
            {
                yield return candidate;
            }
        }
    }

    private static IPAddress BroadcastAddress(IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        for (var bit = prefixLength; bit < 32; bit++)
        {
            bytes[bit / 8] |= (byte)(1 << (7 - bit % 8));
        }
        return new IPAddress(bytes);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24)
            | ((uint)bytes[1] << 16)
            | ((uint)bytes[2] << 8)
            | bytes[3];
    }

    private static IPAddress FromUInt32(uint address)
    {
        return new IPAddress(
        [
            (byte)(address >> 24),
            (byte)(address >> 16),
            (byte)(address >> 8),
            (byte)address
        ]);
    }

    private sealed record DiscoveryClient(
        UdpClient Client,
        IPAddress LocalAddress,
        int PrefixLength,
        IPAddress BroadcastAddress,
        bool IsHotspot,
        bool CanSend);
}
