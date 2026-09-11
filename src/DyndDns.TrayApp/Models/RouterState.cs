namespace DyndDns.TrayApp.Models;

internal sealed class FqdnGroupEntry
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Domains { get; set; } = [];
}

internal sealed class DnsRouteEntry
{
    public string Index { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Interface { get; set; } = string.Empty;
}

internal sealed class RouterState
{
    public List<FqdnGroupEntry> FqdnGroups { get; set; } = [];
    public List<DnsRouteEntry> DnsRoutes { get; set; } = [];
}

/// <summary>A dns-proxy route as configured on the router with the domains of the group it points at.</summary>
internal sealed record RouterRouteGroup(string GroupName, string Interface, IReadOnlyList<string> Domains);
