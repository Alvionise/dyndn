using System.Net;
using System.Net.Http;
using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class RouterDiscoveryTests
{
    [Fact]
    public void ReadDeviceName_TakesTheNameOutOfTheAuthenticationChallenge()
    {
        using var full = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        full.Headers.Add("X-NDM-Realm", "Keenetic Giga SE");
        full.Headers.Add("X-Ndm-Product", "Giga SE");

        Assert.Equal("Keenetic Giga SE", RouterDiscoveryService.ReadDeviceName(full));

        // Firmware that sends only the product header still gives the model away.
        using var productOnly = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        productOnly.Headers.Add("X-Ndm-Product", " Giga SE ");

        Assert.Equal("Giga SE", RouterDiscoveryService.ReadDeviceName(productOnly));

        using var anonymous = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        Assert.Equal(string.Empty, RouterDiscoveryService.ReadDeviceName(anonymous));
    }
    [Fact]
    public void EnumerateHosts_ReturnsUsableHostsFor24Subnet()
    {
        var subnet = new Ipv4Subnet(IPAddress.Parse("192.168.1.10"), IPAddress.Parse("255.255.255.0"));

        var hosts = RouterDiscoveryService.EnumerateHosts(subnet, 1024).ToList();

        Assert.Equal(254, hosts.Count);
        Assert.Equal("192.168.1.1", hosts[0]);
        Assert.Equal("192.168.1.254", hosts[^1]);
    }

    [Fact]
    public void EnumerateHosts_HonoursLimit()
    {
        var subnet = new Ipv4Subnet(IPAddress.Parse("10.0.0.5"), IPAddress.Parse("255.255.255.0"));

        var hosts = RouterDiscoveryService.EnumerateHosts(subnet, 3).ToList();

        Assert.Equal(new[] { "10.0.0.1", "10.0.0.2", "10.0.0.3" }, hosts);
    }

    [Fact]
    public void EnumerateHosts_SkipsPointToPointSubnets()
    {
        var subnet = new Ipv4Subnet(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("255.255.255.255"));

        Assert.Empty(RouterDiscoveryService.EnumerateHosts(subnet, 1024));
    }

    [Fact]
    public void BuildCandidateAddresses_PutsCommonHostsFirstAndDeduplicates()
    {
        Ipv4Subnet[] subnets = [new Ipv4Subnet(IPAddress.Parse("192.168.1.10"), IPAddress.Parse("255.255.255.0"))];

        var candidates = RouterDiscoveryService.BuildCandidateAddresses(
            ["192.168.1.1", "my.keenetic.net"],
            ["192.168.1.1"],
            subnets);

        Assert.Equal("192.168.1.1", candidates[0]);
        Assert.Equal("my.keenetic.net", candidates[1]);
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void IsKeeneticResponse_RequiresUnauthorizedStatusAndChallengeHeader()
    {
        using var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        challenge.Headers.Add("X-NDM-Challenge", "abc");
        Assert.True(RouterDiscoveryService.IsKeeneticResponse(challenge));

        using var realmOnly = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        realmOnly.Headers.Add("X-NDM-Realm", "Keenetic");
        Assert.True(RouterDiscoveryService.IsKeeneticResponse(realmOnly));

        using var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        Assert.False(RouterDiscoveryService.IsKeeneticResponse(unauthorized));

        using var success = new HttpResponseMessage(HttpStatusCode.OK);
        success.Headers.Add("X-NDM-Challenge", "abc");
        Assert.False(RouterDiscoveryService.IsKeeneticResponse(success));
    }
}
