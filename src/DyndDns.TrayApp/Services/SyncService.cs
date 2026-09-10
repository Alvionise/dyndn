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
    private readonly object _pendingLock = new();
    private readonly object _watcherLock = new();

    private DnsGroup? _pendingGroup;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private long _lastSelfWriteTicks;

    public event Action<SyncNotification>? SyncProgress;

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
    public void PersistAndSync(Action persist, DnsGroup group)
    {
        Interlocked.Exchange(ref _lastSelfWriteTicks, DateTime.UtcNow.Ticks);
        persist();
        RequestSync(group);
    }

    public void RequestSync(DnsGroup? group = null)
    {
        lock (_pendingLock)
        {
            _pendingGroup = group ?? _pendingGroup;
        }

        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending; the worker will pick up the latest group.
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

            DnsGroup? group;
            lock (_pendingLock)
            {
                group = _pendingGroup;
            }

            await SyncAsync(group);
        }
    }

    private async Task SyncAsync(DnsGroup? group)
    {
        try
        {
            group ??= _configService.LoadDnsList();

            Notify(SyncStatus.Running, $"Синхронизация: {group.Domains.Count} доменов");

            var config = _configService.LoadConfig();
            await _apiService.SyncGroupAsync(group, config.VpnInterface);

            group.IsSynced = true;
            Notify(SyncStatus.Success, $"Синхронизировано: {group.Domains.Count} доменов");
        }
        catch (Exception ex)
        {
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
