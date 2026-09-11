using System.Diagnostics;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;

namespace DyndDns.TrayApp.Views;

/// <summary>
/// The app's main window: the domains recorded on this machine together with the routers and VPNs they
/// are bound to. The table shows every binding as its own row, so a domain routed through two routers is
/// visible twice, while a domain that is not bound anywhere is shown with an empty router and can be
/// bound right here. The layout is docked rather than positioned absolutely, so it survives resizing.
/// </summary>
internal sealed class MonitoringWindow : Form
{
    private const int MaxResults = 1000;

    /// <summary>How often the table is re-read while the "Автообновление" checkbox is on.</summary>
    private const int AutoRefreshInterval = 5000;
    private const string DomainColumn = "Domain";
    private const string RouterColumn = "Router";
    private const string InterfaceColumn = "Interface";
    private const string HitsColumn = "Hits";
    private const string LastSeenColumn = "LastSeen";
    private const string OnRouterColumn = "OnRouter";

    private static readonly Color RoutedViaSelectedColor = Color.FromArgb(198, 239, 206);
    private static readonly Color RoutedViaOtherColor = Color.FromArgb(255, 235, 156);

    // The two options of the router filter that are always present. The "not bound" one comes first: it is
    // the shortest way to see the domains that are not routed anywhere yet.
    private static readonly RouterChoice UnboundOnlyChoice = new(0, "Не привязанные ни к одному роутеру") { UnboundOnly = true };
    private static readonly RouterChoice AllRoutersChoice = new(null, "Все роутеры");

    private readonly IMonitoringWindowHost _host;

    private readonly TextBox _term;
    private readonly ComboBox _routerFilter;
    private readonly ComboBox _interfaceFilter;
    private readonly CheckBox _boundOnlyCheck;
    private readonly CheckBox _autoRefreshCheck;

    private readonly TextBox _domainInput;
    private readonly ComboBox _targetRouter;
    private readonly ComboBox _targetInterface;

    private readonly DataGridView _grid;
    private readonly Label _status;

    private readonly List<RouterProfile> _routers = [];
    private readonly Dictionary<string, List<DomainBinding>> _bindings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Interfaces each router reported, so the menu of the table can offer them without asking again.</summary>
    private readonly Dictionary<int, List<VpnInterfaceInfo>> _interfacesByRouter = [];

    /// <summary>Routers whose connections were asked for already, so the table never asks for one in a loop.</summary>
    private readonly HashSet<int> _interfacesRequested = [];
    private IReadOnlyDictionary<BindingKey, string> _routerState = new Dictionary<BindingKey, string>();

    private readonly System.Windows.Forms.Timer _autoRefresh;
    private ToolTip? _filterHint;

    /// <summary>Whether the user picked the router filter themselves; the window otherwise applies its default.</summary>
    private bool _userChoseFilter;

    /// <summary>Set while the window sets the router filter itself, so that is not taken for a choice.</summary>
    private bool _updatingFilter;

    /// <summary>Bin of the filter row; the control does not release the image it was given.</summary>
    private Bitmap? _binIcon;

    /// <summary>Menu of the table, shown by the window when the grid is right-clicked.</summary>
    private readonly ContextMenuStrip _rowMenu;

    /// <summary>Cover of the table while the window waits for what the routers have to say.</summary>
    private readonly Panel _loading;

    /// <summary>Reads on their way to the routers; the cover stays while at least one of them is running.</summary>
    private int _pendingReads;

    /// <summary>Column the table is ordered by, or <c>null</c> for the default order (newest lookup first).</summary>
    private string? _sortColumn;

    private bool _sortAscending = true;

    public MonitoringWindow(IMonitoringWindowHost host)
    {
        _host = host;

        Text = AppInfo.Title("Поиск и мониторинг");
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        ShowInTaskbar = true;

        _term = new TextBox { Dock = DockStyle.Fill };
        _routerFilter = CreateCombo(260);
        _interfaceFilter = CreateCombo(220);
        _boundOnlyCheck = new CheckBox { Text = "Только привязанные", AutoSize = true };
        _autoRefreshCheck = new CheckBox { Text = "Автообновление", AutoSize = true, Checked = true };

        // A placeholder, not pre-filled text: with "example.com" sitting in the field, pressing "Привязать"
        // bound that example instead of the rows selected in the table, which is what an empty field does.
        _domainInput = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "example.com" };
        _targetRouter = CreateCombo(240);
        _targetInterface = CreateCombo(220);

        _grid = CreateGrid();
        _loading = CreateLoadingOverlay();
        _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };

        var root = DialogLayout.Table(1, 5);
        root.Padding = new Padding(10);
        root.BackColor = SystemColors.Control;

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildSearchRow(), 0, 0);
        root.Controls.Add(BuildFilterRow(), 0, 1);
        root.Controls.Add(_grid, 0, 2);
        root.Controls.Add(_loading, 0, 2);
        root.Controls.Add(BuildActionPanel(), 0, 3);
        root.Controls.Add(_status, 0, 4);

        _loading.BringToFront();

        Controls.Add(root);

        // The table follows the term as it is typed, so the row needs no button of its own to start the search.
        _term.TextChanged += (_, _) => Rebuild();

        // Enter and F5 re-read the journal and the bindings; the typing itself only re-filters what is loaded.
        _term.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            Reload();
        };

        _domainInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            BindFromInput();
        };

        // Only a change the user made counts as a choice: the window sets the index itself when it loads the
        // profiles, and a list the user is not working with changes its index for the wheel as well, which is not
        // a choice either — the focus tells the two apart.
        _routerFilter.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingFilter || !_routerFilter.Focused)
                return;

            ApplyRouterFilter();
        };

        _interfaceFilter.SelectedIndexChanged += (_, _) => ApplyInterfaceFilter();
        _boundOnlyCheck.CheckedChanged += (_, _) => Rebuild();
        _autoRefreshCheck.CheckedChanged += (_, _) => ApplyAutoRefresh();
        _targetRouter.SelectedIndexChanged += (_, _) => ApplyTargetRouter();

        // Re-reads the journal periodically; the checkbox only starts and stops it. The timer is built once here,
        // because a window that is shown again would otherwise build a second one and keep both handlers.
        _autoRefresh = new System.Windows.Forms.Timer { Interval = AutoRefreshInterval };
        _autoRefresh.Tick += (_, _) => Reload();

        // The grid is not allowed to sort on its own: the rows are ordered while they are built, so the order
        // survives the rebuilds of the auto-refresh instead of being lost on the next one.
        _grid.ColumnHeaderMouseClick += (_, e) => SortBy(_grid.Columns[e.ColumnIndex].Name);
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].Cells[DomainColumn].Value is string domain)
                _domainInput.Text = domain;
        };

        // The menu of the table repeats the actions of the binding panel for the rows under the cursor, so a
        // domain can be routed without moving to the panel and picking the same values there. The window opens it
        // itself: the first right click on a window that is not active yet is spent on activating it, so a menu
        // handed to the grid needed a second click.
        _rowMenu = CreateRowMenu();

        // MouseUp of the control itself: the point it carries is measured from the corner of the grid, while the
        // one of a cell event is measured from the corner of the cell, which put the menu next to the wrong place.
        _grid.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
                ShowRowMenu(e.Location);
        };

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                Reload();
                return;
            }

            if (e.KeyCode == Keys.Delete && _grid.Focused)
            {
                e.SuppressKeyPress = true;
                UnbindSelected();
                return;
            }

            // The keyboard way to the same menu; the pointer way is the right click on the grid.
            if (_grid.Focused && (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10)))
            {
                e.SuppressKeyPress = true;
                ShowRowMenu(_grid.CurrentCell is { } cell
                    ? _grid.GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, cutOverflow: false).Location
                    : Point.Empty);
            }
        };

        UpdateBindingFilters();
        ApplyContentMinimumSize(root, new Size(1020, 620));
    }

    /// <summary>
    /// The switch describes bound domains only, while the "not bound" filter asks for the opposite, so it is
    /// disabled there instead of silently showing an empty table.
    /// </summary>
    private void UpdateBindingFilters() =>
        _boundOnlyCheck.Enabled = (_routerFilter.SelectedItem as RouterChoice)?.UnboundOnly != true;

    /// <summary>Keeps the content from being clipped; see <see cref="DialogLayout.ClientSizeNeeded"/>.</summary>
    private void ApplyContentMinimumSize(Control root, Size designed)
    {
        var size = DialogLayout.ClientSizeNeeded(root, designed);

        MinimumSize = SizeFromClientSize(size);
        ClientSize = size;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        LoadRouters();
        ReloadRouterState();
        Reload();
        ApplyAutoRefresh();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _autoRefresh.Stop();
        _autoRefresh.Dispose();

        _filterHint?.Dispose();
        _filterHint = null;

        _binIcon?.Dispose();
        _binIcon = null;

        // The menu of the table is not a component of the window, so nothing else releases it.
        _rowMenu.Dispose();

        base.OnFormClosed(e);
    }

    /// <summary>Queues the action for this window's thread; see <see cref="ViewDispatch.Post"/>.</summary>
    private void Post(Action action) => ViewDispatch.Post(this, action);

    /// <summary>
    /// Cover of the table: an answer of a router takes seconds, and until it arrives the table would look empty
    /// or show the state of the previous visit. It is drawn once and only made visible while a read is running.
    /// </summary>
    private static Panel CreateLoadingOverlay()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Control, Visible = false };

        // The strip is added last, so it takes its place above the message.
        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Загрузка данных с роутеров..."
        });

        panel.Controls.Add(new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 4,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30
        });

        return panel;
    }

    /// <summary>A read started: the cover appears and stays until every started read has finished.</summary>
    private void BeginRead()
    {
        _pendingReads++;
        SetLoading(true);
    }

    private void EndRead()
    {
        _pendingReads = Math.Max(0, _pendingReads - 1);

        if (_pendingReads == 0)
            SetLoading(false);
    }

    private void SetLoading(bool loading)
    {
        _loading.Visible = loading;

        if (loading)
            _loading.BringToFront();

        // The cursor follows the cover, so the whole window looks busy and not only the table.
        UseWaitCursor = loading;
    }

    /// <summary>
    /// Applies router state that was read on another thread; used when the tray side refreshes it after
    /// a synchronization. Reading the state also aligns the stored bindings with the routers, so the table is
    /// built from the fresh list: a domain added to a group in the router's web UI becomes a row of its own.
    /// </summary>
    public void ApplyRouterState(IReadOnlyDictionary<BindingKey, string> routerState) =>
        Post(() =>
        {
            _routerState = routerState;
            Reload();
        });

    /// <summary>
    /// Re-reads the router profiles, their state, the bindings and the journal. The tray calls it after a
    /// profile was added, changed or deleted, so an open window follows the new set of routers.
    /// </summary>
    public void ReloadData() =>
        Post(() =>
        {
            LoadRouters();
            ReloadRouterState();
            Reload();
        });

    // --- разметка ---

    private static ComboBox CreateCombo(int width) => new FilterComboBox
    {
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Margin = new Padding(0, 3, 12, 3)
    };

    /// <summary>
    /// Applies the router the user chose in the filter: the switches, the VPN list, the binding panel and the
    /// table follow it. The choice is remembered so a reload of the profiles does not undo it.
    /// </summary>
    internal void ApplyRouterFilter()
    {
        _userChoseFilter = true;
        UpdateBindingFilters();
        LoadFilterInterfaces();
        PointTargetRouterAtFilter();
        Rebuild();
    }

    /// <summary>
    /// Points the binding panel at the router the filter was set to: the table shows that router and the next
    /// domain is bound to it, so the panel does not have to be set to the same router again by hand.
    /// </summary>
    private void PointTargetRouterAtFilter()
    {
        if ((_routerFilter.SelectedItem as RouterChoice) is not { RouterId: > 0 } router)
            return;

        var index = FindChoice(_targetRouter, router.RouterId);

        if (index is null)
            return;

        // The list of the panel follows the chosen router, so its connections are the ones of that router.
        _targetRouter.SelectedIndex = index.Value;
        LoadTargetInterfaces();
    }

    /// <summary>Rebuilds the table for the interface the user picked in the filter.</summary>
    private void ApplyInterfaceFilter()
    {
        if (_interfaceFilter.Focused)
            Rebuild();
    }

    /// <summary>Re-reads the connections of the router the user picked in the binding panel.</summary>
    private void ApplyTargetRouter()
    {
        if (_targetRouter.Focused)
            LoadTargetInterfaces();
    }

    /// <summary>Label of a grid row: natural height, vertically centred, with a gap to its control.</summary>
    private static Label RowLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 0, 8, 0)
    };

    private Control BuildSearchRow()
    {
        var row = DialogLayout.Table(2);

        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        row.Controls.Add(RowLabel("Домен содержит:"), 0, 0);
        row.Controls.Add(_term, 1, 0);

        return row;
    }

    /// <summary>
    /// Row of filters. It is a grid, not a wrapping flow panel: a flow panel would push its last controls
    /// onto a second line that the auto-sized row cannot show, which is how the autostart checkbox used to
    /// disappear in a narrow window.
    /// </summary>
    private Control BuildFilterRow()
    {
        var row = DialogLayout.Table(8);

        // The window starts on «Все роутеры»: it shows what the routers have, bound and not bound alike, while
        // «Не привязанные ни к одному роутеру» stays a choice for finding the domains still waiting for one.
        _routerFilter.Items.Add(AllRoutersChoice);
        _routerFilter.Items.Add(UnboundOnlyChoice);
        SelectRouterFilter(_routerFilter.Items.IndexOf(AllRoutersChoice));

        // Held in a field so the window can release it: a tooltip owns a native window of its own.
        _filterHint = new ToolTip();
        _filterHint.SetToolTip(_routerFilter, "По умолчанию видны маршруты роутеров; «Не привязанные ни к одному роутеру» — домены без привязок");
        _filterHint.SetToolTip(_interfaceFilter, "Выберите конкретный роутер, чтобы фильтровать по его VPN");

        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        row.Controls.Add(RowLabel("Фильтр — роутер:"), 0, 0);
        row.Controls.Add(_routerFilter, 1, 0);
        row.Controls.Add(RowLabel("VPN:"), 2, 0);
        row.Controls.Add(_interfaceFilter, 3, 0);
        row.Controls.Add(_boundOnlyCheck, 4, 0);
        row.Controls.Add(_autoRefreshCheck, 5, 0);

        // The bin ends the row: the columns that stretch come before it, so it is not left in the middle.
        row.Controls.Add(CreateClearUnboundButton(), 7, 0);

        return row;
    }

    /// <summary>
    /// The bin of the filter row: the journal grows with every lookup, and this drops the domains that are routed
    /// nowhere. An icon next to the checkboxes, because the one button that belongs to the list below stands out
    /// less there than it crowds the panel; the tooltip says what it clears.
    /// </summary>
    private Button CreateClearUnboundButton()
    {
        _binIcon = CreateBinIcon();

        var button = new Button
        {
            Name = "ClearUnbound",
            AccessibleName = "Очистить не привязанные",
            Image = _binIcon,
            ImageAlign = ContentAlignment.MiddleCenter,
            Width = 30,
            Height = 28,
            Margin = new Padding(10, 3, 0, 3)
        };

        button.Click += (_, _) => ClearUnboundDomains();
        _filterHint!.SetToolTip(button, "Убрать из журнала домены, не привязанные ни к одному роутеру");

        return button;
    }

    /// <summary>
    /// The bin itself, drawn here: the window carries no image resources, and the font of the interface has no
    /// character of that shape.
    /// </summary>
    private static Bitmap CreateBinIcon()
    {
        var icon = new Bitmap(16, 16);

        using var graphics = Graphics.FromImage(icon);
        using var pen = new Pen(SystemColors.ControlText);

        graphics.DrawLine(pen, 2, 4, 13, 4);
        graphics.DrawLine(pen, 6, 2, 9, 2);
        graphics.DrawLine(pen, 3, 4, 4, 14);
        graphics.DrawLine(pen, 12, 4, 11, 14);
        graphics.DrawLine(pen, 4, 14, 11, 14);
        graphics.DrawLine(pen, 6, 7, 6, 12);
        graphics.DrawLine(pen, 9, 7, 9, 12);

        return icon;
    }

    /// <summary>Binding panel: add a domain by hand or act on the rows selected in the table.</summary>
    private Control BuildActionPanel()
    {
        var group = DialogLayout.Group("Привязка доменов", new Padding(10));
        var rows = DialogLayout.Table(1, 2);

        // Grids, not wrapping flow panels: a flow panel would move its last controls onto a second line
        // that an auto-sized row cannot show, which is how the bind button used to disappear.
        var bindRow = DialogLayout.Table(7);

        bindRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bindRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bindRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bindRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bindRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bindRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bindRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var bindButton = new Button { Text = "Привязать (Enter)", Width = 135, Height = 28, Margin = new Padding(8, 0, 0, 0) };
        bindButton.Click += (_, _) => BindFromInput();

        _domainInput.MinimumSize = new Size(160, 0);

        bindRow.Controls.Add(RowLabel("Домен:"), 0, 0);
        bindRow.Controls.Add(_domainInput, 1, 0);
        bindRow.Controls.Add(RowLabel("к роутеру:"), 2, 0);
        bindRow.Controls.Add(_targetRouter, 3, 0);
        bindRow.Controls.Add(RowLabel("через VPN:"), 4, 0);
        bindRow.Controls.Add(_targetInterface, 5, 0);
        bindRow.Controls.Add(bindButton, 6, 0);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            ForeColor = SystemColors.GrayText,
            Text = "Введите домен и нажмите «Привязать», чтобы добавить его вручную. " +
                   "Правая кнопка на строке привязывает выбранные домены к роутеру, Delete отвязывает их. " +
                   "Двойной щелчок по строке подставляет домен в поле. F5 перечитывает журнал и привязки. " +
                   "Корзина в строке фильтров убирает из журнала домены без привязок."
        };

        rows.Controls.Add(bindRow, 0, 0);
        rows.Controls.Add(hint, 0, 1);
        group.Controls.Add(rows);

        return group;
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SystemColors.Window,
            EnableHeadersVisualStyles = false
        };

        // The header of the column that holds the current cell is painted with the colours of a selection, which
        // made "Домен" — the first column — look like the column the table is sorted by. The header keeps its own
        // colours in every state instead, which the grid only honours with the visual styles switched off.
        grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = grid.ColumnHeadersDefaultCellStyle.BackColor;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = SystemColors.ControlText;

        grid.Columns.Add(CreateColumn(DomainColumn, "Домен", 30));
        grid.Columns.Add(CreateColumn(RouterColumn, "Роутер", 22));
        grid.Columns.Add(CreateColumn(InterfaceColumn, "VPN", 16));
        grid.Columns.Add(CreateColumn(HitsColumn, "Обращений", 9));
        grid.Columns.Add(CreateColumn(LastSeenColumn, "Последний раз", 14));
        grid.Columns.Add(CreateColumn(OnRouterColumn, "На роутере", 9));

        return grid;
    }

    /// <summary>
    /// The rows are ordered by <see cref="OrderRows"/> while they are built, so the columns leave the sorting to
    /// the window: the grid would otherwise reorder the rows behind its back and keep the header of the sorted
    /// column highlighted in blue.
    /// </summary>
    private static DataGridViewTextBoxColumn CreateColumn(string name, string header, int fillWeight) => new()
    {
        Name = name,
        HeaderText = header,
        FillWeight = fillWeight,
        SortMode = DataGridViewColumnSortMode.Programmatic
    };

    /// <summary>
    /// Menu of the table. It is filled while it opens, so the routers it offers are the profiles of the moment
    /// and their interfaces are the ones the window has already read.
    /// </summary>
    private ContextMenuStrip CreateRowMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Opening += (_, _) =>
        {
            // The auto-refresh is held while the menu is open: rebuilding the rows would drop the selection the
            // menu was opened for and move the entries the pointer is on out from under it.
            SuspendAutoRefresh(suspend: true);
            FillRowMenu(menu);
        };

        menu.Closed += (_, _) => SuspendAutoRefresh(suspend: false);

        return menu;
    }

    /// <summary>Holds the auto-refresh for a moment; the switch of the checkbox decides whether it comes back.</summary>
    private void SuspendAutoRefresh(bool suspend)
    {
        if (suspend)
            _autoRefresh.Stop();
        else
            ApplyAutoRefresh();
    }

    /// <summary>
    /// Fills the menu with what the binding panel does for the selected rows: every router with the connections it
    /// has, one entry each, plus detaching. The entries are a plain list on purpose — a submenu has to be opened by
    /// hovering before a connection can be picked, and a click is what chooses here.
    /// </summary>
    internal void FillRowMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        var domains = SelectedDomains();

        if (domains.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("Строки не выбраны") { Enabled = false });
            return;
        }

        var bind = new ToolStripMenuItem($"Привязать выбранные ({domains.Count})") { Enabled = _routers.Count > 0 };

        foreach (var profile in _routers)
        {
            var connections = InterfaceChoices(profile.VpnInterface, InterfacesOf(profile.Id));

            if (connections.Count == 0)
            {
                // Nothing is known about the connections of the router yet, so the bind goes to the interface of
                // the profile; the router resolves it when the route is written.
                bind.DropDownItems.Add(CreateMenuItem(
                    $"{profile.DisplayName} · VPN роутера по умолчанию",
                    () => BindDomains(profile.Id, domains, string.Empty, profile.DisplayName)));
                continue;
            }

            foreach (var connection in connections)
            {
                bind.DropDownItems.Add(CreateMenuItem(
                    $"{profile.DisplayName} · {connection.Name}",
                    () => BindDomains(profile.Id, domains, connection.Value, profile.DisplayName)));
            }
        }

        if (_routers.Count == 0)
            bind.DropDownItems.Add(new ToolStripMenuItem("Роутеров нет") { Enabled = false });

        menu.Items.Add(bind);
        menu.Items.Add(new ToolStripSeparator());

        var unbind = CreateMenuItem($"Отвязать выбранные ({domains.Count})", UnbindSelected);
        unbind.ShortcutKeyDisplayString = "Delete";
        menu.Items.Add(unbind);
    }

    private static ToolStripMenuItem CreateMenuItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Interfaces a router reported, empty while the window knows nothing about it yet.</summary>
    private IEnumerable<VpnInterfaceInfo> InterfacesOf(int routerId) =>
        _interfacesByRouter.TryGetValue(routerId, out var interfaces) ? interfaces : [];

    /// <summary>Opens the menu of the table at a point of the grid, acting on the row under it.</summary>
    private void ShowRowMenu(Point location)
    {
        // The row the menu points at wins over a selection that does not cover it — the rows may have been rebuilt
        // since the selection was made, and a right click on a fresh window has no selection at all yet. A
        // selection that does cover the row stays, so several rows can be bound at once.
        var hit = _grid.HitTest(location.X, location.Y);

        if (hit.RowIndex >= 0 && !_grid.Rows[hit.RowIndex].Selected)
        {
            _grid.ClearSelection();
            _grid.Rows[hit.RowIndex].Selected = true;
            _grid.CurrentCell = _grid.Rows[hit.RowIndex].Cells[DomainColumn];
        }

        _rowMenu.Show(_grid, location);
    }

    // --- данные ---

    private void Reload()
    {
        try
        {
            _bindings.Clear();

            foreach (var binding in _host.GetBindings())
            {
                if (!_bindings.TryGetValue(binding.Domain, out var list))
                    _bindings[binding.Domain] = list = [];

                list.Add(binding);
            }
        }
        catch (Exception ex)
        {
            _status.Text = "Не удалось прочитать привязки";
            Trace.TraceError($"Binding list failed: {ex.Message}");
            return;
        }

        Rebuild();
        LoadMissingInterfaces();
    }

    /// <summary>
    /// Asks about the connections of the routers the table shows — once per router — so the "VPN" column can name
    /// a connection the way the drop-downs do. The answer rebuilds the table; a router that does not answer keeps
    /// its interfaces under their own names.
    /// </summary>
    private void LoadMissingInterfaces()
    {
        var routers = _bindings.Values
            .SelectMany(list => list)
            .Select(binding => binding.RouterId)
            .Distinct()
            .Where(routerId => _interfacesRequested.Add(routerId))
            .ToList();

        foreach (var routerId in routers)
            LoadInterfaces(routerId, _ => Rebuild());
    }

    private void Rebuild()
    {
        try
        {
            var term = _term.Text.Trim();
            var stats = _host.SearchDomains(term.Length == 0 ? null : term, MaxResults);
            var routerFilter = _routerFilter.SelectedItem as RouterChoice;
            var specificRouter = routerFilter is { RouterId: > 0 } ? routerFilter.RouterId : null;
            var interfaceFilter = InterfaceFilterValue((_interfaceFilter.SelectedItem as InterfaceChoice)?.Value);

            _grid.Rows.Clear();

            // The rows are collected first: the order a click on a header asks for can only be applied to the
            // whole table, not to the row that happens to be added.
            var rows = new List<TableRow>();

            foreach (var stat in TableDomains(stats, AllBindings(), term))
            {
                var bindings = _bindings.TryGetValue(stat.Domain, out var list)
                    ? list.OrderBy(binding => binding.RouterName, StringComparer.OrdinalIgnoreCase).ToList()
                    : [];

                if (bindings.Count == 0)
                {
                    // An unbound domain is shown until a filter asks for a binding.
                    var wantsUnbound = routerFilter?.UnboundOnly == true;
                    var bindingRequired = specificRouter is not null || interfaceFilter is not null || _boundOnlyCheck.Checked;

                    if (!wantsUnbound && bindingRequired)
                        continue;

                    rows.Add(CreateRow(stat, null));
                    continue;
                }

                // The domain is bound, so it does not belong to the "not bound" filter.
                if (routerFilter?.UnboundOnly == true)
                    continue;

                foreach (var binding in bindings)
                {
                    if (specificRouter is not null && specificRouter != binding.RouterId)
                        continue;

                    var effective = EffectiveInterface(binding);

                    if (interfaceFilter is not null && !string.Equals(interfaceFilter, effective, StringComparison.OrdinalIgnoreCase))
                        continue;

                    rows.Add(CreateRow(stat, binding));
                }
            }

            foreach (var row in OrderRows(rows, _sortColumn, _sortAscending))
                AddRow(row);

            var shown = _grid.Rows.Count;

            _status.Text = shown == 0
                ? $"Ничего не найдено. Записей в журнале: {stats.Count}. Домен можно добавить вручную ниже."
                : $"Строк: {shown} · журнал: {stats.Count} доменов · роутеров: {_routers.Count} · обновлено {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            // The status line reports it instead of a message box: the table is rebuilt on every auto-refresh,
            // so a persistent database problem would keep throwing modal dialogs at the user.
            _status.Text = $"Ошибка чтения базы: {ex.Message}";
            Trace.TraceError($"Monitoring table failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The domains the table shows: the journal rows, completed with the bound domains the journal does not
    /// know. A domain added by hand — or adopted from a group on the router — is a binding like any other, so it
    /// has to be visible (and filterable) even though this machine never looked it up. <paramref name="stats"/>
    /// arrives already narrowed by the search, while <paramref name="term"/> narrows only the added domains; a
    /// domain without a look-up gets no hits, no time and sorts after the journal rows.
    /// </summary>
    internal static IEnumerable<DomainStat> TableDomains(
        IReadOnlyList<DomainStat> stats,
        IReadOnlyCollection<DomainBinding> bindings,
        string? term)
    {
        var known = new HashSet<string>(stats.Select(stat => stat.Domain), StringComparer.OrdinalIgnoreCase);

        var extra = bindings
            .Select(binding => binding.Domain)
            .Where(domain => !known.Contains(domain))
            .Where(domain => string.IsNullOrEmpty(term) || domain.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .Select(domain => new DomainStat(domain, 0, DateTime.MinValue));

        return stats.OrderByDescending(stat => stat.LastSeen).Concat(extra);
    }

    private IReadOnlyCollection<DomainBinding> AllBindings() =>
        [.. _bindings.Values.SelectMany(list => list)];

    /// <summary>
    /// The VPN the filter narrows the table to, or <c>null</c> while «Все VPN» is chosen: that option carries an
    /// empty value and means "no filter", so taking it for an interface name dropped every row and left the
    /// table empty.
    /// </summary>
    internal static string? InterfaceFilterValue(string? selectedValue) =>
        string.IsNullOrEmpty(selectedValue) ? null : selectedValue;

    private TableRow CreateRow(DomainStat stat, DomainBinding? binding)
    {
        var effective = binding is null ? string.Empty : EffectiveInterface(binding);
        var onRouter = binding is not null && _routerState.TryGetValue(new BindingKey(binding.RouterId, binding.Domain), out var routed)
            ? routed
            : string.Empty;

        return new TableRow(
            stat,
            binding,
            DescribeInterface(binding, effective),
            onRouter,
            onRouter.Length > 0 && string.Equals(onRouter, effective, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The rows in the order the table shows them: by the column the user clicked on, or, while no column is
    /// chosen, by the time of the last lookup with the domains no lookup ever reached at the end. The order is
    /// applied here and not by the grid, so the rebuilds of the auto-refresh do not lose it.
    /// </summary>
    internal static List<TableRow> OrderRows(IEnumerable<TableRow> rows, string? column, bool ascending)
    {
        if (column is null)
        {
            return
            [
                .. rows.OrderByDescending(row => row.Stat.LastSeen)
                    .ThenBy(row => row.Stat.Domain, StringComparer.OrdinalIgnoreCase)
            ];
        }

        var ordered = column switch
        {
            DomainColumn => rows.OrderBy(row => row.Stat.Domain, StringComparer.OrdinalIgnoreCase),
            RouterColumn => rows.OrderBy(row => row.Binding?.RouterName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            InterfaceColumn => rows.OrderBy(row => row.Interface, StringComparer.OrdinalIgnoreCase),
            HitsColumn => rows.OrderBy(row => row.Stat.Hits),
            LastSeenColumn => rows.OrderBy(row => row.Stat.LastSeen),
            _ => rows.OrderBy(row => row.OnRouter, StringComparer.OrdinalIgnoreCase)
        };

        return [.. ascending ? ordered : ordered.Reverse()];
    }

    /// <summary>Orders the table by the clicked column, the same column a second time turning the order around.</summary>
    private void SortBy(string column)
    {
        _sortAscending = _sortColumn == column ? !_sortAscending : true;
        _sortColumn = column;

        ShowSortGlyph();
        Rebuild();
    }

    /// <summary>Shows on the header which column the table is ordered by.</summary>
    private void ShowSortGlyph()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.HeaderCell.SortGlyphDirection = column.Name == _sortColumn
                ? _sortAscending ? SortOrder.Ascending : SortOrder.Descending
                : SortOrder.None;
        }
    }

    private void AddRow(TableRow row)
    {
        var rowIndex = _grid.Rows.Add(
            row.Stat.Domain,
            row.Binding?.RouterName ?? string.Empty,
            row.Interface,
            row.Stat.Hits,
            LastSeenText(row.Stat),
            row.OnRouter);

        var gridRow = _grid.Rows[rowIndex];
        gridRow.Tag = row.Binding is null ? null : new BindingKey(row.Binding.RouterId, row.Binding.Domain);

        if (row.OnRouter.Length > 0)
        {
            gridRow.DefaultCellStyle.BackColor = row.RoutedViaSelected ? RoutedViaSelectedColor : RoutedViaOtherColor;
        }
    }

    /// <summary>What the row shows in the "last lookup" column.</summary>
    private static string LastSeenText(DomainStat stat) =>
        stat.LastSeen == DateTime.MinValue
            ? "—"
            : stat.LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    /// <summary>Interface the binding really uses: its own one, or the default of the profile.</summary>
    private string EffectiveInterface(DomainBinding binding) =>
        binding.Interface.Length > 0
            ? binding.Interface
            : _routers.FirstOrDefault(profile => profile.Id == binding.RouterId)?.VpnInterface ?? string.Empty;

    /// <summary>
    /// What the "VPN" column says: the connection the binding goes through, named as the drop-downs name it. A
    /// binding without an interface of its own goes through the one of its profile — the text says so, because
    /// the name alone would look like a binding that was made by hand.
    /// </summary>
    private string DescribeInterface(DomainBinding? binding, string effective)
    {
        if (binding is null)
            return string.Empty;

        if (binding.Interface.Length > 0)
            return VpnLabelFor(binding.RouterId, effective);

        return effective.Length > 0
            ? $"по умолчанию: {VpnLabelFor(binding.RouterId, effective)}"
            : "по умолчанию роутера";
    }

    /// <summary>
    /// Label of one connection: the name the router gives it with the interface in parentheses. An interface the
    /// window knows nothing about keeps its own name.
    /// </summary>
    private string VpnLabelFor(int routerId, string interfaceName) =>
        VpnLabel.Format(
            interfaceName,
            InterfacesOf(routerId)
                .FirstOrDefault(info => string.Equals(info.Name, interfaceName, StringComparison.OrdinalIgnoreCase))
                ?.Description);

    /// <summary>Reads the profiles into the drop-downs; also called by a test, which drives the window without showing it.</summary>
    internal void LoadRouters()
    {
        _routers.Clear();
        _routers.AddRange(_host.GetRouters());

        var previousFilter = (_routerFilter.SelectedItem as RouterChoice)?.RouterId;
        var previousTarget = (_targetRouter.SelectedItem as RouterChoice)?.RouterId;

        _routerFilter.Items.Clear();
        _routerFilter.Items.Add(AllRoutersChoice);
        _routerFilter.Items.Add(UnboundOnlyChoice);

        _targetRouter.Items.Clear();

        foreach (var profile in _routers)
        {
            _routerFilter.Items.Add(new RouterChoice(profile.Id, profile.DisplayName));
            _targetRouter.Items.Add(new RouterChoice(profile.Id, profile.DisplayName));
        }

        // What the user chose survives a reload; an untouched filter goes back to the default, because a router
        // may have been added or removed since.
        SelectRouterFilter(_userChoseFilter
            ? FindChoice(_routerFilter, previousFilter) ?? _routerFilter.Items.IndexOf(AllRoutersChoice)
            : _routerFilter.Items.IndexOf(AllRoutersChoice));

        _targetRouter.SelectedIndex = FindChoice(_targetRouter, previousTarget) ??
            (_targetRouter.Items.Count > 0 ? 0 : -1);

        LoadFilterInterfaces();
        LoadTargetInterfaces();
    }

    private void SelectRouterFilter(int index)
    {
        _updatingFilter = true;

        try
        {
            _routerFilter.SelectedIndex = index;
        }
        finally
        {
            _updatingFilter = false;
        }
    }

    private static int? FindChoice(ComboBox combo, int? routerId)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is RouterChoice choice && choice.RouterId == routerId)
                return index;
        }

        return null;
    }

    /// <summary>
    /// VPN options of the filter: the interfaces of the chosen router, when one is chosen. The interfaces
    /// arrive later, so the list is filled with what is known and completed when the router answers.
    /// </summary>
    private void LoadFilterInterfaces()
    {
        var routerId = (_routerFilter.SelectedItem as RouterChoice)?.RouterId;
        var previous = (_interfaceFilter.SelectedItem as InterfaceChoice)?.Value;

        _interfaceFilter.Items.Clear();
        _interfaceFilter.Items.Add(new InterfaceChoice(string.Empty, "Все VPN"));

        if (routerId is not > 0)
        {
            // No router is chosen, so there is nothing to narrow down: the list stays on «Все VPN» and is switched
            // off instead of offering interfaces that belong to no particular router.
            _interfaceFilter.SelectedIndex = 0;
            _interfaceFilter.Enabled = false;
            return;
        }

        _interfaceFilter.Enabled = true;

        var profile = _routers.FirstOrDefault(item => item.Id == routerId);

        AddInterfaceChoices(_interfaceFilter, InterfaceChoices(profile?.VpnInterface, []));

        _interfaceFilter.SelectedIndex = Math.Max(IndexOfInterfaceChoice(_interfaceFilter, previous), 0);

        LoadInterfaces(routerId.Value, interfaces =>
        {
            AddInterfaceChoices(_interfaceFilter, InterfaceChoices(profile?.VpnInterface, interfaces));

            if (previous is not null)
                _interfaceFilter.SelectedIndex = Math.Max(IndexOfInterfaceChoice(_interfaceFilter, previous), 0);
        });
    }

    /// <summary>
    /// Index of an interface option in one of the drop-downs, or <c>-1</c> when it is not listed yet. Both
    /// drop-downs use it, so a router that answers twice never adds its interfaces a second time; the names are
    /// compared ignoring case, because the router and the profile do not always spell them alike.
    /// </summary>
    internal static int IndexOfInterfaceChoice(ComboBox combo, string? value)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is InterfaceChoice choice &&
                string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    /// <summary>
    /// The connections a drop-down offers for a router: the one its profile uses together with the ones the router
    /// reported, each interface once, written as «имя (интерфейс)». There is no "по умолчанию" entry of its own,
    /// because the interface of the profile is the same connection under the label it already has in the list.
    /// </summary>
    internal static List<InterfaceChoice> InterfaceChoices(string? profileInterface, IEnumerable<VpnInterfaceInfo> reported)
    {
        var choices = new List<InterfaceChoice>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var interfaces = reported.ToList();

        Add(profileInterface, DescriptionOf(profileInterface));

        foreach (var info in interfaces)
            Add(info.Name, info.Description);

        return choices;

        void Add(string? interfaceName, string? description)
        {
            if (!string.IsNullOrWhiteSpace(interfaceName) && known.Add(interfaceName))
                choices.Add(new InterfaceChoice(interfaceName, VpnLabel.Format(interfaceName, description)));
        }

        string? DescriptionOf(string? interfaceName) =>
            interfaces
                .FirstOrDefault(info => string.Equals(info.Name, interfaceName, StringComparison.OrdinalIgnoreCase))
                ?.Description;
    }

    /// <summary>
    /// Adds the connections a drop-down does not list yet and refreshes the label of the ones it already has, so a
    /// connection listed before the router described it gets its name once the answer arrives. One of them is kept
    /// selected.
    /// </summary>
    internal static void AddInterfaceChoices(ComboBox combo, IEnumerable<InterfaceChoice> choices)
    {
        foreach (var choice in choices)
        {
            var index = IndexOfInterfaceChoice(combo, choice.Value);

            if (index < 0)
            {
                combo.Items.Add(choice);
                continue;
            }

            // The interface of the profile stands in the list before its router was asked, so the entry it created
            // carries no name yet; the answer replaces that label instead of standing next to it.
            if (combo.Items[index] is InterfaceChoice listed && listed.Name != choice.Name)
                combo.Items[index] = choice;
        }

        if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    /// <summary>
    /// VPN options of the binding panel: the connections of the chosen router, listed by name. The interface of
    /// the profile is one of them, so it is not offered a second time as a "default".
    /// </summary>
    private void LoadTargetInterfaces()
    {
        var routerId = (_targetRouter.SelectedItem as RouterChoice)?.RouterId;
        var profile = _routers.FirstOrDefault(item => item.Id == routerId);

        _targetInterface.Items.Clear();
        AddInterfaceChoices(_targetInterface, InterfaceChoices(profile?.VpnInterface, []));

        if (routerId is not > 0)
            return;

        LoadInterfaces(
            routerId.Value,
            interfaces => AddInterfaceChoices(_targetInterface, InterfaceChoices(profile?.VpnInterface, interfaces)));
    }

    private void ReloadRouterState()
    {
        BeginRead();

        Task.Run(_host.GetRouterState).ContinueWith(
            task => Post(() => ApplyRouterState(task)),
            TaskScheduler.Default);
    }

    private void ApplyRouterState(Task<IReadOnlyDictionary<BindingKey, string>> task)
    {
        var read = false;

        try
        {
            if (task.IsFaulted)
                throw task.Exception!.GetBaseException();

            _routerState = new Dictionary<BindingKey, string>(task.Result);
            read = true;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Router routing state lookup failed: {ex.Message}");
            _status.Text = "Не удалось получить маршруты с роутеров";
        }
        finally
        {
            EndRead();
        }

        // A successful read also aligned the stored bindings with the routers, so the table is rebuilt from the
        // fresh list (the colours live in AddRow alone, and the "on the router only" filter is applied there);
        // after a failure the rows are only repainted with the state that was kept.
        if (read)
            Reload();
        else
            Rebuild();
    }

    /// <summary>
    /// Reads the interfaces of a router on the thread pool and hands them to <paramref name="apply"/> on the
    /// UI thread: the call talks to the router and would freeze the window for the whole request timeout
    /// otherwise. A failure is reported in the status line instead of being thrown.
    /// </summary>
    private void LoadInterfaces(int routerId, Action<IReadOnlyList<VpnInterfaceInfo>> apply)
    {
        BeginRead();

        Task.Run(() => _host.GetVpnInterfaces(routerId)).ContinueWith(
            task => Post(() =>
            {
                EndRead();

                if (task.IsFaulted)
                {
                    Trace.TraceError($"VPN interface lookup for router {routerId} failed: {task.Exception?.GetBaseException().Message}");
                    _status.Text = "Не удалось получить VPN-интерфейсы роутера";
                    apply([]);
                    return;
                }

                _interfacesByRouter[routerId] = [.. task.Result];
                apply(task.Result);
            }),
            TaskScheduler.Default);
    }

    // --- действия ---

    /// <summary>
    /// Binds the domain typed in the field; when the field is empty, the rows selected in the table are
    /// bound instead, which is what the button does in both cases.
    /// </summary>
    private void BindFromInput()
    {
        if (_targetRouter.SelectedItem is not RouterChoice { RouterId: > 0 } router)
        {
            _status.Text = "Выберите роутер, к которому привязать домен.";
            return;
        }

        var typed = DomainNormalizer.Normalize(_domainInput.Text);
        List<string> domains = typed.Length > 0 ? [typed] : SelectedDomains();

        if (domains.Count == 0)
        {
            _status.Text = "Введите домен или выберите строки в таблице.";
            return;
        }

        var interfaceName = (_targetInterface.SelectedItem as InterfaceChoice)?.Value ?? string.Empty;

        _domainInput.Text = string.Empty;
        BindDomains(router.RouterId!.Value, domains, interfaceName, router.Name);
    }

    /// <summary>
    /// Binds the domains to one router and interface and reports what happened. The binding panel and the menu of
    /// the table both come here, so the two ways of binding cannot drift apart.
    /// </summary>
    private void BindDomains(int routerId, IReadOnlyList<string> domains, string interfaceName, string routerName)
    {
        var moved = CountMovedBindings(routerId, domains, interfaceName);
        var changed = _host.Bind(routerId, domains, interfaceName);

        Reload();

        _status.Text = changed == 0
            ? "Такая привязка уже есть."
            : moved > 0
                ? $"Доменов переведено на другой VPN: {moved} (у домена один интерфейс на роутер)."
                : $"Привязано доменов: {changed} к {routerName}";
    }

    /// <summary>
    /// How many of the domains are bound to that router through another interface already. A domain has one
    /// interface per router, so binding it again moves it instead of adding a second route for the same name.
    /// </summary>
    private int CountMovedBindings(int routerId, IReadOnlyList<string> domains, string interfaceName)
    {
        var moved = 0;

        foreach (var domain in domains)
        {
            if (!_bindings.TryGetValue(domain, out var list))
                continue;

            if (list.Any(binding => binding.RouterId == routerId &&
                !string.Equals(binding.Interface, interfaceName, StringComparison.OrdinalIgnoreCase)))
            {
                moved++;
            }
        }

        return moved;
    }

    private void UnbindSelected()
    {
        var keys = SelectedKeys();

        if (keys.Count == 0)
        {
            _status.Text = "Выберите привязанные строки в таблице.";
            return;
        }

        var removed = _host.Unbind(keys);

        Reload();
        _status.Text = removed == 0
            ? "Выбранные строки не были привязаны."
            : $"Отвязано доменов: {removed}";
    }

    /// <summary>
    /// Drops the journal entries without a binding, i.e. the rows the "not bound" filter shows: the journal
    /// grows with every lookup, and the domains routed somewhere are what the user keeps.
    /// </summary>
    private void ClearUnboundDomains()
    {
        if (!Dialogs.Ask(
            this,
            "Удалить из журнала домены, которые не привязаны ни к одному роутеру? Домены с привязками останутся."))
        {
            return;
        }

        var removed = _host.ClearUnboundDomains();

        Reload();

        _status.Text = removed == 0
            ? "Не привязанных доменов не было."
            : $"Удалено из журнала доменов: {removed}";
    }

    private List<DataGridViewRow> SelectedRows() =>
        [.. _grid.SelectedRows.Cast<DataGridViewRow>()];

    /// <summary>Domains of the selected rows, without repetitions.</summary>
    private List<string> SelectedDomains() =>
        [.. SelectedRows()
            .Select(row => row.Cells[DomainColumn].Value as string)
            .Where(domain => !string.IsNullOrEmpty(domain))
            .Select(domain => domain!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private List<BindingKey> SelectedKeys() =>
        [.. SelectedRows()
            .Select(row => row.Tag)
            .OfType<BindingKey>()
            .Distinct()];

    private void ApplyAutoRefresh()
    {
        if (_autoRefreshCheck.Checked)
            _autoRefresh.Start();
        else
            _autoRefresh.Stop();
    }

    /// <summary>Choice of the router drop-downs; <c>null</c> means "all", 0 means "not bound".</summary>
    private sealed record RouterChoice(int? RouterId, string Name)
    {
        public bool UnboundOnly { get; init; }

        public override string ToString() => Name;
    }

    /// <summary>Choice of the VPN drop-downs; an empty value means the profile's own interface.</summary>
    internal sealed record InterfaceChoice(string Value, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// Drop-down that leaves the wheel alone while it has no focus. Windows sends the wheel to the control under
    /// the pointer, so scrolling over a closed list changed the chosen router behind the user's back instead of
    /// waiting for a click.
    /// </summary>
    internal sealed class FilterComboBox : ComboBox
    {
        private const int MouseWheelMessage = 0x020A;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == MouseWheelMessage && !Focused)
                return;

            base.WndProc(ref m);
        }
    }

    /// <summary>
    /// One row of the table: what the grid shows, plus the values the order is chosen by. A domain without a
    /// binding has an empty router, interface and state.
    /// </summary>
    internal sealed record TableRow(
        DomainStat Stat,
        DomainBinding? Binding,
        string Interface,
        string OnRouter,
        bool RoutedViaSelected);
}
