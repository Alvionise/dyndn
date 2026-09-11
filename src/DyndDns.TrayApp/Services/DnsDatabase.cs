using System.Globalization;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// The DNS journal: which domains were looked up on this machine, how often and when. The table lives in
/// the shared <see cref="AppDatabase"/>, next to the settings and the router profiles.
/// </summary>
internal sealed class DnsDatabase
{
    /// <summary>Matches the journal rows that no binding mentions; shared by the cleanup queries.</summary>
    private const string UnboundFilter = "domain NOT IN (SELECT domain COLLATE NOCASE FROM routes)";

    private readonly AppDatabase _database;

    public DnsDatabase(AppDatabase database) => _database = database;

    /// <summary>Adds counters to existing rows, creating the missing ones.</summary>
    public void AddHits(IReadOnlyDictionary<string, int> hits)
    {
        if (hits.Count == 0)
            return;

        var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO dns_queries (domain, hits, last_seen)
            VALUES ($domain, $hits, $now)
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

        command.Parameters.AddWithValue("$now", now);

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

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO dns_queries (domain, hits, last_seen)
            VALUES ($domain, $hits, $lastSeen)
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
    /// Drops the journal rows no binding mentions, i.e. the domains that are not routed anywhere; bound ones
    /// stay. The comparison ignores case, and the bindings live in the same database file.
    /// </summary>
    public int DeleteUnbound()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM dns_queries WHERE {UnboundFilter};";

        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Keeps the part of the journal that no binding mentions down to the <paramref name="keep"/> newest
    /// entries and returns how many rows were dropped, so a long running monitor does not grow the database
    /// without a limit. Bound domains are never removed, however old their last lookup is.
    /// </summary>
    public int TrimUnbound(int keep)
    {
        if (keep <= 0)
            return 0;

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            DELETE FROM dns_queries
             WHERE domain IN (
                 SELECT domain
                   FROM dns_queries
                  WHERE {UnboundFilter}
                  ORDER BY last_seen DESC, hits DESC, domain
                  LIMIT -1 OFFSET $keep);
            """;

        command.Parameters.AddWithValue("$keep", keep);

        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Newest lookups first, so a domain typed in the browser is visible immediately; equal
    /// timestamps fall back to the hit count and the name. An empty term returns everything.
    /// </summary>
    public IReadOnlyList<DomainStat> Search(string? term, int limit)
    {
        var results = new List<DomainStat>();
        var hasTerm = !string.IsNullOrWhiteSpace(term);

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = hasTerm
            ? "SELECT domain, hits, last_seen FROM dns_queries WHERE domain LIKE $term ORDER BY last_seen DESC, hits DESC, domain LIMIT $limit;"
            : "SELECT domain, hits, last_seen FROM dns_queries ORDER BY last_seen DESC, hits DESC, domain LIMIT $limit;";

        if (hasTerm)
            command.Parameters.AddWithValue("$term", $"%{term!.Trim()}%");

        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(new DomainStat(
                reader.GetString(0),
                reader.GetInt64(1),
                ParseTimestamp(reader.GetString(2))));
        }

        return results;
    }

    private static DateTime ParseTimestamp(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.MinValue;
}
