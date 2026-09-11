using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class DnsDatabaseTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    [Fact]
    public void AddHits_AggregatesCountersPerDomain()
    {
        var database = CreateDatabase();

        database.AddHits(new Dictionary<string, int> { ["a.com"] = 2 });
        database.AddHits(new Dictionary<string, int> { ["a.com"] = 3 });

        var row = Assert.Single(database.Search("a.com", 10));
        Assert.Equal("a.com", row.Domain);
        Assert.Equal(5, row.Hits);
    }

    [Fact]
    public void Search_FiltersBySubstringAndBreaksTimestampTiesByHits()
    {
        var database = CreateDatabase();

        database.AddHits(new Dictionary<string, int>
        {
            ["low.com"] = 1,
            ["high.com"] = 9,
            ["other.net"] = 5
        });

        Assert.Equal(["high.com", "low.com"], database.Search("com", 10).Select(row => row.Domain));
        Assert.Equal(3, database.Search(null, 10).Count);
    }

    [Fact]
    public void Search_ReturnsTheMostRecentlySeenDomainsFirst()
    {
        var database = CreateDatabase();

        database.AddHits(new Dictionary<string, int> { ["old.com"] = 9 });
        Thread.Sleep(20);
        database.AddHits(new Dictionary<string, int> { ["new.com"] = 1 });

        Assert.Equal(["new.com", "old.com"], database.Search(null, 10).Select(row => row.Domain));
    }

    [Fact]
    public void Search_RespectsLimit()
    {
        var database = CreateDatabase();

        database.AddHits(new Dictionary<string, int> { ["a.com"] = 3, ["b.com"] = 2, ["c.com"] = 1 });

        Assert.Equal(["a.com", "b.com"], database.Search(null, 2).Select(row => row.Domain));
    }

    [Fact]
    public void AddVisits_KeepsTheHighestCounterAndStillAddsEtvHits()
    {
        var database = CreateDatabase();

        var moment = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
        database.AddVisits(new Dictionary<string, DomainVisit> { ["a.com"] = new(4, moment) });

        // The same history is imported on every change, so the counter must not grow.
        database.AddVisits(new Dictionary<string, DomainVisit> { ["a.com"] = new(4, moment.AddMinutes(5)) });

        var row = Assert.Single(database.Search("a.com", 10));
        Assert.Equal(4L, row.Hits);
        Assert.Equal(moment.AddMinutes(5), row.LastSeen);

        // Live lookups of the same domain keep accumulating on top of the imported visits.
        database.AddHits(new Dictionary<string, int> { ["a.com"] = 2 });

        Assert.Equal(6L, Assert.Single(database.Search("a.com", 10)).Hits);
    }

    [Fact]
    public void Search_WorksOnASharedDatabaseWithProfiles()
    {
        var shared = _temp.Database;

        // The journal and the settings live in one file, so the DNS tables must be usable next to them.
        var store = new SettingsStore(shared);
        store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1", Password = "secret" });

        var database = new DnsDatabase(shared);
        database.AddHits(new Dictionary<string, int> { ["a.com"] = 1 });

        Assert.Single(database.Search(null, 10));
        Assert.Single(store.GetRouters());
    }

    [Fact]
    public void DeleteUnbound_DropsOnlyTheDomainsNoBindingMentions()
    {
        var shared = _temp.Database;
        var store = new SettingsStore(shared);
        var profile = store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1", Password = "secret" });
        store.AddRoute(profile.Id, "Bound.com", "L2TP0");

        var database = new DnsDatabase(shared);
        database.AddHits(new Dictionary<string, int> { ["bound.com"] = 1, ["lonely.net"] = 3 });

        // A bound domain stays in the journal whatever casing the lookup recorded it with.
        Assert.Equal(1, database.DeleteUnbound());
        Assert.Equal("bound.com", Assert.Single(database.Search(null, 10)).Domain);

        // With no binding left, the journal empties.
        store.RemoveRoute(profile.Id, "Bound.com");
        database.AddHits(new Dictionary<string, int> { ["other.net"] = 1 });

        Assert.Equal(2, database.DeleteUnbound());
        Assert.Empty(database.Search(null, 10));
    }

    [Fact]
    public void TrimUnbound_KeepsTheNewestEntriesAndIgnoresTheLimitWhenItIsOff()
    {
        var database = CreateDatabase();

        database.AddHits(new Dictionary<string, int> { ["oldest.net"] = 1 });
        Thread.Sleep(20);
        database.AddHits(new Dictionary<string, int> { ["middle.net"] = 1 });
        Thread.Sleep(20);
        database.AddHits(new Dictionary<string, int> { ["newest.net"] = 1 });

        // A limit of zero means the cleanup is off, so nothing is dropped.
        Assert.Equal(0, database.TrimUnbound(0));
        Assert.Equal(3, database.Search(null, 10).Count);

        Assert.Equal(1, database.TrimUnbound(2));
        Assert.Equal(["newest.net", "middle.net"], database.Search(null, 10).Select(row => row.Domain));

        // Within the limit there is nothing left to drop.
        Assert.Equal(0, database.TrimUnbound(2));
    }

    [Fact]
    public void TrimUnbound_LeavesTheBoundDomainsAlone()
    {
        var shared = _temp.Database;
        var store = new SettingsStore(shared);
        var profile = store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1", Password = "secret" });
        store.AddRoute(profile.Id, "Bound.com", "L2TP0");

        var database = new DnsDatabase(shared);
        database.AddHits(new Dictionary<string, int> { ["bound.com"] = 1 });
        Thread.Sleep(20);
        database.AddHits(new Dictionary<string, int> { ["fresh.net"] = 1 });

        // The bound domain is older than the single entry the limit allows, yet it is neither counted towards
        // the limit nor removed; only unbound domains compete for the room.
        Assert.Equal(0, database.TrimUnbound(1));
        Assert.Equal(["fresh.net", "bound.com"], database.Search(null, 10).Select(row => row.Domain));
    }

    private DnsDatabase CreateDatabase() => new(_temp.Database);

    public void Dispose() => _temp.Dispose();
}
