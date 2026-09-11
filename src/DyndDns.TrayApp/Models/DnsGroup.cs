namespace DyndDns.TrayApp.Models;

/// <summary>One Keenetic FQDN object-group together with the domains it contains.</summary>
internal sealed class DnsGroup
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Domains { get; set; } = [];
}
