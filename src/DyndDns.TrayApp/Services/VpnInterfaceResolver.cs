using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Picks the VPN connection the app should route through.
/// </summary>
internal static class VpnInterfaceResolver
{
    /// <summary>
    /// Keeps the already configured interface while it still exists, otherwise falls back to the connected one
    /// — or, when none is connected, to the first of the list, so a configured but currently down tunnel is
    /// still selected. Null when there are no connections at all.
    /// </summary>
    public static VpnInterfaceInfo? PickPreferred(IReadOnlyList<VpnInterfaceInfo> interfaces, string? currentName)
    {
        if (!string.IsNullOrWhiteSpace(currentName))
        {
            var configured = interfaces.FirstOrDefault(item =>
                string.Equals(item.Name, currentName, StringComparison.OrdinalIgnoreCase));

            if (configured is not null)
                return configured;
        }

        return interfaces.FirstOrDefault(item => item.IsUp) ?? interfaces.FirstOrDefault();
    }
}
