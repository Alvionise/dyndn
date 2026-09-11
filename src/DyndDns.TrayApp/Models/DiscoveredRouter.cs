namespace DyndDns.TrayApp.Models;

/// <summary>
/// A router found during a network scan. <see cref="Name"/> is filled best-effort from UPnP/SSDP and
/// may be empty when the friendly name could not be resolved.
/// </summary>
internal sealed record DiscoveredRouter(string Address, string Name)
{
    /// <summary>How the device is written in the scan list; see <see cref="RouterLabel"/>.</summary>
    public string DisplayName => RouterLabel.Format(Name, Address);

    // List controls show ToString(), so the user sees the friendly form instead of the record dump.
    public override string ToString() => DisplayName;
}
