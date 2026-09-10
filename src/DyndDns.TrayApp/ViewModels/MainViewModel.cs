using System.ComponentModel;
using System.Drawing;
using System.IO;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using DyndDns.TrayApp.Triggers;
using DyndDns.TrayApp.Views;

namespace DyndDns.TrayApp.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private const int SuccessNotificationMs = 2000;
    private const int ErrorNotificationMs = 5000;

    private readonly ConfigService _configService;
    private readonly SetupWizard _setupWizard;
    private readonly AppConfig _config;
    private readonly KeeneticApiService _apiService;
    private readonly SyncService _syncService;
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip _contextMenu;
    private readonly System.Windows.Forms.ToolStripMenuItem _syncMenuItem;
    private readonly System.Windows.Forms.Keys _shortcutKeys;
    private readonly HotkeyManager? _hotkeyManager;
    private readonly DnsMonitorService _dnsMonitor;
    private readonly System.Windows.Forms.ToolStripMenuItem _monitorMenuItem;

    private DnsSearchWindow? _searchWindow;

    private readonly Icon _defaultIcon;
    private readonly Icon _syncIcon;
    private readonly Icon _errorIcon;

    private DnsGroup? _dnsList;
    private List<DnsRoute> _routes = new();

    public MainViewModel(string configDir)
    {
        _configService = new ConfigService(configDir);
        _configService.EnsureConfigExists();

        _config = _configService.LoadConfig();
        _setupWizard = new SetupWizard(_configService);
        _apiService = new KeeneticApiService(_config.Router);
        _syncService = new SyncService(_configService, _apiService);
        _syncService.SyncProgress += OnSyncProgress;
        _syncService.SyncCompleted += OnSyncCompleted;

        _dnsList = _configService.LoadDnsList();
        _routes = _configService.LoadRoutes();

        var databasePath = Path.IsPathRooted(_config.Sqlite.DatabaseFile)
            ? _config.Sqlite.DatabaseFile
            : Path.Combine(configDir, _config.Sqlite.DatabaseFile);

        _dnsMonitor = new DnsMonitorService(databasePath, _config.Sqlite.BrowserHistoryEnabled);
        _monitorMenuItem = new System.Windows.Forms.ToolStripMenuItem("Мониторинг DNS");

        _defaultIcon = LoadIcon("DyndDns.TrayApp.app.ico");
        _syncIcon = LoadIcon("DyndDns.TrayApp.app.sync.ico");
        _errorIcon = LoadIcon("DyndDns.TrayApp.app.error.ico");

        _shortcutKeys = ComputeShortcut(_config.Hotkey);

        _contextMenu = new System.Windows.Forms.ContextMenuStrip();
        _syncMenuItem = new System.Windows.Forms.ToolStripMenuItem("Синхронизировать");

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "DyndDns",
            Icon = _defaultIcon,
            Visible = true
        };

        BuildContextMenu();
        _notifyIcon.MouseClick += OnNotifyIconClick;

        if (_config.Hotkey.Enabled)
        {
            _hotkeyManager = new HotkeyManager(
                // Marshal through the dispatcher so the modal dialog is never opened
                // from inside the native hotkey hook.
                () => Dispatch(PromptAddDomain),
                _config.Hotkey.Key,
                _config.Hotkey.Modifiers);
            _hotkeyManager.Register();
        }

        _syncService.StartWatching();

        if ((_dnsList?.Domains.Count ?? 0) > 0 || _routes.Count > 0)
            _syncService.RequestSync();

        RunFirstRunSetupIfNeeded();

        // Deferred so the tray icon and startup finish before the router is contacted.
        var startupDispatcher = System.Windows.Application.Current?.Dispatcher;

        if (startupDispatcher is null)
        {
            RefreshVpnInterface(interactive: false);
            StartDnsMonitor(manual: false);
        }
        else
        {
            startupDispatcher.BeginInvoke(new Action(() => RefreshVpnInterface(interactive: false)));
            startupDispatcher.BeginInvoke(new Action(() => StartDnsMonitor(manual: false)));
        }
    }

    /// <summary>Tracked domains together with the interface each one is routed through.</summary>
    public IReadOnlyList<DnsRoute> Routes => _routes;

    private void BuildContextMenu()
    {
        _contextMenu.Items.Clear();

        var openListItem = new System.Windows.Forms.ToolStripMenuItem("Открыть список");
        openListItem.Click += (s, e) => OpenDnsList();
        _contextMenu.Items.Add(openListItem);

        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var addDomainItem = new System.Windows.Forms.ToolStripMenuItem("Добавить домен...");
        addDomainItem.ShortcutKeys = _shortcutKeys;
        addDomainItem.Click += (s, e) => PromptAddDomain();
        _contextMenu.Items.Add(addDomainItem);

        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        _syncMenuItem.Click += (s, e) => _syncService.RequestSync();
        _contextMenu.Items.Add(_syncMenuItem);

        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        _contextMenu.Items.Add(BuildRoutingMenu());

        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var settingsItem = new System.Windows.Forms.ToolStripMenuItem("Настройки");
        settingsItem.Click += (s, e) => OpenConfig();
        _contextMenu.Items.Add(settingsItem);

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Выход");
        exitItem.Click += (s, e) => Exit();
        _contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = _contextMenu;
    }

    /// <summary>
    /// Everything the app offers on top of the classic domain list: searching the recorded domains,
    /// the DNS monitor, and the router/VPN maintenance actions.
    /// </summary>
    private System.Windows.Forms.ToolStripMenuItem BuildRoutingMenu()
    {
        var menu = new System.Windows.Forms.ToolStripMenuItem("Мониторинг и маршруты");

        var searchItem = new System.Windows.Forms.ToolStripMenuItem("Поиск доменов...");
        searchItem.Click += (s, e) => OpenSearchWindow();
        menu.DropDownItems.Add(searchItem);

        _monitorMenuItem.Click += (s, e) => ToggleDnsMonitor();
        menu.DropDownItems.Add(_monitorMenuItem);

        menu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());

        var refreshVpnItem = new System.Windows.Forms.ToolStripMenuItem("Обновить VPN");
        refreshVpnItem.Click += (s, e) => RefreshVpnInterface(interactive: true);
        menu.DropDownItems.Add(refreshVpnItem);

        var setupItem = new System.Windows.Forms.ToolStripMenuItem("Настройка роутера");
        setupItem.Click += (s, e) => RunSetupWizard();
        menu.DropDownItems.Add(setupItem);

        return menu;
    }

    private static System.Windows.Forms.Keys ComputeShortcut(HotkeyConfig hotkey)
    {
        var result = System.Windows.Forms.Keys.None;

        foreach (var modifier in hotkey.Modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (modifier.Trim())
            {
                case "Control": result |= System.Windows.Forms.Keys.Control; break;
                case "Shift": result |= System.Windows.Forms.Keys.Shift; break;
                case "Alt": result |= System.Windows.Forms.Keys.Alt; break;
                case "Win": result |= System.Windows.Forms.Keys.LWin; break;
            }
        }

        return result | (hotkey.Key switch
        {
            "A" => System.Windows.Forms.Keys.A,
            "B" => System.Windows.Forms.Keys.B,
            "C" => System.Windows.Forms.Keys.C,
            "D" => System.Windows.Forms.Keys.D,
            "E" => System.Windows.Forms.Keys.E,
            "F" => System.Windows.Forms.Keys.F,
            "G" => System.Windows.Forms.Keys.G,
            "H" => System.Windows.Forms.Keys.H,
            "I" => System.Windows.Forms.Keys.I,
            "J" => System.Windows.Forms.Keys.J,
            "K" => System.Windows.Forms.Keys.K,
            "L" => System.Windows.Forms.Keys.L,
            "M" => System.Windows.Forms.Keys.M,
            "N" => System.Windows.Forms.Keys.N,
            "O" => System.Windows.Forms.Keys.O,
            "P" => System.Windows.Forms.Keys.P,
            "Q" => System.Windows.Forms.Keys.Q,
            "R" => System.Windows.Forms.Keys.R,
            "S" => System.Windows.Forms.Keys.S,
            "T" => System.Windows.Forms.Keys.T,
            "U" => System.Windows.Forms.Keys.U,
            "V" => System.Windows.Forms.Keys.V,
            "W" => System.Windows.Forms.Keys.W,
            "X" => System.Windows.Forms.Keys.X,
            "Y" => System.Windows.Forms.Keys.Y,
            "Z" => System.Windows.Forms.Keys.Z,
            "0" => System.Windows.Forms.Keys.D0,
            "1" => System.Windows.Forms.Keys.D1,
            "2" => System.Windows.Forms.Keys.D2,
            "3" => System.Windows.Forms.Keys.D3,
            "4" => System.Windows.Forms.Keys.D4,
            "5" => System.Windows.Forms.Keys.D5,
            "6" => System.Windows.Forms.Keys.D6,
            "7" => System.Windows.Forms.Keys.D7,
            "8" => System.Windows.Forms.Keys.D8,
            "9" => System.Windows.Forms.Keys.D9,
            _ => System.Windows.Forms.Keys.V
        });
    }

    private void OnNotifyIconClick(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != System.Windows.Forms.MouseButtons.Right)
            return;

        UpdateContextMenu();
        _contextMenu.Show(System.Windows.Forms.Cursor.Position);
    }

    private void UpdateContextMenu()
    {
        var insertIndex = _contextMenu.Items.IndexOf(_syncMenuItem);

        for (var i = _contextMenu.Items.Count - 1; i >= 0; i--)
        {
            if (_contextMenu.Items[i] is System.Windows.Forms.ToolStripMenuItem { Tag: not null } item)
                _contextMenu.Items.Remove(item);
        }

        if (_dnsList is not { Domains.Count: > 0 })
            return;

        var domainsMenu = new System.Windows.Forms.ToolStripMenuItem($"Домены ({_dnsList.Domains.Count})")
        {
            Tag = "domains"
        };

        foreach (var domain in _dnsList.Domains)
        {
            var domainItem = new System.Windows.Forms.ToolStripMenuItem($"  {domain}")
            {
                Tag = domain,
                DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text
            };
            domainItem.Click += (s, e) => RemoveDomain(domain);
            domainsMenu.DropDownItems.Add(domainItem);
        }

        var addSubItem = new System.Windows.Forms.ToolStripMenuItem("  + Добавить...");
        addSubItem.Click += (s, e) => PromptAddDomain();
        domainsMenu.DropDownItems.Add(addSubItem);

        var removeItem = new System.Windows.Forms.ToolStripMenuItem("  - Удалить всё");
        removeItem.Click += (s, e) => RemoveAllDomains();
        domainsMenu.DropDownItems.Add(removeItem);

        _contextMenu.Items.Insert(insertIndex, domainsMenu);
    }

    private void OnSyncProgress(SyncNotification notification) =>
        Dispatch(() => ApplySyncNotification(notification));

    private void ApplySyncNotification(SyncNotification notification)
    {
        switch (notification.Status)
        {
            case SyncStatus.Running:
                _notifyIcon.Icon = _syncIcon;
                _syncMenuItem.Text = "Синхронизация...";
                break;

            case SyncStatus.Success:
                _notifyIcon.Icon = _defaultIcon;
                _syncMenuItem.Text = "Синхронизировано";
                ShowNotification(notification.Message, SuccessNotificationMs);
                break;

            case SyncStatus.Error:
                _notifyIcon.Icon = _errorIcon;
                _syncMenuItem.Text = "Ошибка синхронизации";
                ShowNotification(notification.Message, ErrorNotificationMs);
                break;
        }
    }

    private void ShowNotification(string message, int displayMilliseconds)
    {
        // ToolTipIcon.None keeps the shell from drawing its own info/warning/error glyph,
        // so the balloon uses the application icon instead.
        _notifyIcon.ShowBalloonTip(displayMilliseconds, "DyndDns", message, System.Windows.Forms.ToolTipIcon.None);

        _ = Task.Delay(displayMilliseconds).ContinueWith(_ => Dispatch(CloseNotification));
    }

    private void CloseNotification()
    {
        try
        {
            // The shell ignores the balloon timeout on modern Windows, so close the balloon
            // ourselves by removing and re-adding the tray icon.
            _notifyIcon.Visible = false;
            _notifyIcon.Icon = _defaultIcon;
            _notifyIcon.Visible = true;
        }
        catch (ObjectDisposedException)
        {
            // The tray icon is already gone; nothing left to close.
        }
    }

    private void PromptAddDomain()
    {
        var input = ShowAddDomainDialog();
        if (string.IsNullOrWhiteSpace(input))
            return;

        var domain = DomainNormalizer.Normalize(input);
        if (domain.Length > 0)
            AddDomain(domain);
    }

    private static string? ShowAddDomainDialog()
    {
        using var form = new System.Windows.Forms.Form
        {
            Text = "DyndDns — Добавить домен",
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
            ClientSize = new Size(360, 128),
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            TopMost = true
        };

        var label = new System.Windows.Forms.Label
        {
            Text = "Домен:",
            Location = new Point(15, 18),
            AutoSize = true
        };

        var textBox = new System.Windows.Forms.TextBox
        {
            Location = new Point(15, 43),
            Width = 330,
            Text = "example.com",
            Font = new Font("Segoe UI", 10F)
        };
        textBox.SelectAll();

        var okButton = new System.Windows.Forms.Button
        {
            Text = "Добавить",
            DialogResult = System.Windows.Forms.DialogResult.OK,
            Location = new Point(165, 83),
            Width = 85
        };

        var cancelButton = new System.Windows.Forms.Button
        {
            Text = "Отмена",
            DialogResult = System.Windows.Forms.DialogResult.Cancel,
            Location = new Point(260, 83),
            Width = 85
        };

        form.Controls.AddRange(new System.Windows.Forms.Control[] { label, textBox, okButton, cancelButton });
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        form.FormClosing += (s, e) =>
        {
            if (form.DialogResult == System.Windows.Forms.DialogResult.OK &&
                string.IsNullOrWhiteSpace(textBox.Text))
            {
                e.Cancel = true;
                textBox.Focus();
                textBox.SelectAll();
            }
        };

        return form.ShowDialog() == System.Windows.Forms.DialogResult.OK ? textBox.Text.Trim() : null;
    }

    private void AddDomain(string domain)
    {
        if (_dnsList is null || _dnsList.Domains.Contains(domain))
            return;

        _dnsList.Domains.Add(domain);
        PersistAndSync();
    }

    private void RemoveDomain(string domain)
    {
        if (_dnsList is null || !_dnsList.Domains.Remove(domain))
            return;

        PersistAndSync();
    }

    private void RemoveAllDomains()
    {
        if (_dnsList is null || _dnsList.Domains.Count == 0)
            return;

        var result = System.Windows.Forms.MessageBox.Show(
            $"Удалить все {_dnsList.Domains.Count} доменов?",
            "DyndDns",
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Question);

        if (result != System.Windows.Forms.DialogResult.Yes)
            return;

        _dnsList.Domains.Clear();
        PersistAndSync();
    }

    private void PersistAndSync()
    {
        if (_dnsList is null)
            return;

        _syncService.PersistAndSync(() => _configService.SaveDnsList(_dnsList));
    }

    /// <summary>
    /// Adds a domain picked in the search window, bound to a specific VPN interface. These live in
    /// their own file so the classic list keeps its format and its group on the router.
    /// </summary>
    private bool AddRoute(string domain, string interfaceName)
    {
        if (_routes.Any(route => string.Equals(route.Domain, domain, StringComparison.OrdinalIgnoreCase)))
            return false;

        _routes.Add(new DnsRoute(domain, interfaceName));
        _syncService.PersistAndSync(() => _configService.SaveRoutes(_routes));
        return true;
    }

    /// <summary>
    /// Drops a route picked in the search window. The domain leaves the local file and the following
    /// sync rewrites the router groups without it, so the removal reaches the router as well.
    /// </summary>
    private bool RemoveRoute(string domain)
    {
        var existing = _routes.FirstOrDefault(route =>
            string.Equals(route.Domain, domain, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
            return false;

        _routes.Remove(existing);
        _syncService.PersistAndSync(() => _configService.SaveRoutes(_routes));
        return true;
    }

    private void OpenDnsList()
    {
        if (_dnsList is null || string.IsNullOrEmpty(_dnsList.DnsListFile))
            return;

        if (File.Exists(_dnsList.DnsListFile))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_dnsList.DnsListFile)
            {
                UseShellExecute = true
            });
        }
        else
        {
            _notifyIcon.ShowBalloonTip(3000, "DyndDns", "Файл списка не найден", System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    private void OpenConfig()
    {
        var path = _configService.ConfigPath;
        if (!File.Exists(path))
            return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
        {
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Offers the setup wizard on first launch. Deferred through the dispatcher so startup settles
    /// before a modal dialog opens on top of the freshly created tray icon.
    /// </summary>
    private void RunFirstRunSetupIfNeeded()
    {
        if (!ConfigService.SetupRequired(_config))
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
            RunSetupWizard();
        else
            dispatcher.BeginInvoke(new Action(RunSetupWizard));
    }

    /// <summary>
    /// Starts DNS collection. Called on startup (honouring the config flag) and from the tray menu,
    /// where a manual start ignores the flag.
    /// </summary>
    private void StartDnsMonitor(bool manual)
    {
        if (_dnsMonitor.IsRunning)
        {
            UpdateMonitorMenu();
            return;
        }

        if (!manual && !_config.Sqlite.MonitorEnabled)
        {
            UpdateMonitorMenu();
            return;
        }

        if (!_dnsMonitor.Start())
        {
            ShowNotification("Мониторинг DNS не запущен: нужны права администратора и настроенная база Sqlite.", ErrorNotificationMs);
            UpdateMonitorMenu();
            return;
        }

        UpdateMonitorMenu();
    }

    private void ToggleDnsMonitor()
    {
        if (_dnsMonitor.IsRunning)
            _dnsMonitor.Stop();
        else
            StartDnsMonitor(manual: true);

        UpdateMonitorMenu();
    }

    private void UpdateMonitorMenu() =>
        _monitorMenuItem.Text = _dnsMonitor.IsRunning ? "Мониторинг DNS: вкл" : "Мониторинг DNS: выкл";

    private void OpenSearchWindow()
    {
        if (_searchWindow is { IsDisposed: false })
        {
            _searchWindow.Activate();
            return;
        }

        _searchWindow = new DnsSearchWindow(
            (term, limit) => _dnsMonitor.Search(term, limit),
            () => Task.Run(() => _apiService.GetVpnInterfacesAsync()).GetAwaiter().GetResult(),
            () => _routes.ToList(),
            ReadRouterRoutes,
            AddRoute,
            RemoveRoute);

        _searchWindow.FormClosed += (_, _) => _searchWindow = null;
        _searchWindow.Show();
    }

    /// <summary>
    /// Reads the routing state from the router. The router is the source of truth, so every read also
    /// aligns the locally stored bindings with it.
    /// </summary>
    private IReadOnlyDictionary<string, string> ReadRouterRoutes()
    {
        var groups = Task.Run(() => _apiService.GetRouteGroupsAsync()).GetAwaiter().GetResult();

        // While a change of our own is still on its way to the router, its state is only displayed:
        // reconciling now would bring just removed routes back from the not-yet-updated router.
        if (!_syncService.IsBusy)
            ReconcileRoutes(DnsRouting.FromRouterGroups(groups));

        var routed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            foreach (var domain in group.Domains)
                routed[domain] = group.Interface;
        }

        return routed;
    }

    /// <summary>Adopts the bindings reported by the router; local-only entries are left untouched.</summary>
    private void ReconcileRoutes(IReadOnlyList<DnsRoute> routerRoutes)
    {
        if (routerRoutes.Count == 0)
            return;

        var merged = DnsRouting.MergeRouterRoutes(_routes, routerRoutes);

        if (merged.SequenceEqual(_routes))
            return;

        _routes = merged;
        _configService.SaveRoutes(_routes);
        System.Diagnostics.Trace.TraceInformation($"Routes reconciled from the router: {_routes.Count} entries");
    }

    private void OnSyncCompleted() => Dispatch(RefreshRoutingState);

    /// <summary>
    /// Re-reads the routing state after a synchronization and repaints the search window when it is
    /// open. Runs on the UI thread because it touches the tracked routes and the window.
    /// </summary>
    private void RefreshRoutingState()
    {
        IReadOnlyDictionary<string, string> routed;

        try
        {
            routed = ReadRouterRoutes();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Router routing state refresh failed: {ex.Message}");
            return;
        }

        _searchWindow?.ApplyRouterState(routed);
    }

    private void RunSetupWizard()
    {
        if (!_setupWizard.Run(_config))
            return;

        // Credentials changed: force the next router call to authenticate with the new values.
        _apiService.InvalidateAuthentication();

        RefreshVpnInterface(interactive: false);
    }

    /// <summary>
    /// Reads the router's VPN connections and keeps <see cref="AppConfig.VpnInterface"/> pointing at
    /// the active one. The router call runs on the thread pool and the result is marshalled back
    /// through the dispatcher, so the UI thread is never blocked.
    ///
    /// When <paramref name="interactive"/> is set (the tray menu action) and several connections
    /// exist, the user picks one. Otherwise the configured connection is kept while it is still
    /// present, falling back to the active one. A change is saved and re-synced.
    /// </summary>
    private void RefreshVpnInterface(bool interactive)
    {
        if (string.IsNullOrWhiteSpace(_config.Router.Address) || string.IsNullOrWhiteSpace(_config.Router.Password))
            return;

        _ = Task.Run(() => _apiService.GetVpnInterfacesAsync())
            .ContinueWith(task => Dispatch(() => ApplyVpnInterfaces(task, interactive)), TaskScheduler.Default);
    }

    private void ApplyVpnInterfaces(Task<List<VpnInterfaceInfo>> task, bool interactive)
    {
        if (task.IsFaulted)
        {
            System.Diagnostics.Trace.TraceError($"VPN interface lookup failed: {task.Exception}");
            return;
        }

        var interfaces = task.Result;

        if (interfaces.Count == 0)
        {
            ShowNotification("VPN-подключение не найдено. Создайте его на роутере и выберите «Обновить VPN».", ErrorNotificationMs);
            return;
        }

        var active = interfaces.Count == 1
            ? interfaces[0]
            : interactive
                ? PromptVpnInterfaceSelection(interfaces)
                : VpnInterfaceResolver.PickPreferred(interfaces, _config.VpnInterface);

        if (active is null)
            return;

        var changed = !string.Equals(_config.VpnInterface, active.Name, StringComparison.OrdinalIgnoreCase);

        if (changed)
        {
            _config.VpnInterface = active.Name;
            _configService.SaveConfig(_config);
        }

        if (!active.IsUp)
            ShowNotification($"VPN {active.Name} не подключён. Проверьте настройки подключения.", ErrorNotificationMs);
        else if (interactive)
            ShowNotification($"VPN-интерфейс: {active.Name}", SuccessNotificationMs);

        if (changed && _routes.Count > 0)
            _syncService.RequestSync();
    }

    private VpnInterfaceInfo? PromptVpnInterfaceSelection(IReadOnlyList<VpnInterfaceInfo> interfaces)
    {
        using var dialog = new VpnInterfaceDialog(interfaces, _config.VpnInterface);

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedInterface
            : null;
    }

    private void Exit()
    {
        _hotkeyManager?.Dispose();
        _searchWindow?.Close();
        _dnsMonitor.Dispose();
        _syncService.Dispose();
        _apiService.Dispose();
        _notifyIcon.Dispose();
        System.Windows.Application.Current?.Shutdown();
    }

    private static Icon LoadIcon(string resourceName)
    {
        using var stream = typeof(MainViewModel).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Icon resource '{resourceName}' is missing.");

        // Pick the frame that matches the tray size to keep the icon crisp.
        return new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
