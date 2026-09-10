using System.Diagnostics;
using System.IO;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

public enum SyncStatus
{
    Running,
    Success,
    Error
}

public readonly record struct SyncNotification(SyncStatus Status, string Message);

/// <summary>
/// Serializes synchronization on a single background worker. Requests are coalesced:
/// while a sync is running, additional requests collapse into at most one follow-up run
/// that uses the latest domain list. This keeps the UI thread free and avoids overlapping
/// router calls.
/// </summary>
public sealed class SyncService : IDisposable
{
    private static readonly TimeSpan SelfWriteGracePeriod = TimeSpan.FromSeconds(2);

    private readonly ConfigService _configService;
    private readonly KeeneticApiService _apiService;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _watcherLock = new();

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private long _lastSelfWriteTicks;
    private volatile bool _isSyncing;

    public event Action<SyncNotification>? SyncProgress;

    /// <summary>Raised after a successful run so views can re-read the router's routing state.</summary>
    public event Action? SyncCompleted;

    /// <summary>
    /// True while a sync runs or another one is queued, i.e. local changes have not reached the router
    /// yet. Readers of the router state must not treat it as the truth in the meantime.
    /// </summary>
    public bool IsBusy => _isSyncing || _wake.CurrentCount > 0;

    public SyncService(ConfigService configService, KeeneticApiService apiService)
    {
        _configService = configService;
        _apiService = apiService;
        _ = Task.Run(RunWorkerAsync);
    }

    public void StartWatching()
    {
        if (!_configService.LoadConfig().Sync.AutoSync)
            return;

        var directory = Path.GetDirectoryName(_configService.DnsListPath) ?? Directory.GetCurrentDirectory();

        _watcher = new FileSystemWatcher(directory, "dns-list.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };
        _watcher.Changed += OnDnsListChanged;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Saves the DNS list while the watcher ignores the resulting file event, then syncs.
    /// Prevents the save that the app performed from triggering a second, redundant sync.
    /// </summary>
    public void PersistAndSync(Action persist)
    {
        Interlocked.Exchange(ref _lastSelfWriteTicks, DateTime.UtcNow.Ticks);
        persist();
        RequestSync();
    }

    public void RequestSync()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending; the worker will run again with the latest file contents.
        }
    }

    private async Task RunWorkerAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await _wake.WaitAsync(_shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await SyncAsync();
        }
    }

    /// <summary>
    /// Applies the tracked domains to the router. The classic list keeps its own group and interface
    /// exactly as before; the search-window routes are grouped per VPN interface into extra
    /// <c>dyndns-<Interface></c> groups, and groups that are no longer needed are removed.
    /// </summary>
    private async Task SyncAsync()
    {
        _isSyncing = true;

        try
        {
            var config = _configService.LoadConfig();
            var list = _configService.LoadDnsList();
            var routes = _configService.LoadRoutes();
            var routeGroups = DnsRouting.GroupByInterface(routes, config.VpnInterface);

            Notify(SyncStatus.Running, $"Синхронизация: {list.Domains.Count + routes.Count} доменов");

            await _apiService.SyncGroupAsync(list, config.VpnInterface);

            foreach (var group in routeGroups)
            {
                var dnsGroup = new DnsGroup
                {
                    Name = group.GroupName,
                    Description = $"DyndDns: {group.Interface}",
                    Domains = group.Domains
                };

                await _apiService.SyncGroupAsync(dnsGroup, group.Interface);
            }

            await _apiService.RemoveStaleRoutingGroupsAsync(routeGroups.Select(group => group.GroupName).ToList());

            // Cleared before the notification so the views that react to it read a settled state.
            _isSyncing = false;

            Notify(SyncStatus.Success, $"Синхронизировано: {list.Domains.Count + routes.Count} доменов");
            SyncCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            _isSyncing = false;

            Trace.TraceError($"Sync failed: {ex}");
            Notify(SyncStatus.Error, $"Ошибка: {ex.Message}");
        }
    }

    private void OnDnsListChanged(object sender, FileSystemEventArgs e)
    {
        var lastSelfWrite = Interlocked.Read(ref _lastSelfWriteTicks);
        if (lastSelfWrite != 0 && DateTime.UtcNow.Ticks - lastSelfWrite < SelfWriteGracePeriod.Ticks)
            return;

        CancellationToken token;
        lock (_watcherLock)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            token = _debounceCts.Token;
        }

        _ = Task.Delay(400, token).ContinueWith(
            task =>
            {
                if (!task.IsCanceled)
                    RequestSync();
            },
            TaskScheduler.Default);
    }

    private void Notify(SyncStatus status, string message)
    {
        try
        {
            SyncProgress?.Invoke(new SyncNotification(status, message));
        }
        catch (Exception ex)
        {
            // Progress delivery is best-effort: the UI may already be shutting down.
            Trace.TraceError($"Sync progress delivery failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();

        _watcher?.Dispose();
        _watcher = null;

        lock (_watcherLock)
        {
            _debounceCts?.Cancel();
            _debounceCts = null;
        }
    }
}
