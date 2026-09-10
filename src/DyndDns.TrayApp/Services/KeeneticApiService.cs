using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Talks to the Keenetic RCI HTTP API.
///
/// Reads use the <c>show sc</c> prefix, which RCI expects as a nested JSON object
/// (e.g. <c>[{"show":{"sc":{"object-group":{"fqdn":{}}}}}]</c>) rather than a CLI-style
/// array of words. Authentication is cached and retried once on a 401, and every request
/// builds a fresh <see cref="HttpContent"/> so a repeat after re-authentication stays valid.
/// </summary>
public sealed class KeeneticApiService : IDisposable
{
    private const string SaveConfigurationCommand = "{\"system\":{\"configuration\":{\"save\":{}}}}";

    // Interface name prefixes (and reported types) that identify a VPN client connection.
    private static readonly string[] VpnInterfacePrefixes =
    {
        "L2TP", "PPTP", "SSTP", "Wireguard", "OpenVPN", "IPsec", "IKEv2"
    };

    // Shared client for one-off credential checks; it accepts self-signed router certificates
    // and never follows redirects so an HTTP probe is not silently upgraded to HTTPS.
    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly HttpClient _httpClient;
    private readonly RouterConfig _routerConfig;
    private readonly SemaphoreSlim _authenticationLock = new(1, 1);
    private bool _isAuthenticated;

    public KeeneticApiService(RouterConfig routerConfig)
    {
        _routerConfig = routerConfig;

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };

        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private string BaseUrl => RouterAddress.ToBaseUrl(_routerConfig.Address);

    private string RciUrl => $"{BaseUrl}/rci/";

    public async Task SyncGroupAsync(DnsGroup group, string vpnInterface)
    {
        if (!await EnsureAuthenticatedAsync())
            throw new InvalidOperationException("Authentication failed");

        var state = await GetRouterStateAsync();

        var existingGroup = state.FqdnGroups.FirstOrDefault(entry => entry.Name == group.Name);
        var existingRoute = state.DnsRoutes.FirstOrDefault(route => route.Group == group.Name);

        var routeNeedsUpdate = existingRoute is null ||
            !string.Equals(existingRoute.Interface, vpnInterface, StringComparison.OrdinalIgnoreCase);

        if (group.Domains.Count == 0)
        {
            if (existingRoute is not null)
                await SendBatchAsync(BuildDeleteRouteCommand(existingRoute), SaveConfigurationCommand);

            if (existingGroup is not null)
                await SendBatchAsync(BuildDeleteGroupCommand(group.Name), SaveConfigurationCommand);

            return;
        }

        // Update the group in place instead of deleting and recreating it: the router removes
        // routes asynchronously when their group disappears, and that cleanup would swallow a
        // route created right after the group is rebuilt.
        if (existingGroup is null)
        {
            await SendBatchAsync(BuildCreateGroupCommand(group), SaveConfigurationCommand);
        }
        else if (BuildGroupUpdateCommand(existingGroup, group) is { } updateCommand)
        {
            await SendBatchAsync(updateCommand, SaveConfigurationCommand);
        }

        if (existingRoute is not null && routeNeedsUpdate)
            await SendBatchAsync(BuildDeleteRouteCommand(existingRoute), SaveConfigurationCommand);

        if (routeNeedsUpdate)
            await SendBatchAsync(BuildCreateRouteCommand(group.Name, vpnInterface), SaveConfigurationCommand);
    }

    /// <summary>
    /// Builds a single command that turns the current group include list into the desired one
    /// by adding missing addresses and removing stale ones. Returns <c>null</c> when the group
    /// already matches.
    /// </summary>
    internal static string? BuildGroupUpdateCommand(FqdnGroupEntry existing, DnsGroup desired)
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

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["object-group"] = new Dictionary<string, object>
            {
                ["fqdn"] = new Dictionary<string, object> { [desired.Name] = body }
            }
        });
    }

    private async Task<bool> EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
            return true;

        // Concurrent callers (the startup sync and the VPN refresh) share one router session, where a
        // fresh challenge invalidates the previous one; the login is therefore performed once.
        await _authenticationLock.WaitAsync();

        try
        {
            if (_isAuthenticated)
                return true;

            _isAuthenticated = await AuthenticateAsync();
            return _isAuthenticated;
        }
        finally
        {
            _authenticationLock.Release();
        }
    }

    private async Task<bool> AuthenticateAsync()
    {
        try
        {
            using var probe = await _httpClient.GetAsync($"{BaseUrl}/auth");

            if (probe.StatusCode != HttpStatusCode.Unauthorized)
                return probe.IsSuccessStatusCode;

            var challenge = GetHeader(probe, "X-NDM-Challenge");
            var realm = GetHeader(probe, "X-NDM-Realm");

            if (string.IsNullOrEmpty(challenge) || string.IsNullOrEmpty(realm))
                return false;

            var password = ComputePasswordHash(realm, challenge);
            using var content = new StringContent(
                JsonSerializer.Serialize(new { login = _routerConfig.Username, password }),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.PostAsync($"{BaseUrl}/auth", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Keenetic authentication failed: {ex.Message}");
            return false;
        }
    }

    private string ComputePasswordHash(string realm, string challenge) =>
        ComputePasswordHash(_routerConfig.Username, realm, challenge, _routerConfig.Password);

    internal static string ComputePasswordHash(string username, string realm, string challenge, string password)
    {
        var stage1 = MD5.HashData(Encoding.UTF8.GetBytes($"{username}:{realm}:{password}"));
        var stage1Hex = Convert.ToHexString(stage1).ToLowerInvariant();

        var stage2 = SHA256.HashData(Encoding.UTF8.GetBytes(challenge + stage1Hex));
        return Convert.ToHexString(stage2).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies credentials against any reachable Keenetic without touching the stored config.
    /// The setup wizard calls this before persisting the router settings.
    /// </summary>
    public static async Task<bool> ValidateCredentialsAsync(
        string address,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = RouterAddress.ToBaseUrl(address);
        if (baseUrl.Length == 0)
            return false;

        try
        {
            using var probe = await SharedHttpClient.GetAsync($"{baseUrl}/auth", cancellationToken).ConfigureAwait(false);

            if (probe.StatusCode != HttpStatusCode.Unauthorized)
                return probe.IsSuccessStatusCode;

            var challenge = GetHeader(probe, "X-NDM-Challenge");
            var realm = GetHeader(probe, "X-NDM-Realm");

            if (string.IsNullOrEmpty(challenge) || string.IsNullOrEmpty(realm))
                return false;

            var passwordHash = ComputePasswordHash(username, realm, challenge, password);
            using var content = new StringContent(
                JsonSerializer.Serialize(new { login = username, password = passwordHash }),
                Encoding.UTF8,
                "application/json");

            using var response = await SharedHttpClient.PostAsync($"{baseUrl}/auth", content, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Keenetic credential validation failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Drops the cached authentication so the next request re-authenticates with the current
    /// <see cref="RouterConfig"/> values, e.g. after the setup wizard replaced them.
    /// </summary>
    public void InvalidateAuthentication() => _isAuthenticated = false;

    private async Task<RouterState> GetRouterStateAsync()
    {
        var groups = await ShowStartupConfigAsync("object-group", "fqdn");
        var routes = await ShowStartupConfigAsync("dns-proxy", "route");

        return new RouterState
        {
            FqdnGroups = groups is { } groupData ? ParseFqdnGroups(groupData) : new List<FqdnGroupEntry>(),
            DnsRoutes = routes is { } routeData ? ParseDnsRoutes(routeData) : new List<DnsRouteEntry>()
        };
    }

    /// <summary>
    /// Reads a section of the startup-config, e.g. <c>show sc object-group fqdn</c>.
    /// RCI represents that address as nested objects; the reply is an array whose first
    /// element mirrors the same nesting.
    /// </summary>
    private async Task<JsonElement?> ShowStartupConfigAsync(params string[] path)
    {
        var segments = new[] { "show", "sc" }.Concat(path).ToArray();

        var payload = JsonSerializer.Serialize(new[] { NestPath(segments) });
        var body = await SendRciAsync(payload);

        using var document = JsonDocument.Parse(body);
        var current = document.RootElement;

        if (current.ValueKind == JsonValueKind.Array && current.GetArrayLength() > 0)
            current = current[0];

        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return null;

            current = next;
        }

        return current.Clone();
    }

    private static Dictionary<string, object> NestPath(IEnumerable<string> segments)
    {
        var root = new Dictionary<string, object>();
        var current = root;

        foreach (var segment in segments)
        {
            var child = new Dictionary<string, object>();
            current[segment] = child;
            current = child;
        }

        return root;
    }

    private async Task SendBatchAsync(params string[] commands)
    {
        var payload = "[" + string.Join(",", commands) + "]";
        await SendRciAsync(payload);
    }

    private async Task<string> SendRciAsync(string payload)
    {
        var response = await PostRciAsync(payload);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _isAuthenticated = false;

            if (!await EnsureAuthenticatedAsync())
                throw new InvalidOperationException("Authentication failed");

            response = await PostRciAsync(payload);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"RCI error: {response.StatusCode} - {body}");

            return body;
        }
    }

    private Task<HttpResponseMessage> PostRciAsync(string payload)
    {
        var content = new StringContent(payload, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return _httpClient.PostAsync(RciUrl, content);
    }

    private static string? GetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    internal static List<FqdnGroupEntry> ParseFqdnGroups(JsonElement element)
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
                        : item.TryGetProperty("address", out var address) ? address.GetString() : null;

                    if (!string.IsNullOrEmpty(domain))
                        domains.Add(domain);
                }
            }

            groups.Add(new FqdnGroupEntry
            {
                Name = groupProperty.Name,
                Description = groupProperty.Value.TryGetProperty("description", out var description)
                    ? description.GetString() ?? string.Empty
                    : string.Empty,
                Domains = domains
            });
        }

        return groups;
    }

    internal static List<DnsRouteEntry> ParseDnsRoutes(JsonElement element)
    {
        var routes = new List<DnsRouteEntry>();

        if (element.ValueKind != JsonValueKind.Array)
            return routes;

        foreach (var route in element.EnumerateArray())
        {
            if (!route.TryGetProperty("group", out var group))
                continue;

            routes.Add(new DnsRouteEntry
            {
                Index = GetString(route, "index"),
                Group = group.GetString() ?? string.Empty,
                Interface = GetString(route, "interface"),
                Comment = GetString(route, "comment")
            });
        }

        return routes;
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    internal static string BuildDeleteGroupCommand(string groupName) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["object-group"] = new Dictionary<string, object>
            {
                ["fqdn"] = new Dictionary<string, object>
                {
                    [groupName] = new Dictionary<string, object> { ["no"] = true }
                }
            }
        });

    internal static string BuildCreateGroupCommand(DnsGroup group)
    {
        var include = group.Domains
            .Select(domain => new Dictionary<string, object> { ["address"] = domain })
            .ToArray();

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["object-group"] = new Dictionary<string, object>
            {
                ["fqdn"] = new Dictionary<string, object>
                {
                    [group.Name] = new Dictionary<string, object>
                    {
                        ["description"] = group.Description,
                        ["include"] = include
                    }
                }
            }
        });
    }

    internal static string BuildDeleteRouteCommand(DnsRouteEntry route) =>
        route.Index.Length > 0
            ? JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["dns-proxy"] = new Dictionary<string, object>
                {
                    ["route"] = new Dictionary<string, object>
                    {
                        ["no"] = true,
                        ["index"] = route.Index
                    }
                }
            })
            : JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["dns-proxy"] = new Dictionary<string, object>
                {
                    ["route"] = new Dictionary<string, object>
                    {
                        ["no"] = true,
                        ["group"] = route.Group
                    }
                }
            });

    internal static string BuildCreateRouteCommand(string groupName, string vpnInterface) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["dns-proxy"] = new Dictionary<string, object>
            {
                ["route"] = new Dictionary<string, object>
                {
                    ["group"] = groupName,
                    ["gateway"] = string.Empty,
                    ["auto"] = true,
                    ["reject"] = true,
                    ["interface"] = vpnInterface,
                    ["disable"] = false
                }
            }
        });

    /// <summary>
    /// VPN-capable interfaces reported by the router, in a stable order. Used to keep
    /// <see cref="Models.AppConfig.VpnInterface"/> pointing at a connection that really exists.
    /// </summary>
    public async Task<List<VpnInterfaceInfo>> GetVpnInterfacesAsync()
    {
        if (!await EnsureAuthenticatedAsync())
            throw new InvalidOperationException("Authentication failed");

        var body = await SendRciAsync("[{\"show\":{\"interface\":{}}}]");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            root = root[0];

        if (!root.TryGetProperty("show", out var show) || !show.TryGetProperty("interface", out var interfaces))
            return new List<VpnInterfaceInfo>();

        return ParseInterfaces(interfaces)
            .Where(item => IsVpnInterface(item.Name, item.Type))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static List<VpnInterfaceInfo> ParseInterfaces(JsonElement interfaces)
    {
        var result = new List<VpnInterfaceInfo>();

        if (interfaces.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in interfaces.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
                continue;

            var connected = GetString(property.Value, "connected");
            var state = GetString(property.Value, "state");
            var isUp = string.Equals(connected, "yes", StringComparison.OrdinalIgnoreCase) ||
                (connected.Length == 0 && string.Equals(state, "up", StringComparison.OrdinalIgnoreCase));

            result.Add(new VpnInterfaceInfo(
                property.Name,
                GetString(property.Value, "type"),
                GetString(property.Value, "description"),
                isUp,
                GetString(property.Value, "address")));
        }

        return result;
    }

    internal static bool IsVpnInterface(string name, string type) =>
        VpnInterfacePrefixes.Any(prefix =>
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The routing state as the router itself has it: every dns-proxy route together with the domains
    /// of the group it points at. This is the authoritative view the local files are aligned with.
    /// </summary>
    public async Task<List<RouterRouteGroup>> GetRouteGroupsAsync()
    {
        if (!await EnsureAuthenticatedAsync())
            throw new InvalidOperationException("Authentication failed");

        var state = await GetRouterStateAsync();
        var groups = new List<RouterRouteGroup>();

        foreach (var route in state.DnsRoutes)
        {
            var group = state.FqdnGroups.FirstOrDefault(entry =>
                string.Equals(entry.Name, route.Group, StringComparison.OrdinalIgnoreCase));

            if (group is null)
                continue;

            groups.Add(new RouterRouteGroup(route.Group, route.Interface, group.Domains.ToList()));
        }

        return groups;
    }

    /// <summary>
    /// Removes the dns-proxy routes and FQDN groups this app manages that are no longer wanted,
    /// so deleting a domain locally also cleans up the router.
    /// </summary>
    public async Task RemoveStaleRoutingGroupsAsync(IReadOnlyCollection<string> keepGroupNames)
    {
        if (!await EnsureAuthenticatedAsync())
            throw new InvalidOperationException("Authentication failed");

        var keep = new HashSet<string>(keepGroupNames, StringComparer.OrdinalIgnoreCase);
        var state = await GetRouterStateAsync();

        foreach (var route in state.DnsRoutes.Where(entry => DnsRouting.IsRoutingGroup(entry.Group) && !keep.Contains(entry.Group)))
            await SendBatchAsync(BuildDeleteRouteCommand(route), SaveConfigurationCommand);

        foreach (var group in state.FqdnGroups.Where(entry => DnsRouting.IsRoutingGroup(entry.Name) && !keep.Contains(entry.Name)))
            await SendBatchAsync(BuildDeleteGroupCommand(group.Name), SaveConfigurationCommand);
    }

    public void Dispose() => _httpClient.Dispose();
}
