using System.Diagnostics;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;

namespace DyndDns.TrayApp.ViewModels;

/// <summary>
/// Keeps what the routers report about the domains routed through them, per profile. The routers are the
/// source of truth, so every read also aligns the stored bindings with them; a router that cannot be reached
/// keeps its previous state instead of looking empty. Reads talk to the routers and therefore run on worker
/// threads, so the cached state is guarded by a lock.
/// </summary>
internal sealed class RouterStateReader
{
    private readonly SettingsStore _settings;
    private readonly Func<RouterProfile, Task<IReadOnlyList<RouterRouteGroup>>> _readRouteGroups;
    private readonly Func<bool> _isSyncBusy;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, Dictionary<BindingKey, string>> _state = [];

    /// <param name="readRouteGroups">
    /// Reads the routing state of one router. A delegate rather than the API client itself: the reader only
    /// needs the answers, which keeps it testable without a router.
    /// </param>
    /// <param name="isSyncBusy">
    /// Tells whether a synchronization is still on its way to the routers; while it is, the router state is
    /// only displayed and the local bindings are not reconciled with it.
    /// </param>
    public RouterStateReader(
        SettingsStore settings,
        Func<RouterProfile, Task<IReadOnlyList<RouterRouteGroup>>> readRouteGroups,
        Func<bool> isSyncBusy)
    {
        _settings = settings;
        _readRouteGroups = readRouteGroups;
        _isSyncBusy = isSyncBusy;
    }

    /// <summary>Re-reads every profile that has an address and returns the state of all of them.</summary>
    public IReadOnlyDictionary<BindingKey, string> Read()
    {
        foreach (var profile in _settings.GetRouters().Where(profile => profile.Address.Length > 0))
            Refresh(profile);

        return Snapshot();
    }

    /// <summary>Re-reads one profile and aligns the local bindings with what its router reports.</summary>
    public void Refresh(RouterProfile profile)
    {
        try
        {
            var groups = Task.Run(() => _readRouteGroups(profile)).GetAwaiter().GetResult();

            // While a change of our own is still on its way to the router, its state is only displayed:
            // reconciling now would bring just removed routes back from the not-yet-updated router.
            if (!_isSyncBusy())
                ReconcileRoutes(profile, DnsRouting.FromRouterGroups(groups));

            var state = new Dictionary<BindingKey, string>();

            foreach (var group in groups)
            {
                foreach (var domain in group.Domains)
                    state[new BindingKey(profile.Id, domain)] = group.Interface;
            }

            lock (_gate)
                _state[profile.Id] = state;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Router state of '{profile.DisplayName}' was not read: {ex.Message}");
        }
    }

    /// <summary>The state of every profile as it was read last.</summary>
    public IReadOnlyDictionary<BindingKey, string> Snapshot()
    {
        var snapshot = new Dictionary<BindingKey, string>();

        lock (_gate)
        {
            foreach (var entry in _state.Values)
            {
                foreach (var pair in entry)
                    snapshot[pair.Key] = pair.Value;
            }
        }

        return snapshot;
    }

    /// <summary>Drops the state of a profile whose router is gone.</summary>
    public void Forget(int routerId)
    {
        lock (_gate)
            _state.Remove(routerId);
    }

    /// <summary>Adopts the bindings reported by the router; bindings of other routers are left untouched.</summary>
    private void ReconcileRoutes(RouterProfile profile, IReadOnlyList<DnsRoute> routerRoutes)
    {
        if (routerRoutes.Count == 0)
            return;

        var local = _settings.GetRoutes(profile.Id);
        var merged = DnsRouting.MergeRouterRoutes(local, routerRoutes);
        var stored = local.ToDictionary(route => route.Domain, route => route.Interface, StringComparer.OrdinalIgnoreCase);

        // Only what the router changed is written: this runs on every read of its state, and writing the whole
        // set again would open a connection (and a transaction) for every domain to store the same values.
        var changed = merged
            .Where(route => !stored.TryGetValue(route.Domain, out var current) ||
                !string.Equals(current, route.Interface, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (changed.Count == 0)
            return;

        foreach (var route in changed)
            _settings.AddRoute(profile.Id, route.Domain, route.Interface);

        Trace.TraceInformation(
            $"Bindings of '{profile.DisplayName}' reconciled from the router: {changed.Count} of {merged.Count} changed");
    }
}
