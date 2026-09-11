using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// The network adapters of this machine, as far as the app is concerned: what can carry traffic to a router. The
/// network scan and the UPnP lookup need the same picture, and on a machine with a VPN or a virtual adapter
/// neither of them may let the traffic leave through the wrong interface.
/// </summary>
internal static class LocalNetwork
{
    // Virtual adapters (Hyper-V, WSL, VPN tunnels) add large dead address ranges that only slow the scan down,
    // and a multicast sent through one of them comes back as "the host is unreachable".
    private static readonly string[] VirtualAdapterMarkers =
    [
        "hyper-v", "vmware", "virtualbox", "wsl", "loopback", "pseudo",
        "tap-windows", "wireguard", "openvpn", "tailscale", "zerotier"
    ];

    /// <summary>Adapters a router can be reached through: up, and neither loopback nor virtual.</summary>
    public static IEnumerable<NetworkInterface> UsableInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces().Where(IsUsable);

    /// <summary>IPv4 addresses of those adapters, so a search can be sent from each of them.</summary>
    public static List<IPAddress> LocalAddresses() =>
        [.. UsableInterfaces()
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)];

    /// <summary>IPv4 addresses of the routers the machine uses as gateways.</summary>
    public static List<IPAddress> DefaultGateways() =>
        [.. UsableInterfaces()
            .SelectMany(networkInterface => networkInterface.GetIPProperties().GatewayAddresses)
            .Select(gateway => gateway.Address)
            .Where(address => address is { AddressFamily: AddressFamily.InterNetwork } && !address.Equals(IPAddress.Any))];

    private static bool IsUsable(NetworkInterface networkInterface) =>
        networkInterface.OperationalStatus == OperationalStatus.Up &&
        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
        !VirtualAdapterMarkers.Any(marker =>
            networkInterface.Description.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
