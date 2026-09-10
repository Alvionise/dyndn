using System.Globalization;
using DyndDns.TrayApp.Models;
using Microsoft.Data.Sqlite;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Stores aggregated DNS hits in a local SQLite file. Only the domain and its counters are kept:
/// per the requirements the process and the exact time of each lookup are not interesting.
/// Domains imported from a browser history share the same table.
/// </summary>
internal sealed class DnsDatabase
{
    private readonly string _connectionString;

    public DnsDatabase(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS dns_queries (
                domain     TEXT    NOT NULL PRIMARY KEY,
                hits       INTEGER NOT NULL DEFAULT 0,
                first_seen TEXT    NOT NULL,
                last_seen  TEXT    NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Adds counters to existing rows, creating the missing ones.</summary>
    public void AddHits(IReadOnlyDictionary<string, int> hits)
    {
        if (hits.Count == 0)
            return;

        var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO dns_queries (domain, hits, first_seen, last_seen)
            VALUES ($domain, $hits, $now, $now)
            ON CONFLICT(domain) DO UPDATE SET
                hits = hits + excluded.hits,
                last_seen = excluded.last_seen;
            """;

        var domainParameter = command.CreateParameter();
        domainParameter.ParameterName = "$domain";
        command.Parameters.Add(domainParameter);

        var hitsParameter = command.CreateParameter();
        hitsParameter.ParameterName = "$hits";
        command.Parameters.Add(hitsParameter);

        var nowParameter = command.CreateParameter();
        nowParameter.ParameterName = "$now";
        nowParameter.Value = now;
        command.Parameters.Add(nowParameter);

        foreach (var entry in hits)
        {
            domainParameter.Value = entry.Key;
            hitsParameter.Value = entry.Value;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Merges visits coming from a browser history. Counters are raised to the highest known value
    /// instead of being added up, because the same history is imported again on every change.
    /// </summary>
    public void AddVisits(IReadOnlyDictionary<string, DomainVisit> visits)
    {
        if (visits.Count == 0)
            return;

        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO dns_queries (domain, hits, first_seen, last_seen)
            VALUES ($domain, $hits, $lastSeen, $lastSeen)
            ON CONFLICT(domain) DO UPDATE SET
                hits = MAX(hits, excluded.hits),
                last_seen = MAX(last_seen, excluded.last_seen);
            """;

        var domainParameter = command.CreateParameter();
        domainParameter.ParameterName = "$domain";
        command.Parameters.Add(domainParameter);

        var hitsParameter = command.CreateParameter();
        hitsParameter.ParameterName = "$hits";
        command.Parameters.Add(hitsParameter);

        var lastSeenParameter = command.CreateParameter();
        lastSeenParameter.ParameterName = "$lastSeen";
        command.Parameters.Add(lastSeenParameter);

        foreach (var entry in visits)
        {
            domainParameter.Value = entry.Key;
            hitsParameter.Value = entry.Value.Hits;
            lastSeenParameter.Value = entry.Value.LastSeen.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Newest lookups first, so a domain typed in the browser is visible immediately; equal
    /// timestamps fall back to the hit count and the name. An empty term returns everything.
    /// </summary>
    public IReadOnlyList<DomainStat> Search(string? term, int limit)
    {
        var results = new List<DomainStat>();
        var hasTerm = !string.IsNullOrWhiteSpace(term);

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = hasTerm
            ? "SELECT domain, hits, first_seen, last_seen FROM dns_queries WHERE domain LIKE $term ORDER BY last_seen DESC, hits DESC, domain LIMIT $limit;"
            : "SELECT domain, hits, first_seen, last_seen FROM dns_queries ORDER BY last_seen DESC, hits DESC, domain LIMIT $limit;";

        if (hasTerm)
            command.Parameters.AddWithValue("$term", $"%{term!.Trim()}%");

        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(new DomainStat(
                reader.GetString(0),
                reader.GetInt64(1),
                ParseTimestamp(reader.GetString(2)),
                ParseTimestamp(reader.GetString(3))));
        }

        return results;
    }

    private static DateTime ParseTimestamp(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.MinValue;

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
