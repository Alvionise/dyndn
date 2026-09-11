using System.IO;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class BrowserHistoryTests : IDisposable
{
    private static readonly DateTime ChromiumEpoch = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly List<string> _databases = [];

    [Fact]
    public void Read_AggregatesVisitsPerDomain()
    {
        var moment = new DateTime(2026, 9, 10, 12, 30, 0, DateTimeKind.Utc);
        var micros = ToChromiumMicros(moment);

        var path = CreateChromiumHistory(
            ("https://www.example.com/page", 3, micros),
            ("http://example.com/other", 2, micros - 60_000_000),
            ("chrome://settings", 5, micros),
            ("https://other.net/", 1, micros));

        var visits = new BrowserHistoryReader().Read([new BrowserHistoryDatabase(path, BrowserHistoryFormat.Chromium)]);

        Assert.Equal(2, visits.Count);
        Assert.Equal(5L, visits["example.com"].Hits);
        Assert.Equal(moment, visits["example.com"].LastSeen);
        Assert.Equal(1L, visits["other.net"].Hits);
    }

    [Fact]
    public void Read_MergesDomainsFromSeveralBrowsers()
    {
        var moment = new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc);

        var chromium = CreateChromiumHistory(("https://example.com/", 4, ToChromiumMicros(moment)));
        var firefox = CreateFirefoxHistory(("https://example.com/", 6, ToFirefoxMicros(moment.AddHours(1))));

        var visits = new BrowserHistoryReader().Read(
        [
            new BrowserHistoryDatabase(chromium, BrowserHistoryFormat.Chromium),
            new BrowserHistoryDatabase(firefox, BrowserHistoryFormat.Firefox)
        ]);

        var visit = Assert.Single(visits);
        Assert.Equal(10L, visit.Value.Hits);
        Assert.Equal(moment.AddHours(1), visit.Value.LastSeen);
    }

    [Fact]
    public void Read_ConvertsFirefoxTimestamps()
    {
        var moment = new DateTime(2026, 9, 1, 8, 15, 0, DateTimeKind.Utc);
        var path = CreateFirefoxHistory(("https://firefox.example.org/", 7, ToFirefoxMicros(moment)));

        var visits = new BrowserHistoryReader().Read([new BrowserHistoryDatabase(path, BrowserHistoryFormat.Firefox)]);

        Assert.Equal(7L, visits["firefox.example.org"].Hits);
        Assert.Equal(moment, visits["firefox.example.org"].LastSeen);
    }

    [Fact]
    public void Read_SurvivesAnAbsurdTimestamp()
    {
        // A corrupt value must neither throw nor wrap around: the conversion clamps it, so the visits of that
        // browser are still read instead of the whole database being skipped.
        var path = CreateChromiumHistory(("https://clamped.example.org/", 3, long.MaxValue));

        var visits = new BrowserHistoryReader().Read([new BrowserHistoryDatabase(path, BrowserHistoryFormat.Chromium)]);

        Assert.Equal(3L, visits["clamped.example.org"].Hits);
        Assert.Equal(DateTime.MaxValue.Ticks, visits["clamped.example.org"].LastSeen.Ticks);
    }

    [Fact]
    public void Read_IgnoresUnreadableDatabase()
    {
        var missing = Path.Combine(Path.GetTempPath(), "dyndns-missing-" + Guid.NewGuid().ToString("N") + ".db");

        var visits = new BrowserHistoryReader().Read([new BrowserHistoryDatabase(missing, BrowserHistoryFormat.Chromium)]);

        Assert.Empty(visits);
    }

    private static long ToChromiumMicros(DateTime moment) =>
        (long)((moment.ToUniversalTime() - ChromiumEpoch).TotalSeconds * 1_000_000);

    private static long ToFirefoxMicros(DateTime moment) =>
        (long)((moment.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds * 1_000_000);

    private string CreateChromiumHistory(params (string Url, long Hits, long Micros)[] rows) =>
        CreateHistory(
            "chrome",
            "CREATE TABLE urls (url TEXT NOT NULL, visit_count INTEGER NOT NULL, last_visit_time INTEGER NOT NULL);",
            rows.Select(row => $"INSERT INTO urls (url, visit_count, last_visit_time) VALUES ('{row.Url}', {row.Hits}, {row.Micros});"));

    private string CreateFirefoxHistory(params (string Url, long Hits, long Micros)[] rows) =>
        CreateHistory(
            "firefox",
            "CREATE TABLE moz_places (id INTEGER PRIMARY KEY, url TEXT, visit_count INTEGER, last_visit_date INTEGER);",
            rows.Select(row => $"INSERT INTO moz_places (url, visit_count, last_visit_date) VALUES ('{row.Url}', {row.Hits}, {row.Micros});"));

    private string CreateHistory(string name, string schema, IEnumerable<string> inserts)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dyndns-{name}-{Guid.NewGuid():N}.db");
        _databases.Add(path);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = schema;
            create.ExecuteNonQuery();
        }

        foreach (var insert in inserts)
        {
            using var command = connection.CreateCommand();
            command.CommandText = insert;
            command.ExecuteNonQuery();
        }

        return path;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var database in _databases.Where(File.Exists))
            File.Delete(database);

        GC.SuppressFinalize(this);
    }
}
