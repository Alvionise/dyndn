using System.Diagnostics;
using System.IO;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Wires the DNS sources to the SQLite store: ETW hits are aggregated in memory and flushed in
/// batches, while the browser history is imported whenever a browser wrote to it. Both land in the
/// same table, so the search window sees service lookups and visited sites alike.
/// </summary>
internal sealed class DnsMonitorService : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HistoryInterval = TimeSpan.FromSeconds(30);

    private readonly DnsDatabase _database;
    private readonly DnsMonitor _monitor = new();
    private readonly BrowserHistoryReader _historyReader = new();
    private readonly bool _browserHistoryEnabled;
    private readonly Dictionary<string, DateTime> _historyStamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _historyLock = new();

    private System.Threading.Timer? _flushTimer;
    private System.Threading.Timer? _historyTimer;

    public DnsMonitorService(string databasePath, bool browserHistoryEnabled = true)
    {
        _database = new DnsDatabase(databasePath);
        _browserHistoryEnabled = browserHistoryEnabled;
    }

    public bool IsRunning => _monitor.IsRunning;

    /// <summary>Returns <c>false</c> when the database or the ETW session is unavailable.</summary>
    public bool Start()
    {
        try
        {
            _database.Initialize();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"DNS database unavailable: {ex.Message}");
            return false;
        }

        StartBrowserHistoryImport();

        if (!_monitor.Start())
            return false;

        _flushTimer ??= new System.Threading.Timer(_ => Flush(), null, FlushInterval, FlushInterval);
        return true;
    }

    /// <summary>
    /// Stops collecting. The database is created lazily as well, so searching works even when the
    /// monitor was never started.
    /// </summary>
    public void Stop()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;

        _historyTimer?.Dispose();
        _historyTimer = null;

        Flush();
        _monitor.Stop();
    }

    public IReadOnlyList<DomainStat> Search(string? term, int limit)
    {
        _database.Initialize();
        return _database.Search(term, limit);
    }

    /// <summary>
    /// Browsers resolve names with their own resolver, so their lookups never reach the ETW provider;
    /// their visit history is imported instead, once at startup and then on a timer.
    /// </summary>
    private void StartBrowserHistoryImport()
    {
        if (!_browserHistoryEnabled)
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

                _database.Initialize();
                _database.AddVisits(visits);
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

            _database.Initialize();
            _database.AddHits(hits);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"DNS flush failed: {ex.Message}");
        }
    }

    public void Dispose() => Stop();
}
