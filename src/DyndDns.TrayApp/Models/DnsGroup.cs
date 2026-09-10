namespace DyndDns.TrayApp.Models;

public class DnsGroup
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DnsListFile { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<string> Domains { get; set; } = new();
    public bool IsSynced { get; set; } = false;
}
