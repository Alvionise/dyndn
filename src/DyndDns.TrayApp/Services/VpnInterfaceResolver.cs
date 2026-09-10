using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Picks the VPN connection the app should route through.
/// </summary>
internal static class VpnInterfaceResolver
{
    /// <summary>
    /// Returns the connected VPN interface when there is one, otherwise the first interface in the
    /// list (so a configured but currently down tunnel is still selected). Null when there are none.
    /// </summary>
    public static VpnInterfaceInfo? PickActive(IReadOnlyList<VpnInterfaceInfo> interfaces) =>
        interfaces.FirstOrDefault(item => item.IsUp) ?? interfaces.FirstOrDefault();

    /// <summary>
    /// Used when the router reports several connections and the user is not being asked: keeps the
    /// already configured interface while it still exists, otherwise falls back to the active one.
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

        return PickActive(interfaces);
    }
}
