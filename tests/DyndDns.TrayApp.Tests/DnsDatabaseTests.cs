using System.IO;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class DnsDatabaseTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "dyndn-dns-" + Guid.NewGuid().ToString("N") + ".db");

    [Fact]
    public void AddHits_AggregatesCountersPerDomain()
    {
        var database = new DnsDatabase(_databasePath);
        database.Initialize();

        database.AddHits(new Dictionary<string, int> { ["a.com"] = 2 });
        database.AddHits(new Dictionary<string, int> { ["a.com"] = 3 });

        var row = Assert.Single(database.Search("a.com", 10));
        Assert.Equal("a.com", row.Domain);
        Assert.Equal(5, row.Hits);
    }

    [Fact]
    public void Search_FiltersBySubstringAndBreaksTimestampTiesByHits()
    {
        var database = new DnsDatabase(_databasePath);
        database.Initialize();

        database.AddHits(new Dictionary<string, int>
        {
            ["low.com"] = 1,
            ["high.com"] = 9,
            ["other.net"] = 5
        });

        Assert.Equal(new[] { "high.com", "low.com" }, database.Search("com", 10).Select(row => row.Domain));
        Assert.Equal(3, database.Search(null, 10).Count);
    }

    [Fact]
    public void Search_ReturnsTheMostRecentlySeenDomainsFirst()
    {
        var database = new DnsDatabase(_databasePath);
        database.Initialize();

        database.AddHits(new Dictionary<string, int> { ["old.com"] = 9 });
        Thread.Sleep(20);
        database.AddHits(new Dictionary<string, int> { ["new.com"] = 1 });

        Assert.Equal(new[] { "new.com", "old.com" }, database.Search(null, 10).Select(row => row.Domain));
    }

    [Fact]
    public void Search_RespectsLimit()
    {
        var database = new DnsDatabase(_databasePath);
        database.Initialize();

        database.AddHits(new Dictionary<string, int> { ["a.com"] = 3, ["b.com"] = 2, ["c.com"] = 1 });

        Assert.Equal(new[] { "a.com", "b.com" }, database.Search(null, 2).Select(row => row.Domain));
    }

    [Fact]
    public void AddVisits_KeepsTheHighestCounterAndStillAddsEtvHits()
    {
        var database = new DnsDatabase(_databasePath);
        database.Initialize();

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

    public void Dispose()
    {
        // Connections are pooled, so the file handle has to be released before deleting it.
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        GC.SuppressFinalize(this);
    }
}
