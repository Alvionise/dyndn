using DyndDns.TrayApp.Models;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class DnsRoutingTests
{
    [Fact]
    public void GroupByInterface_SplitsRoutesPerInterface()
    {
        var routes = new[]
        {
            new DnsRoute("a.com", "L2TP0"),
            new DnsRoute("b.com", "L2TP0"),
            new DnsRoute("c.com", "SSTP0")
        };

        var groups = DnsRouting.GroupByInterface(routes, "L2TP0");

        Assert.Equal(2, groups.Count);

        var l2tp = Assert.Single(groups, group => group.Interface == "L2TP0");
        Assert.Equal("dyndns-L2TP0", l2tp.GroupName);
        Assert.Equal(new[] { "a.com", "b.com" }, l2tp.Domains);

        var sstp = Assert.Single(groups, group => group.Interface == "SSTP0");
        Assert.Equal("dyndns-SSTP0", sstp.GroupName);
        Assert.Equal(new[] { "c.com" }, sstp.Domains);
    }

    [Fact]
    public void GroupByInterface_FallsBackToDefaultInterface()
    {
        var groups = DnsRouting.GroupByInterface(new[] { new DnsRoute("a.com", string.Empty) }, "L2TP0");

        var group = Assert.Single(groups);
        Assert.Equal("L2TP0", group.Interface);
        Assert.Equal("dyndns-L2TP0", group.GroupName);
    }

    [Fact]
    public void GroupByInterface_DropsRoutesWithoutAnyInterface()
    {
        var groups = DnsRouting.GroupByInterface(new[] { new DnsRoute("a.com", string.Empty) }, string.Empty);

        Assert.Empty(groups);
    }

    [Fact]
    public void GroupByInterface_RemovesDuplicateDomains()
    {
        var routes = new[]
        {
            new DnsRoute("a.com", "L2TP0"),
            new DnsRoute("A.COM", "L2TP0")
        };

        var group = Assert.Single(DnsRouting.GroupByInterface(routes, "L2TP0"));

        Assert.Single(group.Domains);
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
        var groups = new[]
        {
            new RouterRouteGroup("dyndns-L2TP0", "L2TP0", new[] { "a.com" }),
            new RouterRouteGroup("default", "SSTP0", new[] { "legacy.com" })
        };

        var routes = DnsRouting.FromRouterGroups(groups);

        Assert.Equal(new DnsRoute("a.com", "L2TP0"), Assert.Single(routes));
    }

    [Fact]
    public void MergeRouterRoutes_AdoptsTheInterfaceTheRouterReports()
    {
        var local = new[] { new DnsRoute("a.com", "SSTP0") };
        var router = new[] { new DnsRoute("a.com", "L2TP0") };

        var merged = DnsRouting.MergeRouterRoutes(local, router);

        Assert.Equal(new DnsRoute("a.com", "L2TP0"), Assert.Single(merged));
    }

    [Fact]
    public void MergeRouterRoutes_AddsDomainsThatOnlyTheRouterKnows()
    {
        var local = new[] { new DnsRoute("a.com", "L2TP0") };
        var router = new[] { new DnsRoute("b.com", "L2TP0") };

        var merged = DnsRouting.MergeRouterRoutes(local, router);

        Assert.Equal(new DnsRoute("a.com", "L2TP0"), merged[0]);
        Assert.Equal(new DnsRoute("b.com", "L2TP0"), merged[1]);
    }

    [Fact]
    public void MergeRouterRoutes_KeepsLocalEntriesThatAreNotSyncedYet()
    {
        var local = new[] { new DnsRoute("pending.com", "L2TP0") };

        var merged = DnsRouting.MergeRouterRoutes(local, Array.Empty<DnsRoute>());

        Assert.Equal(local, merged);
    }

    [Fact]
    public void MergeRouterRoutes_ReturnsTheSameSetWhenEverythingMatches()
    {
        var local = new[] { new DnsRoute("a.com", "L2TP0") };
        var router = new[] { new DnsRoute("A.COM", "l2tp0") };

        var merged = DnsRouting.MergeRouterRoutes(local, router);

        Assert.Equal(local, merged);
    }
}
