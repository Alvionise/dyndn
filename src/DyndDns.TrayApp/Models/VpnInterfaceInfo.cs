namespace DyndDns.TrayApp.Models;

/// <summary>
/// An interface as reported by <c>show interface</c>. Only the VPN-capable ones are used: the name a binding
/// refers to, the description the connection carries on the router — the name it is given in the router's web
/// UI —, the type that tells a tunnel from a physical port, and whether the connection is up.
/// </summary>
internal sealed record VpnInterfaceInfo(string Name, string Type, string Description, bool IsUp);
