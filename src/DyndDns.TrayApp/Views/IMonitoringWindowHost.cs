using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Views;

/// <summary>
/// What the monitoring window needs from the application: the DNS journal, the router profiles with their
/// bindings and the actions it can trigger. One collaborator instead of a dozen callbacks keeps the window
/// free of the tray wiring and lets a test drive it with a simple fake.
/// </summary>
internal interface IMonitoringWindowHost
{
    /// <summary>Domains of the journal, newest lookup first.</summary>
    IReadOnlyList<DomainStat> SearchDomains(string? term, int limit);

    IReadOnlyList<RouterProfile> GetRouters();

    IReadOnlyList<DomainBinding> GetBindings();

    IReadOnlyList<VpnInterfaceInfo> GetVpnInterfaces(int routerId);

    /// <summary>Interface each domain of the profile is routed through on its router.</summary>
    IReadOnlyDictionary<BindingKey, string> GetRouterState();

    /// <summary>Binds the domains to the profile; returns how many bindings changed.</summary>
    int Bind(int routerId, IReadOnlyList<string> domains, string interfaceName);

    /// <summary>Removes the bindings; returns how many were removed.</summary>
    int Unbind(IReadOnlyList<BindingKey> bindings);

    /// <summary>Drops the journal entries that are bound to no router; returns how many were removed.</summary>
    int ClearUnboundDomains();
}
