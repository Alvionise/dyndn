using System.Diagnostics;
using System.IO;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Wires the DNS sources to the shared database: ETW hits are aggregated in memory and flushed in
/// batches, while the browser history is imported whenever a browser wrote to it. Both land in the same
/// table, so the search window sees service lookups and visited sites alike.
/// </summary>
internal sealed class DnsMonitorService : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HistoryInterval = TimeSpan.FromSeconds(30);

    private readonly DnsDatabase _database;
    private readonly AppConfig _config;
    private readonly DnsMonitor _monitor = new();
    private readonly BrowserHistoryReader _historyReader = new();
    private readonly Dictionary<string, DateTime> _historyStamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _historyLock = new();

    private System.Threading.Timer? _flushTimer;
    private System.Threading.Timer? _historyTimer;

    public DnsMonitorService(AppDatabase database, AppConfig config)
    {
        _database = new DnsDatabase(database);
        _config = config;
    }

    public bool IsRunning => _monitor.IsRunning;

    /// <summary>
    /// Returns <c>false</c> when the ETW session is unavailable. Nothing is started in that case: the menu
    /// reports the monitor as off, so it may not keep collecting in the background either.
    /// </summary>
    public bool Start()
    {
        if (!_monitor.Start())
            return false;

        StartBrowserHistoryImport();
        _flushTimer ??= new System.Threading.Timer(_ => Flush(), null, FlushInterval, FlushInterval);
        return true;
    }

    /// <summary>Stops collecting; searching keeps working because it reads the database directly.</summary>
    public void Stop()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;

        _historyTimer?.Dispose();
        _historyTimer = null;

        Flush();
        _monitor.Stop();
    }

    public IReadOnlyList<DomainStat> Search(string? term, int limit) => _database.Search(term, limit);

    /// <summary>
    /// Removes the journal entries that no binding mentions; returns how many were dropped. The counters
    /// collected so far are written first, otherwise the batch still in memory would put the deleted domains
    /// back on the next flush and the list would fill up again a few seconds later.
    /// </summary>
    public int ClearUnboundDomains()
    {
        Flush();

        return _database.DeleteUnbound();
    }

    /// <summary>
    /// Browsers resolve names with their own resolver, so their lookups never reach the ETW provider;
    /// their visit history is imported instead, once at startup and then on a timer.
    /// </summary>
    private void StartBrowserHistoryImport()
    {
        if (!_config.Monitor.BrowserHistoryEnabled)
            return;

        ImportBrowserHistory();
        _historyTimer ??= new System.Threading.Timer(_ => ImportBrowserHistory(), null, HistoryInterval, HistoryInterval);
    }

    private void ImportBrowserHistory()
    {
        lock (_historyLock)
        {
            try
            {
                var changed = _historyReader.Discover().Where(database => WasChanged(database.Path)).ToList();

                if (changed.Count == 0)
                    return;

                var visits = _historyReader.Read(changed);

                if (visits.Count == 0)
                    return;

                _database.AddVisits(visits);
                TrimJournal();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Browser history import failed: {ex.Message}");
            }
        }
    }

    /// <summary>True when the browser wrote to the history file since the previous import.</summary>
    private bool WasChanged(string path)
    {
        DateTime stamp;

        try
        {
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return false;
        }

        if (_historyStamps.TryGetValue(path, out var previous) && previous == stamp)
            return false;

        _historyStamps[path] = stamp;
        return true;
    }

    private void Flush()
    {
        try
        {
            var hits = _monitor.DrainHits();

            if (hits.Count == 0)
                return;

            _database.AddHits(hits);
            TrimJournal();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"DNS flush failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies the configured journal limit. Only domains that no binding mentions are dropped, so a domain
    /// routed somewhere never leaves the list however old its last lookup is; bound entries do not count
    /// towards the limit either.
    /// </summary>
    private void TrimJournal()
    {
        var monitor = _config.Monitor;

        if (!monitor.JournalAutoCleanup || monitor.JournalMaxRows <= 0)
            return;

        try
        {
            var removed = _database.TrimUnbound(monitor.JournalMaxRows);

            if (removed > 0)
                Trace.TraceInformation($"Journal cleanup removed {removed} unused domains.");
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Journal cleanup failed: {ex.Message}");
        }
    }

    public void Dispose() => Stop();
}
