using System.Diagnostics;
using System.IO;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using DyndDns.TrayApp.Views;

namespace DyndDns.TrayApp.ViewModels;

internal sealed class MainViewModel : IMonitoringWindowHost
{
    private const int SuccessNotificationMs = 2000;
    private const int ErrorNotificationMs = 5000;

    /// <summary>
    /// How long a read of a router's connections is reused. Long enough to answer the second asker of the same
    /// moment (the window and the tray open one together), short enough to notice a change on the router.
    /// </summary>
    private static readonly TimeSpan VpnInterfaceCacheLifetime = TimeSpan.FromSeconds(5);

    private readonly AppDatabase _database;
    private readonly SettingsStore _settings;
    private readonly SetupWizard _setupWizard;
    private readonly RouterApiPool _apiPool;
    private readonly SyncService _syncService;
    private readonly DnsMonitorService _dnsMonitor;
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip _contextMenu;
    private readonly System.Windows.Forms.ToolStripMenuItem _monitorMenuItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _routersMenu;

    private readonly Icon _defaultIcon;
    private readonly Icon _syncIcon;
    private readonly Icon _errorIcon;

    /// <summary>Icon of the current state; closing a balloon restores this one, not the default.</summary>
    private Icon _stateIcon;

    /// <summary>Number of the message being shown, so only its own timer closes the balloon.</summary>
    private int _notificationGeneration;

    /// <summary>
    /// Whether the synchronization that is running was asked for by the user. An explicit request is reported
    /// when it finishes; the runs the app starts on its own (at launch, after an edit) stay silent unless they
    /// fail. Every request rewrites it, so it always describes the last one.
    /// </summary>
    private bool _reportSyncOutcome;

    private readonly RouterStateReader _routerStateReader;

    /// <summary>Reads of a router's connections that are on their way, so one router is asked once at a time.</summary>
    private readonly Dictionary<int, Task<List<VpnInterfaceInfo>>> _vpnReads = [];

    /// <summary>What the last read of a router answered, with the moment it was read.</summary>
    private readonly Dictionary<int, (DateTime ReadAt, List<VpnInterfaceInfo> Interfaces)> _vpnInterfaces = [];

    private readonly Lock _vpnGate = new();

    private MonitoringWindow? _window;
    private AppConfig _config;

    public MainViewModel(string configDir)
    {
        _database = new AppDatabase(Path.Combine(configDir, AppDatabase.FileName));
        _database.Initialize();

        _settings = new SettingsStore(_database);

        _config = _settings.LoadConfig();
        _setupWizard = new SetupWizard(_settings);
        _apiPool = new RouterApiPool();

        _syncService = new SyncService(_settings, _apiPool);
        _syncService.SyncProgress += OnSyncProgress;
        _syncService.SyncCompleted += OnSyncCompleted;

        _routerStateReader = new RouterStateReader(_settings, ReadRouteGroupsAsync, () => _syncService.IsBusy);

        _dnsMonitor = new DnsMonitorService(_database, _config);

        _monitorMenuItem = new System.Windows.Forms.ToolStripMenuItem("Мониторинг DNS");
        _routersMenu = new System.Windows.Forms.ToolStripMenuItem("Роутеры");

        _defaultIcon = LoadIcon("DyndDns.TrayApp.app.ico");
        _syncIcon = LoadIcon("DyndDns.TrayApp.app.sync.ico");
        _errorIcon = LoadIcon("DyndDns.TrayApp.app.error.ico");
        _stateIcon = _defaultIcon;

        _contextMenu = new System.Windows.Forms.ContextMenuStrip();

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = AppInfo.Name,
            Icon = _defaultIcon,
            Visible = true
        };

        BuildContextMenu();
        _notifyIcon.MouseClick += OnNotifyIconClick;

        // A sync is also needed when a profile has no domains any more: the run removes its stale groups.
        if (_settings.GetRouters().Count > 0)
            RequestSync();

        RunFirstRunSetupIfNeeded();

        // Deferred so the tray icon and startup finish before the routers are contacted.
        var startupDispatcher = System.Windows.Application.Current?.Dispatcher;

        if (startupDispatcher is null)
        {
            RefreshAllVpnInterfaces();
            StartDnsMonitor(manual: false);
        }
        else
        {
            startupDispatcher.BeginInvoke(new Action(RefreshAllVpnInterfaces));
            startupDispatcher.BeginInvoke(new Action(() => StartDnsMonitor(manual: false)));
        }
    }

    private void BuildContextMenu()
    {
        _contextMenu.Items.Clear();

        var openWindowItem = new System.Windows.Forms.ToolStripMenuItem("Поиск и мониторинг...");
        openWindowItem.Click += (s, e) => OpenWindow();
        _contextMenu.Items.Add(openWindowItem);

        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        _monitorMenuItem.Click += (s, e) => ToggleDnsMonitor();
        _contextMenu.Items.Add(_monitorMenuItem);

        _contextMenu.Items.Add(_routersMenu);

        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var settingsItem = new System.Windows.Forms.ToolStripMenuItem("Настройки...");
        settingsItem.Click += (s, e) => OpenSettings();
        _contextMenu.Items.Add(settingsItem);

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Выход");
        exitItem.Click += (s, e) => Exit();
        _contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = _contextMenu;
    }

    private void OnNotifyIconClick(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != System.Windows.Forms.MouseButtons.Right)
            return;

        UpdateRouterMenu();
        _contextMenu.Show(System.Windows.Forms.Cursor.Position);
    }

    /// <summary>Rebuilt on every open so the check marks follow the stored profiles.</summary>
    private void UpdateRouterMenu()
    {
        var profiles = _settings.GetRouters();

        _routersMenu.DropDownItems.Clear();

        if (profiles.Count == 0)
        {
            _routersMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("Роутеров нет")
            {
                Enabled = false
            });
        }
        else
        {
            foreach (var profile in profiles)
                _routersMenu.DropDownItems.Add(BuildProfileMenu(profile));
        }

        _routersMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());

        var addItem = new System.Windows.Forms.ToolStripMenuItem("Добавить роутер...");
        addItem.Click += (s, e) => AddRouter();
        _routersMenu.DropDownItems.Add(addItem);
    }

    /// <summary>
    /// Actions of a single router. It carries no switch for synchronization: every profile is pushed, and a bind
    /// or an unbind starts the push on its own.
    /// </summary>
    private System.Windows.Forms.ToolStripMenuItem BuildProfileMenu(RouterProfile profile)
    {
        var menu = new System.Windows.Forms.ToolStripMenuItem(profile.DisplayName);

        var snapshot = profile;

        var configureItem = new System.Windows.Forms.ToolStripMenuItem("Настроить (адрес, логин, пароль)...");
        configureItem.Click += (s, e) => ConfigureRouter(snapshot);
        menu.DropDownItems.Add(configureItem);

        menu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());

        var deleteItem = new System.Windows.Forms.ToolStripMenuItem("Удалить роутер");
        deleteItem.Click += (s, e) => DeleteRouter(snapshot);
        menu.DropDownItems.Add(deleteItem);

        return menu;
    }

    private void AddRouter()
    {
        var saved = _setupWizard.Run(profileToUpdate: null);

        if (saved is null)
            return;

        AfterProfileSaved(saved, $"Роутер добавлен: {saved.DisplayName}");
    }

    /// <summary>
    /// What follows a saved profile: the user hears about it, the bindings are pushed to that router, its
    /// connections are read again, and an open window follows the new set of profiles.
    /// </summary>
    private void AfterProfileSaved(RouterProfile profile, string message)
    {
        ShowNotification(message, SuccessNotificationMs);

        // The credentials may have changed, so whatever was read from that router is dropped and read again.
        ForgetVpnInterfaces(profile.Id);

        RequestSync(profile.Id);
        RefreshVpnInterface(profile);
        _window?.ReloadData();
    }

    private void ConfigureRouter(RouterProfile profile)
    {
        var saved = _setupWizard.Run(profile);

        if (saved is null)
            return;

        // The credentials may have changed, so the pooled session of that router is dropped.
        _apiPool.Invalidate(saved.Id);

        AfterProfileSaved(saved, $"Роутер сохранён: {saved.DisplayName}");
    }

    private void DeleteRouter(RouterProfile profile)
    {
        var routes = _settings.GetRoutes(profile.Id);
        var removeGroups = false;

        if (routes.Count > 0)
        {
            var answer = Dialogs.AskOrCancel(
                owner: null,
                $"Удалить роутер «{profile.DisplayName}»?\n\n" +
                $"Снять группы DyndDns ({routes.Count} доменов) с этого роутера?");

            if (answer is null)
                return;

            removeGroups = answer.Value;
        }
        else if (!Dialogs.Ask(null, $"Удалить роутер «{profile.DisplayName}»?"))
        {
            return;
        }

        if (removeGroups)
            RemoveProfileGroups(profile);

        _apiPool.Invalidate(profile.Id);
        ForgetVpnInterfaces(profile.Id);
        _settings.DeleteRouter(profile.Id);
        _routerStateReader.Forget(profile.Id);

        ShowNotification($"Роутер «{profile.DisplayName}» удалён.", SuccessNotificationMs);

        _window?.ReloadData();
    }

    /// <summary>Drops every group the app manages from the router of the profile being deleted.</summary>
    private void RemoveProfileGroups(RouterProfile profile)
    {
        try
        {
            var api = _apiPool.Get(profile);
            Task.Run(() => api.RemoveStaleRoutingGroupsAsync([])).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Removing the groups of '{profile.DisplayName}' failed: {ex.Message}");
            Dialogs.Warn(null, $"Не удалось снять группы с {profile.DisplayName}: {ex.Message}");
        }
    }

    /// <summary>Reads the routing state of one router for <see cref="RouterStateReader"/>.</summary>
    private async Task<IReadOnlyList<RouterRouteGroup>> ReadRouteGroupsAsync(RouterProfile profile) =>
        await _apiPool.Get(profile).GetRouteGroupsAsync();

    /// <summary>
    /// Queues a synchronization and remembers whether the user asked for it, so the tray reports the outcome
    /// of an explicit request instead of staying silent about it.
    /// </summary>
    private void RequestSync(int? routerId = null, bool reportOutcome = false)
    {
        // Only an explicit request turns the report on, and it stays on until a run finishes: a request the app
        // makes on its own must not silence the outcome of the one the user asked for.
        if (reportOutcome)
            _reportSyncOutcome = true;

        _syncService.RequestSync(routerId);
    }

    private void OnSyncProgress(SyncNotification notification) =>
        ViewDispatch.Post(() => ApplySyncNotification(notification));

    /// <summary>
    /// The icon of the tray carries the state of a run. A synchronization is started from the router it concerns,
    /// and a run the user asked for reports what it pushed as well.
    /// </summary>
    private void ApplySyncNotification(SyncNotification notification)
    {
        switch (notification.Status)
        {
            case SyncStatus.Running:
                SetStateIcon(_syncIcon);
                break;

            case SyncStatus.Success:
                SetStateIcon(_defaultIcon);

                if (TakeOutcomeReport())
                    ShowNotification(notification.Message, SuccessNotificationMs);

                break;

            case SyncStatus.Error:
                SetStateIcon(_errorIcon);
                _reportSyncOutcome = false;
                ShowNotification(notification.Message, ErrorNotificationMs);
                break;
        }
    }

    /// <summary>True once for the run the user asked for, so its outcome is reported exactly once.</summary>
    private bool TakeOutcomeReport()
    {
        if (!_reportSyncOutcome)
            return false;

        _reportSyncOutcome = false;
        return true;
    }

    private void SetStateIcon(Icon icon)
    {
        _stateIcon = icon;
        _notifyIcon.Icon = icon;
    }

    private void ShowNotification(string message, int displayMilliseconds)
    {
        // ToolTipIcon.None keeps the shell from drawing its own info/warning/error glyph,
        // so the balloon uses the application icon instead.
        _notifyIcon.ShowBalloonTip(displayMilliseconds, AppInfo.Name, message, System.Windows.Forms.ToolTipIcon.None);

        // A message replaces the previous one, so the timer of an older message must not close the newer
        // balloon: only the timer belonging to the last message is allowed to.
        var generation = ++_notificationGeneration;

        _ = Task.Delay(displayMilliseconds).ContinueWith(
            _ => ViewDispatch.Post(() =>
            {
                if (generation == _notificationGeneration)
                    CloseNotification();
            }),
            TaskScheduler.Default);
    }

    private void CloseNotification()
    {
        try
        {
            // The shell ignores the balloon timeout on modern Windows, so close the balloon
            // ourselves by removing and re-adding the tray icon. The icon of the current state is put back:
            // a synchronization in progress or an error must not be masked by the default icon.
            _notifyIcon.Visible = false;
            _notifyIcon.Icon = _stateIcon;
            _notifyIcon.Visible = true;
        }
        catch (ObjectDisposedException)
        {
            // The tray icon is already gone; nothing left to close.
        }
    }

    /// <summary>
    /// Offers the setup wizard on first launch. Deferred through the dispatcher so startup settles
    /// before a modal dialog opens on top of the freshly created tray icon.
    /// </summary>
    private void RunFirstRunSetupIfNeeded()
    {
        if (_settings.GetRouters().Count > 0 || _config.SetupDismissed)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
            RunSetupWizard();
        else
            dispatcher.BeginInvoke(new Action(RunSetupWizard));
    }

    private void RunSetupWizard()
    {
        var saved = _setupWizard.Run(profileToUpdate: null);

        // The wizard may have remembered a cancellation or cleared it on success.
        _config = _settings.LoadConfig();

        if (saved is null)
            return;

        AfterProfileSaved(saved, $"Роутер настроен: {saved.DisplayName}");
    }

    /// <summary>
    /// Starts DNS collection. Called on startup (honouring the settings) and from the tray menu, where a manual
    /// start ignores the setting. A start that fails is about the missing rights, which is all the user can act
    /// on; the item carries the state either way.
    /// </summary>
    private void StartDnsMonitor(bool manual)
    {
        if (!_dnsMonitor.IsRunning && (manual || _config.Monitor.MonitorEnabled) && !_dnsMonitor.Start())
            ShowNotification("Мониторинг DNS не запущен: нужны права администратора.", ErrorNotificationMs);

        UpdateMonitorMenu();
    }

    private void ToggleDnsMonitor()
    {
        if (_dnsMonitor.IsRunning)
        {
            _dnsMonitor.Stop();
            UpdateMonitorMenu();
            ShowNotification("Мониторинг DNS: выключен", SuccessNotificationMs);
            return;
        }

        StartDnsMonitor(manual: true);

        // A manual start reports itself; a start that failed is reported by StartDnsMonitor instead.
        if (_dnsMonitor.IsRunning)
            ShowNotification("Мониторинг DNS: включён", SuccessNotificationMs);
    }

    /// <summary>The item carries the state, because the recording is toggled, not enabled or disabled.</summary>
    private void UpdateMonitorMenu() =>
        _monitorMenuItem.Text = _dnsMonitor.IsRunning ? "Мониторинг DNS: включён" : "Мониторинг DNS: выключен";

    private void OpenSettings()
    {
        using var dialog = new SettingsDialog(_config, _database.Path);

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var monitoringChanged = dialog.MonitorEnabled != _config.Monitor.MonitorEnabled ||
            dialog.BrowserHistoryEnabled != _config.Monitor.BrowserHistoryEnabled;

        dialog.ApplyTo(_config);
        _settings.SaveConfig(_config);

        // The registry entry is the source of truth for the startup setting, not the database.
        if (dialog.StartWithWindows != StartupRegistration.IsEnabled() && !StartupRegistration.Set(dialog.StartWithWindows))
        {
            ShowNotification("Настройки сохранены, но автозапуск изменить не удалось.", ErrorNotificationMs);
            return;
        }

        ShowNotification(
            monitoringChanged ? "Настройки мониторинга применятся после перезапуска приложения." : "Настройки сохранены.",
            SuccessNotificationMs);
    }

    private void OpenWindow()
    {
        if (_window is { IsDisposed: false })
        {
            _window.Activate();
            return;
        }

        _window = new MonitoringWindow(this);

        _window.FormClosed += (_, _) => _window = null;
        _window.Show();

        // The window works on what the routers have: their VPN connections are read again and everything stored
        // is pushed back, so the profiles the window shows are the profiles the routers hold.
        RefreshAllVpnInterfaces();
        RequestSync(reportOutcome: true);
    }

    /// <summary>Binds the domains to the profile; returns how many bindings were changed.</summary>
    private int Bind(int routerId, IReadOnlyList<string> domains, string interfaceName)
    {
        var changed = 0;

        foreach (var domain in domains)
        {
            var normalized = DomainNormalizer.Normalize(domain);

            if (normalized.Length > 0 && _settings.AddRoute(routerId, normalized, interfaceName))
                changed++;
        }

        if (changed > 0)
            RequestSync(routerId);

        return changed;
    }

    /// <summary>Drops the given bindings; returns how many rows were removed.</summary>
    private int Unbind(IReadOnlyList<BindingKey> bindings)
    {
        var removed = 0;

        foreach (var binding in bindings)
        {
            if (_settings.RemoveRoute(binding.RouterId, binding.Domain))
                removed++;
        }

        if (removed > 0)
        {
            foreach (var routerId in bindings.Select(binding => binding.RouterId).Distinct())
                RequestSync(routerId);
        }

        return removed;
    }

    private IReadOnlyList<VpnInterfaceInfo> GetVpnInterfaces(int routerId) =>
        ReadVpnInterfacesAsync(routerId).GetAwaiter().GetResult();

    /// <summary>
    /// Reads the connections of a router, one call at a time. The window and the tray ask about the same router
    /// within a moment of each other, and a second call while the first is unanswered would only double the
    /// traffic; the answer is also kept for a short while for the same reason. Both callers wait for one answer.
    /// </summary>
    private Task<List<VpnInterfaceInfo>> ReadVpnInterfacesAsync(int routerId)
    {
        Task<List<VpnInterfaceInfo>> read;

        lock (_vpnGate)
        {
            if (_vpnInterfaces.TryGetValue(routerId, out var cached) &&
                DateTime.UtcNow - cached.ReadAt < VpnInterfaceCacheLifetime)
            {
                return Task.FromResult(cached.Interfaces);
            }

            if (_vpnReads.TryGetValue(routerId, out var pending))
                return pending;

            var profile = _settings.GetRouter(routerId);

            if (profile is null)
                return Task.FromResult(new List<VpnInterfaceInfo>());

            read = Task.Run(() => _apiPool.Get(profile).GetVpnInterfacesAsync());
            _vpnReads[routerId] = read;
        }

        // Attached outside the lock on purpose: a continuation of a finished task runs on the calling thread,
        // and this one takes the same lock to store the answer.
        _ = read.ContinueWith(
            task =>
            {
                lock (_vpnGate)
                {
                    _vpnReads.Remove(routerId);

                    if (task.Status == TaskStatus.RanToCompletion)
                        _vpnInterfaces[routerId] = (DateTime.UtcNow, task.Result);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return read;
    }

    /// <summary>Drops what is known about a router: its connections and the read on their way, if any.</summary>
    private void ForgetVpnInterfaces(int routerId)
    {
        lock (_vpnGate)
        {
            _vpnInterfaces.Remove(routerId);
            _vpnReads.Remove(routerId);
        }
    }

    /// <summary>Re-reads the VPN connections of every profile and keeps the active one.</summary>
    private void RefreshAllVpnInterfaces()
    {
        foreach (var profile in _settings.GetRouters())
            RefreshVpnInterface(profile);
    }

    private void OnSyncCompleted(int routerId) => RefreshRouterStateAfterSync(routerId);

    /// <summary>
    /// Re-reads one router after its synchronization and repaints the window when it is open. The read talks
    /// to the router, so it runs on the thread pool; the window marshals the result to its own thread.
    /// </summary>
    private void RefreshRouterStateAfterSync(int routerId)
    {
        if (_window is not { IsDisposed: false })
            return;

        Task.Run(() =>
        {
            var profile = _settings.GetRouter(routerId);

            if (profile is { Address.Length: > 0 })
                _routerStateReader.Refresh(profile);

            return _routerStateReader.Snapshot();
        }).ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    Trace.TraceError($"Router state after synchronization failed: {task.Exception?.GetBaseException().Message}");
                    return;
                }

                _window?.ApplyRouterState(task.Result);
            },
            TaskScheduler.Default);
    }

    /// <summary>
    /// Reads the router's VPN connections and keeps the profile's VPN interface pointing at the active one: the
    /// configured connection is kept while it still exists, otherwise the connected one takes its place. The
    /// router call runs on the thread pool and the result is marshalled back through the dispatcher, so the UI
    /// thread is never blocked; a change is saved and re-synced.
    /// </summary>
    private void RefreshVpnInterface(RouterProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Address) || string.IsNullOrWhiteSpace(profile.Password))
            return;

        RefreshName(profile);

        _ = ReadVpnInterfacesAsync(profile.Id)
            .ContinueWith(task => ViewDispatch.Post(() => ApplyVpnInterfaces(profile, task)), TaskScheduler.Default);
    }

    /// <summary>
    /// Asks the router for the name it calls itself by, for a profile that has none of its own. A router added
    /// when nothing asked the device about it is stored under its address, and without this it would stay a bare
    /// IP in the tray menu, the filters and the table for good.
    /// </summary>
    private void RefreshName(RouterProfile profile)
    {
        if (HasOwnName(profile))
            return;

        _ = Task.Run(() => _apiPool.Get(profile).ReadDeviceNameAsync())
            .ContinueWith(task => ViewDispatch.Post(() => ApplyRouterName(profile, task)), TaskScheduler.Default);
    }

    private void ApplyRouterName(RouterProfile profile, Task<string> task)
    {
        if (task.IsFaulted)
        {
            Trace.TraceError($"Reading the name of '{profile.Address}' failed: {task.Exception}");
            return;
        }

        if (task.Result is not { Length: > 0 } name)
            return;

        // The profile may have been edited or deleted while the router was answering; only its name is written,
        // so an edit made in the meantime (address, credentials) is not undone by the copy that was read here.
        var stored = _settings.GetRouter(profile.Id);

        if (stored is null || HasOwnName(stored))
            return;

        _settings.SetRouterName(stored.Id, name);

        UpdateRouterMenu();
        _window?.ReloadData();
        ShowNotification($"Роутер: {stored.DisplayName}", SuccessNotificationMs);
    }

    /// <summary>True when the profile carries a name of its own, i.e. something other than its address.</summary>
    private static bool HasOwnName(RouterProfile profile) =>
        profile.Name.Length > 0 && !string.Equals(profile.Name, profile.Address, StringComparison.OrdinalIgnoreCase);

    private void ApplyVpnInterfaces(RouterProfile profile, Task<List<VpnInterfaceInfo>> task)
    {
        if (task.IsFaulted)
        {
            Trace.TraceError($"VPN interface lookup of '{profile.DisplayName}' failed: {task.Exception}");
            return;
        }

        var interfaces = task.Result;
        var active = VpnInterfaceResolver.PickPreferred(interfaces, profile.VpnInterface);
        var changed = false;

        if (active is not null && !string.Equals(profile.VpnInterface, active.Name, StringComparison.OrdinalIgnoreCase))
        {
            profile.VpnInterface = active.Name;

            // Only the connection is written: the profile this read started with must not overwrite an edit the
            // user made while the router was answering.
            _settings.SetRouterVpnInterface(profile.Id, active.Name);
            changed = true;
        }

        // A connection deleted on the router takes the bindings that route through it: they cannot be pushed
        // anywhere, and the group of that interface is removed from the router by the next synchronization.
        var dropped = _settings.RemoveBindingsOfMissingInterfaces(
            profile.Id,
            [.. interfaces.Select(info => info.Name)]);

        if (dropped > 0)
        {
            ShowNotification(
                $"{profile.DisplayName}: VPN-подключение удалено на роутере, снято привязок: {dropped}.",
                ErrorNotificationMs);
        }

        // A router without a VPN-capable connection is not worth a message of its own: the synchronization
        // reports that the profile cannot be pushed, and it does so when it actually matters.
        if (active is not null)
        {
            if (!active.IsUp)
                ShowNotification($"{profile.DisplayName}: VPN {active.Name} не подключён. Проверьте настройки.", ErrorNotificationMs);
            else if (changed)
                ShowNotification($"{profile.DisplayName}: VPN-интерфейс {active.Name}", SuccessNotificationMs);
        }

        if (changed || dropped > 0)
            RequestSync(profile.Id);
    }

    // --- IMonitoringWindowHost: what the monitoring window asks the application for ---

    IReadOnlyList<DomainStat> IMonitoringWindowHost.SearchDomains(string? term, int limit) =>
        _dnsMonitor.Search(term, limit);

    IReadOnlyList<RouterProfile> IMonitoringWindowHost.GetRouters() => _settings.GetRouters();

    IReadOnlyList<DomainBinding> IMonitoringWindowHost.GetBindings() => _settings.GetBindings();

    IReadOnlyList<VpnInterfaceInfo> IMonitoringWindowHost.GetVpnInterfaces(int routerId) => GetVpnInterfaces(routerId);

    IReadOnlyDictionary<BindingKey, string> IMonitoringWindowHost.GetRouterState() => _routerStateReader.Read();

    int IMonitoringWindowHost.Bind(int routerId, IReadOnlyList<string> domains, string interfaceName) =>
        Bind(routerId, domains, interfaceName);

    int IMonitoringWindowHost.Unbind(IReadOnlyList<BindingKey> bindings) => Unbind(bindings);

    int IMonitoringWindowHost.ClearUnboundDomains() => _dnsMonitor.ClearUnboundDomains();

    private void Exit()
    {
        _window?.Close();
        _dnsMonitor.Dispose();
        _syncService.Dispose();
        _apiPool.Dispose();

        // The tray owns the icon while it is alive; the loaded frames and the menu it showed are the view model's
        // to release, because nothing else does that when the process is about to end.
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _defaultIcon.Dispose();
        _syncIcon.Dispose();
        _errorIcon.Dispose();

        System.Windows.Application.Current?.Shutdown();
    }

    private static Icon LoadIcon(string resourceName)
    {
        using var stream = typeof(MainViewModel).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Icon resource '{resourceName}' is missing.");

        // Pick the frame that matches the tray size to keep the icon crisp.
        return new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

}
