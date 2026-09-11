using System.Text.Json;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// The contract of the Keenetic RCI: pure functions that turn the app's model into the JSON commands the
/// router understands, and the router's answers back into the model. It is kept apart from
/// <see cref="KeeneticApiService"/>, which owns the transport (HTTP, authentication, batching), so the
/// protocol can be read and tested without a router.
/// </summary>
internal static class KeeneticRci
{
    /// <summary>Fields of <c>show system</c> / <c>show identification</c> that may carry the name, best first.</summary>
    private static readonly string[] DeviceNameFields = ["hostname", "name", "model"];
    // Interface name prefixes (and reported types) that identify a VPN client connection.
    private static readonly string[] VpnInterfacePrefixes =
    [
        "L2TP", "PPTP", "SSTP", "Wireguard", "OpenVPN", "IPsec", "IKEv2"
    ];

    /// <summary>
    /// Builds a single command that turns the current group include list into the desired one
    /// by adding missing addresses and removing stale ones. Returns <c>null</c> when the group
    /// already matches.
    /// </summary>
    public static string? BuildGroupUpdateCommand(FqdnGroupEntry existing, DnsGroup desired)
    {
        var current = new HashSet<string>(existing.Domains, StringComparer.OrdinalIgnoreCase);
        var wanted = new HashSet<string>(desired.Domains, StringComparer.OrdinalIgnoreCase);

        var include = new List<Dictionary<string, object>>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in desired.Domains)
        {
            if (!current.Contains(domain) && added.Add(domain))
                include.Add(new Dictionary<string, object> { ["address"] = domain });
        }

        foreach (var domain in existing.Domains)
        {
            if (!wanted.Contains(domain) && removed.Add(domain))
                include.Add(new Dictionary<string, object> { ["address"] = domain, ["no"] = true });
        }

        var descriptionChanged = !string.Equals(existing.Description, desired.Description, StringComparison.Ordinal);
        if (include.Count == 0 && !descriptionChanged)
            return null;

        var body = new Dictionary<string, object>();
        if (descriptionChanged)
            body["description"] = desired.Description;
        if (include.Count > 0)
            body["include"] = include.ToArray();

        return FqdnGroupCommand(desired.Name, body);
    }

    public static string BuildDeleteGroupCommand(string groupName) =>
        FqdnGroupCommand(groupName, new Dictionary<string, object> { ["no"] = true });

    public static string BuildCreateGroupCommand(DnsGroup group)
    {
        var include = group.Domains
            .Select(domain => new Dictionary<string, object> { ["address"] = domain })
            .ToArray();

        return FqdnGroupCommand(group.Name, new Dictionary<string, object>
        {
            ["description"] = group.Description,
            ["include"] = include
        });
    }

    /// <summary>
    /// Deletes a dns-proxy route either by its index or, when the router did not report one, by the group it
    /// points at; both forms are accepted by the RCI.
    /// </summary>
    public static string BuildDeleteRouteCommand(DnsRouteEntry route)
    {
        var body = new Dictionary<string, object> { ["no"] = true };

        if (route.Index.Length > 0)
            body["index"] = route.Index;
        else
            body["group"] = route.Group;

        return RouteCommand(body);
    }

    public static string BuildCreateRouteCommand(string groupName, string vpnInterface) =>
        RouteCommand(new Dictionary<string, object>
        {
            ["group"] = groupName,
            ["gateway"] = string.Empty,
            ["auto"] = true,
            ["reject"] = true,
            ["interface"] = vpnInterface,
            ["disable"] = false
        });

    public static List<FqdnGroupEntry> ParseFqdnGroups(JsonElement element)
    {
        var groups = new List<FqdnGroupEntry>();

        if (element.ValueKind != JsonValueKind.Object)
            return groups;

        foreach (var groupProperty in element.EnumerateObject())
        {
            if (groupProperty.Value.ValueKind != JsonValueKind.Object)
                continue;

            var domains = new List<string>();
            if (groupProperty.Value.TryGetProperty("include", out var include) &&
                include.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in include.EnumerateArray())
                {
                    var domain = item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : GetString(item, "address");

                    if (!string.IsNullOrEmpty(domain))
                        domains.Add(domain);
                }
            }

            groups.Add(new FqdnGroupEntry
            {
                Name = groupProperty.Name,
                Description = GetString(groupProperty.Value, "description"),
                Domains = domains
            });
        }

        return groups;
    }

    public static List<DnsRouteEntry> ParseDnsRoutes(JsonElement element)
    {
        var routes = new List<DnsRouteEntry>();

        if (element.ValueKind != JsonValueKind.Array)
            return routes;

        foreach (var route in element.EnumerateArray())
        {
            // A route is named by the group it points at, so an entry of another shape — or one that carries no
            // readable group — is not a route at all.
            if (route.ValueKind != JsonValueKind.Object ||
                !route.TryGetProperty("group", out var group) ||
                group.ValueKind != JsonValueKind.String)
                continue;

            routes.Add(new DnsRouteEntry
            {
                Index = GetString(route, "index"),
                Group = group.GetString() ?? string.Empty,
                Interface = GetString(route, "interface")
            });
        }

        return routes;
    }

    public static List<VpnInterfaceInfo> ParseInterfaces(JsonElement interfaces)
    {
        var result = new List<VpnInterfaceInfo>();
        var known = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (interfaces.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in interfaces.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
                continue;

            // "connected" is spelled as a string by most firmwares and as a boolean by some; only when the field
            // is absent altogether is the reported state of the connection what is left to go by.
            var isUp = property.Value.TryGetProperty("connected", out var connected)
                ? IsAffirmative(connected)
                : string.Equals(GetString(property.Value, "state"), "up", StringComparison.OrdinalIgnoreCase);

            var info = new VpnInterfaceInfo(
                property.Name,
                GetString(property.Value, "type"),
                GetString(property.Value, "description"),
                isUp);

            // The router can report one connection twice (a tunnel is listed under two entries spelled the same),
            // and the second copy would become a duplicate option carrying another state; the connected entry
            // replaces the one that is down instead of standing next to it.
            if (known.TryGetValue(info.Name, out var existing))
            {
                if (info.IsUp && !result[existing].IsUp)
                    result[existing] = info;

                continue;
            }

            known[info.Name] = result.Count;
            result.Add(info);
        }

        return result;
    }

    public static bool IsVpnInterface(string name, string type) =>
        VpnInterfacePrefixes.Any(prefix =>
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The name a router calls itself by, out of a <c>show system</c> or <c>show identification</c> answer: the
    /// hostname set in its web UI first, then whatever name the firmware reports, and the model as the last
    /// resort. An empty string means the firmware tells nothing, and the router is shown by its address alone.
    /// </summary>
    public static string ParseDeviceName(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object)
            return string.Empty;

        foreach (var field in DeviceNameFields)
        {
            if (!section.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
                continue;

            var name = value.GetString()?.Trim();

            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return string.Empty;
    }

    /// <summary>
    /// RCI hides a rejected command behind an ordinary <c>200 OK</c> answer, with the reason inside the body
    /// (<c>{"status":[{"status":"error","message":"not found: ..."}]}</c>), so the status code alone cannot
    /// tell success from failure. Returns the first error message of the response, or <c>null</c> when the
    /// router reported no error.
    /// </summary>
    public static string? ReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.Contains("error", StringComparison.Ordinal))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return FindError(document.RootElement);
        }
        catch (JsonException)
        {
            // Not JSON at all; a failing status code is reported by the caller with the raw body.
            return null;
        }
    }

    private static string? FindError(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (FindError(item) is { } error)
                    return error;
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("status") && property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in property.Value.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object ||
                        !entry.TryGetProperty("status", out var state) ||
                        state.ValueKind != JsonValueKind.String ||
                        !string.Equals(state.GetString(), "error", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return entry.TryGetProperty("message", out var message)
                        ? message.GetString() ?? "unknown error"
                        : "unknown error";
                }
            }
            else if (FindError(property.Value) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// The way a firmware says yes: the string <c>yes</c> or the boolean <c>true</c>, both of which occur in the
    /// answers. Anything else — including a field of another shape — means no.
    /// </summary>
    private static bool IsAffirmative(JsonElement value) =>
        value.ValueKind == JsonValueKind.True ||
        (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Value of a property read as a string. A number, a boolean, a field of another shape or a missing one reads
    /// as empty: the answers differ between firmware versions, and one unexpected value may not throw away the
    /// whole section the answer was asked for.
    /// </summary>
    private static string GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>Command on a named FQDN object-group: <c>{"object-group":{"fqdn":{"name":body}}}</c>.</summary>
    private static string FqdnGroupCommand(string name, object body) => Nest("object-group", "fqdn", name, body);

    /// <summary>Command on the dns-proxy route: <c>{"dns-proxy":{"route":body}}</c>.</summary>
    private static string RouteCommand(object body) => Nest("dns-proxy", "route", body);

    /// <summary>
    /// Wraps a body the way the RCI expects it: one object per path segment, the body last. Written once here,
    /// because every command of the app is a short path into the router configuration.
    /// </summary>
    private static string Nest(string section, string subsection, object body) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [section] = new Dictionary<string, object> { [subsection] = body }
        });

    private static string Nest(string section, string subsection, string name, object body) =>
        Nest(section, subsection, new Dictionary<string, object> { [name] = body });
}
