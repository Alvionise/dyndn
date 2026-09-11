using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
internal sealed class KeeneticApiService : IDisposable
{
    private const string SaveConfigurationCommand = "{\"system\":{\"configuration\":{\"save\":{}}}}";

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

    // The sections that may carry the name a router calls itself by; the first one that answers wins.
    private static readonly string[] DeviceNameSections = ["system", "identification"];

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _authenticationLock = new(1, 1);
    private readonly RouterProfile _profile;
    private bool _isAuthenticated;

    public KeeneticApiService(RouterProfile profile)
    {
        _profile = profile;

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };

        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private string BaseUrl => RouterAddress.ToBaseUrl(_profile.Address);

    private string RciUrl => $"{BaseUrl}/rci/";

    public async Task SyncGroupAsync(DnsGroup group, string vpnInterface)
    {
        if (!await EnsureAuthenticatedAsync())
            throw new InvalidOperationException("Authentication failed");

        var state = await GetRouterStateAsync();

        // Group names are compared without regard to case, as everywhere else: the router may report them
        // in a different case, and a miss here would create a group instead of updating it.
        var existingGroup = state.FqdnGroups.FirstOrDefault(entry =>
            string.Equals(entry.Name, group.Name, StringComparison.OrdinalIgnoreCase));
        var existingRoute = state.DnsRoutes.FirstOrDefault(route =>
            string.Equals(route.Group, group.Name, StringComparison.OrdinalIgnoreCase));

        var routeNeedsUpdate = existingRoute is null ||
            !string.Equals(existingRoute.Interface, vpnInterface, StringComparison.OrdinalIgnoreCase);

        if (group.Domains.Count == 0)
        {
            if (existingRoute is not null)
                await SendBatchAsync(KeeneticRci.BuildDeleteRouteCommand(existingRoute), SaveConfigurationCommand);

            if (existingGroup is not null)
                await SendBatchAsync(KeeneticRci.BuildDeleteGroupCommand(group.Name), SaveConfigurationCommand);

            return;
        }

        // Update the group in place instead of deleting and recreating it: the router removes
        // routes asynchronously when their group disappears, and that cleanup would swallow a
        // route created right after the group is rebuilt.
        if (existingGroup is null)
        {
            await SendBatchAsync(KeeneticRci.BuildCreateGroupCommand(group), SaveConfigurationCommand);
        }
        else if (KeeneticRci.BuildGroupUpdateCommand(existingGroup, group) is { } updateCommand)
        {
            await SendBatchAsync(updateCommand, SaveConfigurationCommand);
        }

        if (existingRoute is not null && routeNeedsUpdate)
            await SendBatchAsync(KeeneticRci.BuildDeleteRouteCommand(existingRoute), SaveConfigurationCommand);

        if (routeNeedsUpdate)
            await SendBatchAsync(KeeneticRci.BuildCreateRouteCommand(group.Name, vpnInterface), SaveConfigurationCommand);
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
            return await KeeneticAuth.SignInAsync(_httpClient, BaseUrl, _profile.Username, _profile.Password);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Keenetic authentication failed: {ex.Message}");
            return false;
        }
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
            return await KeeneticAuth.SignInAsync(SharedHttpClient, baseUrl, username, password, cancellationToken)
                .ConfigureAwait(false);
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
    /// Reads the name the router calls itself by, so a device that answered the scan without a UPnP name — or a
    /// router added by its address — is not stored nameless and listed as a bare IP. Best effort: a firmware
    /// without the section, or a failed read, returns an empty string and the caller keeps its own name.
    /// </summary>
    public static async Task<string> ReadDeviceNameAsync(
        string address,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var service = new KeeneticApiService(new RouterProfile
        {
            Address = address,
            Username = username,
            Password = password
        });

        return await service.ReadDeviceNameAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the name of this router over the session that is already signed in; see the static overload for
    /// what is asked and why. Returns an empty string when the firmware does not report one.
    /// </summary>
    public async Task<string> ReadDeviceNameAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureAuthenticatedAsync())
            return string.Empty;

        foreach (var section in DeviceNameSections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await ShowAsync(section) is { } data)
                {
                    var name = KeeneticRci.ParseDeviceName(data);

                    if (name.Length > 0)
                        return name;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Reading the name of the router failed ({section}): {ex.Message}");
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Reads one section of <c>show</c>, e.g. <c>interface</c> or <c>sc object-group fqdn</c>. RCI wants the
    /// address as nested objects and answers with an array whose first element mirrors the same nesting, so the
    /// request is built and the reply unwrapped here instead of in every caller. Returns <c>null</c> when the
    /// firmware does not know that section.
    /// </summary>
    private async Task<JsonElement?> ShowAsync(params string[] path)
    {
        var segments = new[] { "show" }.Concat(path).ToArray();

        var body = await SendRciAsync(JsonSerializer.Serialize(new[] { NestPath(segments) }));

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

    /// <summary>Drops the cached authentication, so the next request signs in again.</summary>
    private void InvalidateAuthentication() => _isAuthenticated = false;

    private async Task<RouterState> GetRouterStateAsync()
    {
        var groups = await ShowAsync("sc", "object-group", "fqdn");
        var routes = await ShowAsync("sc", "dns-proxy", "route");

        return new RouterState
        {
            FqdnGroups = groups is { } groupData ? KeeneticRci.ParseFqdnGroups(groupData) : [],
            DnsRoutes = routes is { } routeData ? KeeneticRci.ParseDnsRoutes(routeData) : []
        };
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
            InvalidateAuthentication();

            if (!await EnsureAuthenticatedAsync())
                throw new InvalidOperationException("Authentication failed");

            response = await PostRciAsync(payload);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"RCI error: {response.StatusCode} - {body}");

            // A rejected command comes back as 200 OK with the reason in the body.
            if (KeeneticRci.ReadError(body) is { } error)
                throw new InvalidOperationException($"RCI rejected the command: {error}");

            return body;
        }
    }

    private Task<HttpResponseMessage> PostRciAsync(string payload)
    {
        var content = new StringContent(payload, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return _httpClient.PostAsync(RciUrl, content);
    }

    /// <summary>
    /// VPN-capable interfaces reported by the router, in a stable order, so the profile keeps pointing at
    /// a connection that really exists.
    /// </summary>
    public async Task<List<VpnInterfaceInfo>> GetVpnInterfacesAsync()
    {
        if (!await EnsureAuthenticatedAsync())
            throw new InvalidOperationException("Authentication failed");

        var interfaces = await ShowAsync("interface");

        return interfaces is { } data
            ? [.. KeeneticRci.ParseInterfaces(data)
                .Where(item => KeeneticRci.IsVpnInterface(item.Name, item.Type))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)]
            : [];
    }

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

            groups.Add(new RouterRouteGroup(route.Group, route.Interface, [.. group.Domains]));
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
            await SendBatchAsync(KeeneticRci.BuildDeleteRouteCommand(route), SaveConfigurationCommand);

        foreach (var group in state.FqdnGroups.Where(entry => DnsRouting.IsRoutingGroup(entry.Name) && !keep.Contains(entry.Name)))
            await SendBatchAsync(KeeneticRci.BuildDeleteGroupCommand(group.Name), SaveConfigurationCommand);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _authenticationLock.Dispose();
    }
}
