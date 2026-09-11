using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class DnsRoutingTests
{
    [Fact]
    public void GroupByInterface_SplitsRoutesPerInterface()
    {
        DnsRoute[] routes =
        [
            new DnsRoute("a.com", "L2TP0"),
            new DnsRoute("b.com", "L2TP0"),
            new DnsRoute("c.com", "SSTP0")
        ];

        var groups = DnsRouting.GroupByInterface(routes, "L2TP0");

        Assert.Equal(2, groups.Count);

        var (_, groupName, domains) = Assert.Single(groups, group => group.Interface == "L2TP0");

        Assert.Equal("dyndns-L2TP0", groupName);
        Assert.Equal(["a.com", "b.com"], domains);

        var (_, sstpGroupName, sstpDomains) = Assert.Single(groups, group => group.Interface == "SSTP0");

        Assert.Equal("dyndns-SSTP0", sstpGroupName);
        Assert.Equal(["c.com"], sstpDomains);
    }

    [Fact]
    public void GroupByInterface_FallsBackToDefaultInterface()
    {
        var groups = DnsRouting.GroupByInterface([new DnsRoute("a.com", string.Empty)], "L2TP0");

        var (interfaceName, groupName, _) = Assert.Single(groups);

        Assert.Equal("L2TP0", interfaceName);
        Assert.Equal("dyndns-L2TP0", groupName);
    }

    [Fact]
    public void GroupByInterface_DropsRoutesWithoutAnyInterface()
    {
        var groups = DnsRouting.GroupByInterface([new DnsRoute("a.com", string.Empty)], string.Empty);

        Assert.Empty(groups);
    }

    [Fact]
    public void GroupByInterface_RemovesDuplicateDomains()
    {
        DnsRoute[] routes =
        [
            new DnsRoute("a.com", "L2TP0"),
            new DnsRoute("A.COM", "L2TP0")
        ];

        var (_, _, domains) = Assert.Single(DnsRouting.GroupByInterface(routes, "L2TP0"));

        Assert.Single(domains);
    }

    [Theory]
    [InlineData("dyndns-L2TP0", true)]
    [InlineData("DYNDNS-SSTP0", true)]
    [InlineData("default", false)]
    [InlineData("OpenVPN", false)]
    public void IsRoutingGroup_MatchesManagedGroups(string name, bool expected) =>
        Assert.Equal(expected, DnsRouting.IsRoutingGroup(name));

    [Fact]
    public void GetGroupName_PrefixesInterface() =>
        Assert.Equal("dyndns-Wireguard0", DnsRouting.GetGroupName("Wireguard0"));

    [Fact]
    public void FromRouterGroups_KeepsOnlyManagedGroups()
    {
        RouterRouteGroup[] groups =
        [
            new RouterRouteGroup("dyndns-L2TP0", "L2TP0", ["a.com"]),
            new RouterRouteGroup("default", "SSTP0", ["legacy.com"])
        ];

        var routes = DnsRouting.FromRouterGroups(groups);

        Assert.Equal(new DnsRoute("a.com", "L2TP0"), Assert.Single(routes));
    }

    [Fact]
    public void MergeRouterRoutes_AdoptsTheInterfaceTheRouterReports()
    {
        DnsRoute[] local = [new DnsRoute("a.com", "SSTP0")];
        DnsRoute[] router = [new DnsRoute("a.com", "L2TP0")];

        var merged = DnsRouting.MergeRouterRoutes(local, router);

        Assert.Equal(new DnsRoute("a.com", "L2TP0"), Assert.Single(merged));
    }

    [Fact]
    public void MergeRouterRoutes_AddsDomainsThatOnlyTheRouterKnows()
    {
        DnsRoute[] local = [new DnsRoute("a.com", "L2TP0")];
        DnsRoute[] router = [new DnsRoute("b.com", "L2TP0")];

        var merged = DnsRouting.MergeRouterRoutes(local, router);

        Assert.Equal(new DnsRoute("a.com", "L2TP0"), merged[0]);
        Assert.Equal(new DnsRoute("b.com", "L2TP0"), merged[1]);
    }

    [Fact]
    public void MergeRouterRoutes_KeepsLocalEntriesThatAreNotSyncedYet()
    {
        DnsRoute[] local = [new DnsRoute("pending.com", "L2TP0")];

        var merged = DnsRouting.MergeRouterRoutes(local, []);

        Assert.Equal(local, merged);
    }

    [Fact]
    public void MergeRouterRoutes_ReturnsTheSameSetWhenEverythingMatches()
    {
        DnsRoute[] local = [new DnsRoute("a.com", "L2TP0")];
        DnsRoute[] router = [new DnsRoute("A.COM", "l2tp0")];

        var merged = DnsRouting.MergeRouterRoutes(local, router);

        Assert.Equal(local, merged);
    }
}
