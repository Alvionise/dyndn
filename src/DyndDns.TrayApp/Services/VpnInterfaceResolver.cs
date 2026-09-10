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
}
