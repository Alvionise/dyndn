using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Views;

/// <summary>
/// Search window over the locally recorded DNS domains. Selected rows can be pushed to the router
/// with a VPN interface chosen in the drop-down; already tracked domains are marked in the grid.
/// </summary>
internal sealed class DnsSearchWindow : Form
{
    private const int MaxResults = 1000;
    private const string StateColumn = "Added";

    private static readonly Color RoutedViaSelectedColor = Color.FromArgb(198, 239, 206);
    private static readonly Color RoutedViaOtherColor = Color.FromArgb(255, 235, 156);

    private readonly Func<string?, int, IReadOnlyList<DomainStat>> _search;
    private readonly Func<IReadOnlyList<VpnInterfaceInfo>> _getInterfaces;
    private readonly Func<IReadOnlyList<DnsRoute>> _getRoutes;
    private readonly Func<IReadOnlyDictionary<string, string>> _getRoutedDomains;
    private readonly Func<string, string, bool> _addRoute;
    private readonly Func<string, bool> _removeRoute;

    private Dictionary<string, string> _routedDomains = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _localRoutes = new(StringComparer.OrdinalIgnoreCase);

    private readonly TextBox _term;
    private readonly ComboBox _interfaces;
    private readonly DataGridView _grid;
    private readonly Label _status;
    private readonly CheckBox _autoRefreshCheck;
    private readonly CheckBox _routedOnlyCheck;

    private System.Windows.Forms.Timer? _autoRefresh;

    public DnsSearchWindow(
        Func<string?, int, IReadOnlyList<DomainStat>> search,
        Func<IReadOnlyList<VpnInterfaceInfo>> getInterfaces,
        Func<IReadOnlyList<DnsRoute>> getRoutes,
        Func<IReadOnlyDictionary<string, string>> getRoutedDomains,
        Func<string, string, bool> addRoute,
        Func<string, bool> removeRoute)
    {
        _search = search;
        _getInterfaces = getInterfaces;
        _getRoutes = getRoutes;
        _getRoutedDomains = getRoutedDomains;
        _addRoute = addRoute;
        _removeRoute = removeRoute;

        Text = "DyndDns — Поиск доменов";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(880, 540);
        MinimumSize = new Size(660, 420);
        Font = new Font("Segoe UI", 9F);
        ShowInTaskbar = true;

        var termLabel = new Label { Text = "Домен содержит:", Location = new Point(12, 15), AutoSize = true };

        _term = new TextBox { Location = new Point(120, 12), Width = 420 };
        _term.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            RunSearch();
        };

        var searchButton = new Button { Text = "Найти", Location = new Point(550, 11), Width = 90, Height = 26 };
        searchButton.Click += (_, _) => RunSearch();

        var interfaceLabel = new Label { Text = "VPN для добавления:", Location = new Point(12, 51), AutoSize = true };

        _interfaces = new ComboBox
        {
            Location = new Point(150, 48),
            Width = 280,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _interfaces.SelectedIndexChanged += (_, _) => ApplyRowColors();

        var reloadButton = new Button { Text = "Обновить список", Location = new Point(12, 84), Width = 130, Height = 27 };
        reloadButton.Click += (_, _) =>
        {
            LoadInterfaces();
            RefreshRouterState();
            RunSearch();
        };

        var reloadInterfacesButton = new Button { Text = "Обновить VPN", Location = new Point(150, 84), Width = 120, Height = 27 };
        reloadInterfacesButton.Click += (_, _) => LoadInterfaces();

        _autoRefreshCheck = new CheckBox
        {
            Text = "Автообновление",
            Location = new Point(285, 88),
            AutoSize = true,
            Checked = true
        };
        _autoRefreshCheck.CheckedChanged += (_, _) => ApplyAutoRefresh();

        _routedOnlyCheck = new CheckBox
        {
            Text = "Только маршруты на роутере",
            Location = new Point(420, 88),
            AutoSize = true
        };
        _routedOnlyCheck.CheckedChanged += (_, _) =>
        {
            // The router state has to be current for the filter to be trusted.
            RefreshRouterState();
            RunSearch();
        };

        var addButton = new Button { Text = "Добавить выбранные", Location = new Point(660, 47), Width = 208, Height = 27 };
        addButton.Click += (_, _) => AddSelected();

        var removeButton = new Button { Text = "Удалить маршруты", Location = new Point(440, 47), Width = 210, Height = 27 };
        removeButton.Click += (_, _) => RemoveSelected();

        _grid = new DataGridView
        {
            Location = new Point(12, 120),
            Size = new Size(856, 374),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SystemColors.Window
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Domain", HeaderText = "Домен", FillWeight = 45 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hits", HeaderText = "Обращений", FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastSeen", HeaderText = "Последний раз", FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = StateColumn, HeaderText = "Маршрут", FillWeight = 20 });

        _status = new Label
        {
            Location = new Point(12, 505),
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };

        Controls.AddRange(new Control[]
        {
            termLabel, _term, searchButton, interfaceLabel, _interfaces, addButton, removeButton,
            reloadButton, reloadInterfacesButton, _autoRefreshCheck, _routedOnlyCheck, _grid, _status
        });

        AcceptButton = searchButton;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                LoadInterfaces();
                RefreshRouterState();
                RunSearch();
                return;
            }

            // Only from the grid: in the search box Delete must keep editing the text.
            if (e.KeyCode == Keys.Delete && _grid.Focused)
            {
                e.SuppressKeyPress = true;
                RemoveSelected();
            }
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        LoadInterfaces();
        RefreshRouterState();
        RunSearch();

        // Re-reads the list periodically while the checkbox is on.
        _autoRefresh = new System.Windows.Forms.Timer { Interval = 5000 };
        _autoRefresh.Tick += (_, _) => RunSearch();
        ApplyAutoRefresh();
    }

    private void ApplyAutoRefresh()
    {
        if (_autoRefresh is null)
            return;

        if (_autoRefreshCheck.Checked)
            _autoRefresh.Start();
        else
            _autoRefresh.Stop();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _autoRefresh?.Stop();
        _autoRefresh?.Dispose();
        _autoRefresh = null;

        base.OnFormClosed(e);
    }

    private void LoadInterfaces()
    {
        Cursor = Cursors.WaitCursor;

        try
        {
            var interfaces = Task.Run(_getInterfaces).GetAwaiter().GetResult();

            _interfaces.Items.Clear();

            foreach (var item in interfaces)
                _interfaces.Items.Add(item);

            if (_interfaces.Items.Count > 0)
                _interfaces.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось получить VPN-интерфейсы: {ex.Message}", "DyndDns", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void RunSearch()
    {
        try
        {
            var term = _term.Text.Trim();
            var results = _search(term.Length == 0 ? null : term, MaxResults);

            _localRoutes = _getRoutes().ToDictionary(route => route.Domain, route => route.Interface, StringComparer.OrdinalIgnoreCase);

            _grid.Rows.Clear();

            var shown = 0;

            foreach (var stat in results)
            {
                // The checkbox narrows the list down to domains the router already routes.
                if (_routedOnlyCheck.Checked && !_routedDomains.ContainsKey(stat.Domain))
                    continue;

                var index = _grid.Rows.Add(
                    stat.Domain,
                    stat.Hits,
                    stat.LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    string.Empty);

                ApplyState(_grid.Rows[index], stat.Domain);
                shown++;
            }

            var scope = _routedOnlyCheck.Checked ? "С маршрутом на роутере" : "Найдено";

            _status.Text = $"{scope}: {shown} · обновлено {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _status.Text = "Ошибка чтения базы";
            MessageBox.Show(this, $"Не удалось прочитать базу DNS: {ex.Message}", "DyndDns", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Reads the routing state from the router so the grid can show where each domain actually goes.
    /// </summary>
    private void RefreshRouterState()
    {
        Cursor = Cursors.WaitCursor;

        try
        {
            _routedDomains = new Dictionary<string, string>(_getRoutedDomains(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Router routing state lookup failed: {ex.Message}");
            _status.Text = "Не удалось получить маршруты с роутера";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>
    /// Applies routing state that was read on another thread and repaints the grid; used when the
    /// tray side refreshes the state after a synchronization.
    /// </summary>
    public void ApplyRouterState(IReadOnlyDictionary<string, string> routedDomains)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(new Action(() =>
        {
            _routedDomains = new Dictionary<string, string>(routedDomains, StringComparer.OrdinalIgnoreCase);

            // With the router-only filter on the rows have to be re-evaluated as well: a route that has
            // just left the router must leave the list too.
            if (_routedOnlyCheck.Checked)
                RunSearch();
            else
                ApplyRowColors();
        }));
    }

    /// <summary>
    /// Writes the interface the domain is routed through, which is also the VPN it belongs to.
    /// The cell is painted green when that is the VPN selected for new entries and yellow when the
    /// domain goes through another one; without a router route the local binding is shown plainly.
    /// </summary>
    private void ApplyState(DataGridViewRow row, string domain)
    {
        var cell = row.Cells[StateColumn];

        if (!_routedDomains.TryGetValue(domain, out var routerInterface))
        {
            row.DefaultCellStyle.BackColor = _grid.DefaultCellStyle.BackColor;
            cell.Value = _localRoutes.TryGetValue(domain, out var localInterface) ? localInterface : string.Empty;
            return;
        }

        var selected = (_interfaces.SelectedItem as VpnInterfaceInfo)?.Name;

        cell.Value = routerInterface;
        row.DefaultCellStyle.BackColor = string.Equals(routerInterface, selected, StringComparison.OrdinalIgnoreCase)
            ? RoutedViaSelectedColor
            : RoutedViaOtherColor;
    }

    private void ApplyRowColors()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Cells["Domain"].Value is string domain && domain.Length > 0)
                ApplyState(row, domain);
        }
    }

    /// <summary>Domains of the rows selected in the grid.</summary>
    private IEnumerable<string> SelectedDomains() =>
        _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Cells["Domain"].Value as string)
            .Where(domain => !string.IsNullOrEmpty(domain))
            .Select(domain => domain!);

    private void AddSelected()
    {
        if (_interfaces.SelectedItem is not VpnInterfaceInfo target)
        {
            MessageBox.Show(this, "Выберите VPN-интерфейс для добавления.", "DyndDns", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var added = 0;
        var skipped = 0;

        foreach (var domain in SelectedDomains())
        {
            if (_addRoute(domain, target.Name))
                added++;
            else
                skipped++;
        }

        RefreshRouterState();
        RunSearch();
        _status.Text = $"Добавлено: {added}, уже было: {skipped}";
    }

    /// <summary>
    /// Drops the routes of the selected domains: they leave the local list, and the next sync rewrites
    /// the router groups without them.
    /// </summary>
    private void RemoveSelected()
    {
        var removed = new List<string>();
        var missing = 0;

        foreach (var domain in SelectedDomains())
        {
            if (_removeRoute(domain))
                removed.Add(domain);
            else
                missing++;
        }

        RefreshRouterState();

        // The route leaves the router only when the queued sync finishes, so its rows are dropped from
        // the displayed state right away to match the action that was just performed.
        foreach (var domain in removed)
            _routedDomains.Remove(domain);

        RunSearch();
        _status.Text = $"Удалено маршрутов: {removed.Count}, без маршрута: {missing}";
    }
}
