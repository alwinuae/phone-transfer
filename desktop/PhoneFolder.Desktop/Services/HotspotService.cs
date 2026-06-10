using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PhoneFolder.Desktop.Services;

public static class HotspotService
{
    public static HotspotStatus GetStatus()
    {
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            UnicastIPAddressInformation[] addresses;
            try
            {
                addresses = network.GetIPProperties().UnicastAddresses.ToArray();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            foreach (var address in addresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork
                    && IsHotspotInterface(network, address.Address))
                {
                    return new HotspotStatus(
                        true,
                        network.Name,
                        address.Address,
                        address.PrefixLength,
                        network.Speed);
                }
            }
        }

        return HotspotStatus.Inactive;
    }

    public static bool IsHotspotInterface(NetworkInterface network, IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork
            || IPAddress.IsLoopback(address)
            || IsLinkLocal(address))
        {
            return false;
        }

        var identity = $"{network.Name} {network.Description}";
        return identity.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("Mobile Hotspot", StringComparison.OrdinalIgnoreCase)
            || address.ToString().StartsWith("192.168.137.", StringComparison.Ordinal);
    }

    public static bool IsOnHotspotSubnet(string host)
    {
        if (!IPAddress.TryParse(host, out var target)
            || target.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var status = GetStatus();
        return status.Active
            && status.Address is not null
            && SameSubnet(target, status.Address, status.PrefixLength);
    }

    public static string ConnectionDescription(string host)
    {
        var status = GetStatus();
        if (!status.Active || !IsOnHotspotSubnet(host))
        {
            return "Local Wi-Fi";
        }

        var link = status.LinkSpeedBitsPerSecond > 0
            ? $" | adapter link {FormatBits(status.LinkSpeedBitsPerSecond)}"
            : string.Empty;
        return $"PC hotspot via {status.InterfaceName}{link}";
    }

    public static void OpenWindowsSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:network-mobilehotspot")
        {
            UseShellExecute = true
        });
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
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

    private static string FormatBits(long bitsPerSecond)
    {
        if (bitsPerSecond >= 1_000_000_000)
        {
            return $"{bitsPerSecond / 1_000_000_000d:0.##} Gbps";
        }
        return $"{bitsPerSecond / 1_000_000d:0.#} Mbps";
    }
}

public sealed record HotspotStatus(
    bool Active,
    string InterfaceName,
    IPAddress? Address,
    int PrefixLength,
    long LinkSpeedBitsPerSecond)
{
    public static HotspotStatus Inactive { get; } = new(false, string.Empty, null, 0, 0);
}
