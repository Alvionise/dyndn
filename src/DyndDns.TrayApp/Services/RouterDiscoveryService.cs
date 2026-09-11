using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

internal readonly record struct DiscoveryProgress(int Scanned, int Total, int Found, string Message);

internal readonly record struct Ipv4Subnet(IPAddress Address, IPAddress Mask);

/// <summary>
/// Finds Keenetic devices on the local network. It probes the well-known addresses first, then
/// sweeps the /24 around every local address and default gateway over HTTP and HTTPS. A host is
/// recognized as Keenetic when <c>GET /auth</c> answers 401 with the NDM challenge headers.
/// </summary>
internal sealed class RouterDiscoveryService
{
    private static readonly string[] CommonHosts =
    [
        "192.168.1.1", "192.168.0.1", "192.168.10.1", "10.0.0.1", "my.keenetic.net"
    ];

    private static readonly string[] Schemes = ["http", "https"];

    private static readonly IPAddress ClassCMask = IPAddress.Parse("255.255.255.0");

    private const int MaxHostsPerSubnet = 254;
    private const int MaxParallelProbes = 128;

    private static readonly HttpClient ProbeClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromMilliseconds(1000)
    };

    public async Task<List<DiscoveredRouter>> DiscoverAsync(
        IProgress<DiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var gateways = LocalNetwork.DefaultGateways();
        var subnets = LocalNetwork.LocalAddresses()
            .Select(address => new Ipv4Subnet(address, ClassCMask))
            .Concat(gateways.Select(gateway => new Ipv4Subnet(gateway, ClassCMask)));

        var candidates = BuildCandidateAddresses(CommonHosts, gateways.Select(gateway => gateway.ToString()), subnets);
        var found = new ConcurrentDictionary<string, DiscoveredRouter>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        progress?.Report(new DiscoveryProgress(0, candidates.Count, 0, $"Поиск роутера: 0/{candidates.Count}"));

        using var gate = new SemaphoreSlim(MaxParallelProbes);

        var probes = candidates.Select(async candidate =>
        {
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var router = await ProbeAsync(candidate, cancellationToken).ConfigureAwait(false);

                if (router is not null)
                    found[router.Address] = router;
            }
            finally
            {
                gate.Release();

                var done = Interlocked.Increment(ref scanned);
                progress?.Report(new DiscoveryProgress(
                    done,
                    candidates.Count,
                    found.Count,
                    $"Поиск роутера: {done}/{candidates.Count}"));
            }
        }).ToArray();

        await Task.WhenAll(probes).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var routers = found.Values
            .OrderBy(router => router.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (routers.Count > 0)
        {
            progress?.Report(new DiscoveryProgress(candidates.Count, candidates.Count, routers.Count, "Определение имён устройств..."));
            await ApplyDeviceNamesAsync(routers, cancellationToken).ConfigureAwait(false);
        }

        return routers;
    }

    private static async Task ApplyDeviceNamesAsync(List<DiscoveredRouter> routers, CancellationToken cancellationToken)
    {
        var names = await SsdpDeviceLocator.ResolveNamesAsync(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < routers.Count; i++)
        {
            // The name from the device itself wins: the UPnP lookup is only a fallback, and on a network where
            // a provider's ONT is the gateway it is the ONT that answers, not the router this app manages.
            if (routers[i].Name.Length == 0 &&
                names.TryGetValue(routers[i].Address, out var name) &&
                name.Length > 0)
            {
                routers[i] = routers[i] with { Name = name };
            }
        }
    }

    private static async Task<DiscoveredRouter?> ProbeAsync(string host, CancellationToken cancellationToken)
    {
        var hostAnswered = false;

        foreach (var scheme in Schemes)
        {
            try
            {
                using var response = await ProbeClient.GetAsync($"{scheme}://{host}/auth", cancellationToken).ConfigureAwait(false);
                hostAnswered = true;

                if (IsKeeneticResponse(response))
                    return new DiscoveredRouter(host, ReadDeviceName(response));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Unreachable on this scheme. A host that did not answer over HTTP will not answer
                // over HTTPS either, so stop probing it and keep the sweep fast.
                if (!hostAnswered)
                    return null;
            }
        }

        return null;
    }

    internal static bool IsKeeneticResponse(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Unauthorized &&
        (response.Headers.Contains("X-NDM-Challenge") || response.Headers.Contains("X-NDM-Realm"));

    /// <summary>
    /// The name the router gives away before anyone signs in: the realm of the authentication challenge is the
    /// name of the device («Keenetic Giga SE»), and the product header carries at least its model. This is the
    /// only name a scan can have, because many routers do not answer the UPnP lookup at all.
    /// </summary>
    internal static string ReadDeviceName(HttpResponseMessage response) =>
        ReadHeader(response, "X-NDM-Realm") is { Length: > 0 } realm
            ? realm
            : ReadHeader(response, "X-Ndm-Product");

    private static string ReadHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty
            : string.Empty;

    internal static IReadOnlyList<string> BuildCandidateAddresses(
        IEnumerable<string> commonHosts,
        IEnumerable<string> gateways,
        IEnumerable<Ipv4Subnet> subnets)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string value)
        {
            if (value.Length > 0 && seen.Add(value))
                candidates.Add(value);
        }

        foreach (var host in commonHosts)
            Add(host);

        foreach (var gateway in gateways)
            Add(gateway);

        foreach (var subnet in subnets)
        {
            foreach (var host in EnumerateHosts(subnet, MaxHostsPerSubnet))
                Add(host);
        }

        return candidates;
    }

    internal static IEnumerable<string> EnumerateHosts(Ipv4Subnet subnet, int maxHosts)
    {
        var mask = ToUInt32(subnet.Mask);
        var network = ToUInt32(subnet.Address) & mask;
        var broadcast = network | ~mask;

        if (broadcast <= network + 1)
            yield break;

        var hostCount = broadcast - network - 1;
        var limit = Math.Min(hostCount, (uint)Math.Max(maxHosts, 0));

        for (uint offset = 1; offset <= limit; offset++)
            yield return FromUInt32(network + offset).ToString();
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
}
