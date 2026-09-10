namespace DyndDns.TrayApp.Models;

/// <summary>
/// One locally tracked domain together with the VPN interface its traffic should be routed through.
/// An empty <see cref="Interface"/> means "use the router's configured VPN interface".
/// </summary>
public sealed record DnsRoute(string Domain, string Interface);

/// <summary>
/// Naming of the Keenetic objects the app manages. Domains are grouped per VPN interface, so each
/// interface gets its own FQDN object-group and one dns-proxy route pointing at it.
/// </summary>
public static class DnsRouting
{
    public const string GroupPrefix = "dyndns-";

    public static string GetGroupName(string interfaceName) => GroupPrefix + interfaceName;

    public static bool IsRoutingGroup(string groupName) =>
        groupName.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Groups routes by their effective interface. Routes without an interface fall back to
    /// <paramref name="defaultInterface"/>; entries with no usable interface at all are dropped.
    /// </summary>
    public static IReadOnlyList<(string Interface, string GroupName, List<string> Domains)> GroupByInterface(
        IEnumerable<DnsRoute> routes,
        string defaultInterface)
    {
        return routes
            .Select(route => new
            {
                route.Domain,
                Interface = string.IsNullOrWhiteSpace(route.Interface) ? defaultInterface : route.Interface
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Interface))
            .GroupBy(entry => entry.Interface, StringComparer.OrdinalIgnoreCase)
            .Select(group => (
                Interface: group.Key,
                GroupName: GetGroupName(group.Key),
                Domains: group
                    .Select(entry => entry.Domain)
                    .Where(domain => !string.IsNullOrWhiteSpace(domain))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .Where(group => group.Domains.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Bindings the router reports for the groups this app manages, mapped back to plain routes.
    /// </summary>
    public static IReadOnlyList<DnsRoute> FromRouterGroups(IEnumerable<RouterRouteGroup> groups) =>
        groups
            .Where(group => IsRoutingGroup(group.GroupName))
            .SelectMany(group => group.Domains.Select(domain => new DnsRoute(domain, group.Interface)))
            .ToList();

    /// <summary>
    /// Brings the locally stored bindings in line with the router, which is the source of truth for
    /// the groups this app manages: a domain the router routes adopts its interface, and domains
    /// routed through those groups are added locally when still unknown. Local-only entries are kept
    /// on purpose — they may simply be waiting for their first sync.
    /// </summary>
    public static List<DnsRoute> MergeRouterRoutes(
        IReadOnlyList<DnsRoute> local,
        IReadOnlyList<DnsRoute> routerRoutes)
    {
        var merged = new List<DnsRoute>(local);

        var indexByDomain = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < merged.Count; index++)
            indexByDomain[merged[index].Domain] = index;

        foreach (var route in routerRoutes)
        {
            if (indexByDomain.TryGetValue(route.Domain, out var existing))
            {
                if (!string.Equals(merged[existing].Interface, route.Interface, StringComparison.OrdinalIgnoreCase))
                    merged[existing] = route;
            }
            else
            {
                indexByDomain[route.Domain] = merged.Count;
                merged.Add(route);
            }
        }

        return merged;
    }
}
