using System.Diagnostics;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

internal enum SyncStatus
{
    Running,
    Success,
    Error
}

internal readonly record struct SyncNotification(SyncStatus Status, string Message);

/// <summary>
/// Serializes synchronization on a single background worker. Requests are coalesced and handled per
/// router profile: the domains of a profile are pushed to its own router through its own session, and a
/// router that fails (unreachable, wrong credentials) does not stop the others.
/// </summary>
internal sealed class SyncService : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly RouterApiPool _apiPool;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _queueLock = new();
    private readonly HashSet<int> _queuedRouters = [];
    private readonly Task _worker;

    private bool _queueAll;
    private volatile bool _isSyncing;

    public event Action<SyncNotification>? SyncProgress;

    /// <summary>Raised after a profile was processed, so views can re-read that router's state.</summary>
    public event Action<int>? SyncCompleted;

    /// <summary>
    /// True while a run is in progress or another one is queued, i.e. local changes have not reached the
    /// routers yet. Readers of the router state must not treat it as the truth in the meantime.
    /// </summary>
    public bool IsBusy => _isSyncing || _wake.CurrentCount > 0;

    public SyncService(SettingsStore settings, RouterApiPool apiPool)
    {
        _settings = settings;
        _apiPool = apiPool;
        _worker = Task.Run(RunWorkerAsync);
    }

    /// <summary>Queues one profile, or every profile when <paramref name="routerId"/> is null.</summary>
    public void RequestSync(int? routerId = null)
    {
        lock (_queueLock)
        {
            if (routerId is { } id)
                _queuedRouters.Add(id);
            else
                _queueAll = true;
        }

        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending; the worker picks up the latest queue.
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

            try
            {
                await SyncAsync();
            }
            catch (Exception ex)
            {
                // A round that fails outside the per-router handling must not take the worker down:
                // requests would keep being accepted and never handled, with nothing said to the user.
                Trace.TraceError($"Synchronization round failed: {ex}");
                Notify(SyncStatus.Error, $"Не удалось выполнить синхронизацию: {ex.Message}");
            }
        }
    }

    private async Task SyncAsync()
    {
        List<int?> targets;

        lock (_queueLock)
        {
            targets = _queueAll
                ? [null]
                : [.. _queuedRouters.Select(id => (int?)id)];

            _queueAll = false;
            _queuedRouters.Clear();
        }

        _isSyncing = true;

        try
        {
            foreach (var target in targets)
                await SyncTargetAsync(target);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncTargetAsync(int? routerId)
    {
        var profiles = routerId is { } id
            ? [.. _settings.GetRouters().Where(profile => profile.Id == id)]
            : _settings.GetRouters();

        // Nothing to push and nothing to tell: the window shows its own empty state, and an error balloon for
        // "there is no router yet" would greet the user every time it is opened on a fresh installation.
        if (profiles.Count == 0)
            return;

        foreach (var profile in profiles)
            await SyncProfileAsync(profile);
    }

    /// <summary>
    /// Pushes the domains bound to one profile: per VPN interface a <c>dyndns-<Interface></c> group
    /// with a matching dns-proxy route, then the removal of the groups that are no longer wanted.
    /// </summary>
    private async Task SyncProfileAsync(RouterProfile profile)
    {
        try
        {
            var routes = _settings.GetRoutes(profile.Id);

            if (routes.Count > 0 && string.IsNullOrWhiteSpace(profile.VpnInterface))
            {
                // Without an interface every domain would be dropped and the router's groups wiped.
                Notify(SyncStatus.Error, $"{profile.DisplayName}: не выбран VPN-интерфейс, синхронизация пропущена");
                return;
            }

            var groups = DnsRouting.GroupByInterface(routes, profile.VpnInterface);
            var api = _apiPool.Get(profile);

            Notify(SyncStatus.Running, $"{profile.DisplayName}: синхронизация, {routes.Count} доменов");

            foreach (var (Interface, GroupName, Domains) in groups)
            {
                var dnsGroup = new DnsGroup
                {
                    Name = GroupName,
                    Description = $"DyndDns: {Interface}",
                    Domains = Domains
                };

                await api.SyncGroupAsync(dnsGroup, Interface);
            }

            await api.RemoveStaleRoutingGroupsAsync([.. groups.Select(group => group.GroupName)]);

            Notify(SyncStatus.Success, $"{profile.DisplayName}: синхронизировано, {routes.Count} доменов");
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Sync of '{profile.DisplayName}' failed: {ex}");
            Notify(SyncStatus.Error, $"{profile.DisplayName}: {ex.Message}");
        }
        finally
        {
            SyncCompleted?.Invoke(profile.Id);
        }
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

        // The worker may still be parked on the semaphore, so the handles are released only after it has
        // left its loop; destroying them under it would end up as an ObjectDisposedException in a task
        // nobody observes.
        _ = _worker.ContinueWith(
            _ =>
            {
                _wake.Dispose();
                _shutdown.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
