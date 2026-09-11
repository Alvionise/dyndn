using System.Diagnostics;
using System.IO;
using DyndDns.TrayApp.Models;
using Microsoft.Data.Sqlite;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Reads the domains a user actually visited out of the browser history. Browsers resolve names with
/// their own resolver, so their lookups never reach the <c>Microsoft-Windows-DNS-Client</c> ETW
/// provider; the visit history is what still shows the sites in use.
/// </summary>
internal sealed class BrowserHistoryReader
{
    // Chromium keeps microseconds since 1601-01-01 UTC, Firefox microseconds since the Unix epoch.
    private static readonly DateTime ChromiumEpoch = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FirefoxEpoch = DateTime.UnixEpoch;

    /// <summary>History files of the browsers installed for the current user.</summary>
    public IReadOnlyList<BrowserHistoryDatabase> Discover()
    {
        var databases = new List<BrowserHistoryDatabase>();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        AddChromiumProfiles(databases, Path.Combine(localAppData, "Google", "Chrome", "User Data"));
        AddChromiumProfiles(databases, Path.Combine(localAppData, "Microsoft", "Edge", "User Data"));
        AddChromiumProfiles(databases, Path.Combine(localAppData, "Yandex", "YandexBrowser", "User Data"));
        AddChromiumProfiles(databases, Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"));
        AddChromiumProfiles(databases, Path.Combine(localAppData, "Vivaldi", "User Data"));

        // Opera keeps its history in the profile folder itself rather than in a "Default" subfolder.
        AddChromiumProfiles(databases, Path.Combine(roamingAppData, "Opera Software", "Opera Stable"), includeRoot: true);

        AddFirefoxProfiles(databases, Path.Combine(roamingAppData, "Mozilla", "Firefox", "Profiles"));

        return databases;
    }

    /// <summary>
    /// Aggregates visited domains over the given databases: counters add up across browsers while the
    /// newest visit of a domain wins.
    /// </summary>
    public IReadOnlyDictionary<string, DomainVisit> Read(IEnumerable<BrowserHistoryDatabase> databases)
    {
        var visits = new Dictionary<string, DomainVisit>(StringComparer.OrdinalIgnoreCase);

        foreach (var database in databases)
        {
            foreach (var (domain, visit) in ReadDatabase(database))
                visits[domain] = Merge(visits.TryGetValue(domain, out var existing) ? existing : default, visit);
        }

        return visits;
    }

    private static IReadOnlyDictionary<string, DomainVisit> ReadDatabase(BrowserHistoryDatabase database)
    {
        var visits = new Dictionary<string, DomainVisit>(StringComparer.OrdinalIgnoreCase);
        var snapshot = Path.Combine(Path.GetTempPath(), $"dyndns-history-{Guid.NewGuid():N}.db");

        try
        {
            // The browser holds the file open, so a private copy is queried instead of the original.
            File.Copy(database.Path, snapshot, overwrite: true);

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = snapshot,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());

            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = database.Format == BrowserHistoryFormat.Chromium
                ? "SELECT url, visit_count, last_visit_time FROM urls WHERE visit_count > 0;"
                : "SELECT url, visit_count, last_visit_date FROM moz_places WHERE visit_count > 0;";

            var epoch = database.Format == BrowserHistoryFormat.Chromium ? ChromiumEpoch : FirefoxEpoch;

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var domain = ToDomain(reader.GetString(0));

                if (domain is null)
                    continue;

                var hits = reader.GetInt64(1);
                var micros = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                var visit = new DomainVisit(hits, ToTimestamp(micros, epoch));

                visits[domain] = Merge(visits.TryGetValue(domain, out var existing) ? existing : default, visit);
            }
        }
        catch (Exception ex)
        {
            // A browser writing mid-copy can leave an unreadable snapshot; the next run retries.
            Trace.TraceWarning($"Browser history '{database.Path}' skipped: {ex.Message}");
        }
        finally
        {
            TryDelete(snapshot);
        }

        return visits;
    }

    private static DomainVisit Merge(DomainVisit target, DomainVisit addition) =>
        new(target.Hits + addition.Hits,
            target.LastSeen > addition.LastSeen ? target.LastSeen : addition.LastSeen);

    /// <summary>
    /// History files count microseconds from their own epoch. Ticks keep the conversion exact, so no
    /// floating point is involved; a missing value means the visit time is unknown.
    /// </summary>
    private static DateTime ToTimestamp(long microseconds, DateTime epoch)
    {
        if (microseconds <= 0)
            return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

        // A history file can carry an absurd value, and the conversion is clamped instead of letting the addition
        // throw: one bad row would otherwise lose the whole history of that browser.
        var ticks = microseconds > (DateTime.MaxValue.Ticks - epoch.Ticks) / 10
            ? DateTime.MaxValue.Ticks
            : epoch.Ticks + microseconds * 10;

        return new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>Maps a history URL to the bare domain, skipping local pages and other schemes.</summary>
    private static string? ToDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        var domain = DomainNormalizer.Normalize(uri.Host);
        return domain.Length == 0 ? null : domain;
    }

    private static void AddChromiumProfiles(
        List<BrowserHistoryDatabase> databases,
        string userDataDirectory,
        bool includeRoot = false)
    {
        if (!Directory.Exists(userDataDirectory))
            return;

        var profileDirectories = new List<string>();

        if (includeRoot)
            profileDirectories.Add(userDataDirectory);

        foreach (var pattern in new[] { "Default", "Profile *" })
        {
            try
            {
                profileDirectories.AddRange(Directory.EnumerateDirectories(userDataDirectory, pattern));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A profile being created or unreadable is simply skipped.
            }
        }

        foreach (var profile in profileDirectories)
        {
            var history = Path.Combine(profile, "History");

            if (File.Exists(history))
                databases.Add(new BrowserHistoryDatabase(history, BrowserHistoryFormat.Chromium));
        }
    }

    private static void AddFirefoxProfiles(List<BrowserHistoryDatabase> databases, string profilesDirectory)
    {
        if (!Directory.Exists(profilesDirectory))
            return;

        try
        {
            foreach (var profile in Directory.EnumerateDirectories(profilesDirectory))
            {
                var places = Path.Combine(profile, "places.sqlite");

                if (File.Exists(places))
                    databases.Add(new BrowserHistoryDatabase(places, BrowserHistoryFormat.Firefox));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable profile folder: nothing to import.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover file in the temp folder is harmless.
        }
    }
}
