namespace DyndDns.TrayApp.Models;

public class FqdnGroupEntry
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Domains { get; set; } = new();
}

public class DnsRouteEntry
{
    public string Index { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Interface { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}

public class RouterState
{
    public List<FqdnGroupEntry> FqdnGroups { get; set; } = new();
    public List<DnsRouteEntry> DnsRoutes { get; set; } = new();
}
