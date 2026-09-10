namespace DyndDns.TrayApp.Models;

/// <summary>
/// A Keenetic device found during a network scan. <see cref="Name"/> is filled best-effort
/// from UPnP/SSDP and may be empty when the friendly name could not be resolved.
/// </summary>
public sealed record DiscoveredRouter(string Address, string Name)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? Address : $"{Name} ({Address})";
}
