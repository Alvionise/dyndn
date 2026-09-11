namespace DyndDns.TrayApp.Models;

/// <summary>
/// One locally tracked domain together with the VPN interface its traffic should be routed through.
/// An empty <see cref="Interface"/> means "use the router's configured VPN interface".
/// </summary>
internal sealed record DnsRoute(string Domain, string Interface);
