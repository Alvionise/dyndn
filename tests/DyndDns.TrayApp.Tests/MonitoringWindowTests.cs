using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Views;
using Xunit;

namespace DyndDns.TrayApp.Tests;

/// <summary>
/// The window builds its own layout from code, so constructing it catches control and layout mistakes
/// (overlapping panels, wrong docking) without needing a visible desktop.
/// </summary>
public class MonitoringWindowTests
{
    [Fact]
    public void Constructor_BuildsTheLayoutWithoutData()
    {
        using var window = CreateWindow();

        Assert.Equal("DyndDns — Поиск и мониторинг", window.Text);

        // The table and the binding panel are the two things a user interacts with.
        Assert.NotEmpty(window.Controls);
    }

    [Fact]
    public void ReloadData_IsSafeBeforeTheWindowIsShown()
    {
        using var window = CreateWindow();

        // No handle yet, so the call must simply do nothing instead of throwing.
        window.ReloadData();

        Assert.False(window.IsDisposed);
    }

    [Fact]
    public void Constructor_OpensOnAllRoutersAndKeepsTheUnboundDomainsAsAChoice()
    {
        using var window = CreateWindow();

        var filter = Descendants(window).OfType<ComboBox>()
            .Single(combo => combo.Items.Count > 0 && combo.GetItemText(combo.Items[0]) == "Все роутеры");

        // The window shows what the routers have, bound and not bound alike, so the rules on them are not hidden
        // by the filter; the domains without a binding stay one choice away.
        Assert.Equal(0, filter.SelectedIndex);
        Assert.Equal("Все роутеры", filter.GetItemText(filter.SelectedItem));
        Assert.Equal("Не привязанные ни к одному роутеру", filter.GetItemText(filter.Items[1]));
    }

    [Fact]
    public void Search_RunsWhileTheTermIsTyped()
    {
        var host = new FakeHost();

        using var window = CreateWindow(host);

        var term = Descendants(window)
            .OfType<TextBox>()
            .Single(box => box.PlaceholderText != "example.com");

        term.Text = "a.com";

        // The list follows the typing, which is why the button that used to start the search is gone.
        Assert.Contains("a.com", host.Terms);
        Assert.DoesNotContain(Descendants(window).OfType<Button>(), button => button.Text == "Найти (Enter)");
    }

    [Fact]
    public void RouterFilter_PointsTheBindingPanelAtTheChosenRouter()
    {
        var host = new FakeHost
        {
            Routers =
            [
                new RouterProfile { Id = 1, Name = "Home", Address = "192.168.1.1", VpnInterface = "SSTP0" },
                new RouterProfile { Id = 2, Name = "Office", Address = "192.168.2.1", VpnInterface = "SSTP0" }
            ]
        };

        using var window = CreateWindow(host);
        window.LoadRouters();

        var filter = Descendants(window).OfType<ComboBox>()
            .Single(combo => combo.Items.Count > 0 && combo.GetItemText(combo.Items[0]) == "Все роутеры");
        var target = Descendants(window).OfType<ComboBox>()
            .Single(combo => combo.Items.Count > 0 && combo.GetItemText(combo.Items[0]) == "Home (192.168.1.1)");

        // The panel starts on the first profile.
        Assert.Equal(0, target.SelectedIndex);

        // Choosing another router in the filter points the panel at it: the table shows that router, and the next
        // domain is bound to it without setting the panel to the same router by hand.
        filter.SelectedIndex = 3;
        window.ApplyRouterFilter();

        Assert.Equal("Office (192.168.2.1)", target.GetItemText(target.SelectedItem));
    }

    [Fact]
    public void InterfaceFilter_IsSwitchedOffUntilARouterIsChosen()
    {
        var host = new FakeHost
        {
            Routers = [new RouterProfile { Id = 1, Name = "Home", Address = "192.168.1.1", VpnInterface = "SSTP0" }]
        };

        using var window = CreateWindow(host);
        window.LoadRouters();

        var vpn = Descendants(window).OfType<ComboBox>()
            .Single(combo => combo.GetItemText(combo.Items[0]) == "Все VPN");
        var routers = Descendants(window).OfType<ComboBox>()
            .Single(combo => combo.GetItemText(combo.Items[0]) == "Все роутеры");

        // The window opens on «Все роутеры», so there is no interface of a router to filter by.
        Assert.False(vpn.Enabled);

        // A concrete router turns the list on.
        routers.SelectedIndex = 2;
        window.ApplyRouterFilter();

        Assert.True(vpn.Enabled);
    }

    [Fact]
    public void Constructor_UsesDropDownsThatLeaveTheWheelAloneWhileTheyAreNotFocused()
    {
        using var window = CreateWindow();

        // The wheel goes to the control under the pointer, so scrolling over the window used to change the chosen
        // router without a click; every drop-down of the window ignores it until the user works with the list.
        Assert.All(
            Descendants(window).OfType<ComboBox>(),
            combo => Assert.IsType<MonitoringWindow.FilterComboBox>(combo));
    }

    [Fact]
    public void Constructor_LeavesTheSortingOfTheTableToTheWindow()
    {
        using var window = CreateWindow();
        var grid = Descendants(window).OfType<DataGridView>().Single();

        // The rows are ordered while they are built, so the grid must not sort on its own: it would reorder them
        // behind the window's back and keep the header of the sorted column highlighted in blue.
        Assert.All(
            grid.Columns.Cast<DataGridViewColumn>(),
            column => Assert.Equal(DataGridViewColumnSortMode.Programmatic, column.SortMode));
        Assert.Null(grid.SortedColumn);
    }

    [Fact]
    public void Constructor_KeepsTheHeaderOfTheCurrentColumnFromLookingSelected()
    {
        using var window = CreateWindow();
        var grid = Descendants(window).OfType<DataGridView>().Single();

        // The grid paints the header of the column with the current cell in the selection colours, so "Домен"
        // (the first column) looked like the sorted one whatever was actually clicked; the header now keeps its
        // normal colours, which needs the themed header drawing to be off.
        Assert.False(grid.EnableHeadersVisualStyles);
        Assert.Equal(
            grid.ColumnHeadersDefaultCellStyle.BackColor,
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor);
    }

    [Fact]
    public void OrderRows_SortsByTheClickedColumnAndTurnsTheOrderAround()
    {
        var newer = new MonitoringWindow.TableRow(
            new DomainStat("a.com", 9, new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc)),
            null,
            "SSTP0",
            string.Empty,
            false);
        var older = new MonitoringWindow.TableRow(
            new DomainStat("b.com", 1, new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc)),
            null,
            "SSTP0",
            string.Empty,
            false);

        // Until a column is clicked the newest lookup comes first.
        Assert.Equal(["a.com", "b.com"], Names(MonitoringWindow.OrderRows([older, newer], null, true)));

        // A click orders by that column, and the same column again turns the order around.
        Assert.Equal(["b.com", "a.com"], Names(MonitoringWindow.OrderRows([older, newer], "Hits", true)));
        Assert.Equal(["a.com", "b.com"], Names(MonitoringWindow.OrderRows([older, newer], "Hits", false)));
        Assert.Equal(["a.com", "b.com"], Names(MonitoringWindow.OrderRows([older, newer], "Domain", true)));
    }

    [Fact]
    public void RouterFilter_IgnoresAChangeThatTheWindowMadeItself()
    {
        using var window = CreateWindow();

        var filter = Descendants(window).OfType<ComboBox>()
            .Single(combo => combo.Items.Count > 0 && combo.GetItemText(combo.Items[0]) == "Все роутеры");
        var switches = Descendants(window).OfType<CheckBox>().ToList();

        // The index moves while the profiles are loaded and for a wheel over a list the user is not working with;
        // neither is a choice, so the switches stay as they are until the user really picks a filter.
        filter.SelectedIndex = 1;

        Assert.Contains(switches, check => check.Text == "Только привязанные" && check.Enabled);
    }

    [Fact]
    public void RouterFilter_DisablesTheBindingSwitchWhenTheUnboundFilterIsChosen()
    {
        using var window = CreateWindow();

        var filter = Descendants(window).OfType<ComboBox>()
            .Single(combo => combo.Items.Count > 0 && combo.GetItemText(combo.Items[0]) == "Все роутеры");
        var switches = Descendants(window).OfType<CheckBox>().ToList();

        // With the default filter the switch is usable; the journal one always is.
        Assert.Contains(switches, check => check.Text == "Только привязанные" && check.Enabled);
        Assert.Contains(switches, check => check.Text == "Автообновление" && check.Enabled);

        // Choosing the domains without a binding disables it: it describes bound domains and would contradict
        // the filter.
        filter.SelectedIndex = 1;
        window.ApplyRouterFilter();

        Assert.Contains(switches, check => check.Text == "Только привязанные" && !check.Enabled);
    }

    [Fact]
    public void Constructor_KeepsOnlyTheActionsThatAreNotAutomatic()
    {
        using var window = CreateWindow();

        // The routers are read when the window opens and after every change of a profile, and the bindings are
        // pushed after every bind and unbind, so the buttons that repeated those steps only crowded the window.
        // Detaching has no button of its own either: it lives in the menu of the table and on the Delete key.
        // The bin carries its meaning in the accessible name, because it shows a picture.
        var buttons = Descendants(window)
            .OfType<Button>()
            .Select(button => button.AccessibleName ?? button.Text)
            .ToList();

        Assert.Equal(["Очистить не привязанные", "Привязать (Enter)"], buttons);
        Assert.DoesNotContain(Descendants(window).OfType<CheckBox>(), check => check.Text == "Только на роутере");
    }

    [Fact]
    public void InterfaceFilter_TreatsTheAllVpnChoiceAsNoFilter()
    {
        // «Все VPN» carries an empty value; read as an interface name it matched no binding and hid every row.
        Assert.Null(MonitoringWindow.InterfaceFilterValue(string.Empty));
        Assert.Null(MonitoringWindow.InterfaceFilterValue(null));
        Assert.Equal("SSTP0", MonitoringWindow.InterfaceFilterValue("SSTP0"));
    }

    [Fact]
    public void InterfaceChoiceIndex_FindsTheSameConnectionSpeltDifferently()
    {
        using var filter = new ComboBox();
        filter.Items.Add(new MonitoringWindow.InterfaceChoice(string.Empty, "Все VPN"));
        filter.Items.Add(new MonitoringWindow.InterfaceChoice("SSTP0", "SSTP0"));

        // The second spelling would become another option, which is how one connection appeared in the list twice.
        Assert.Equal(1, MonitoringWindow.IndexOfInterfaceChoice(filter, "sstp0"));
        Assert.Equal(0, MonitoringWindow.IndexOfInterfaceChoice(filter, string.Empty));
        Assert.Equal(-1, MonitoringWindow.IndexOfInterfaceChoice(filter, "SSTP1"));
    }

    [Fact]
    public void InterfaceChoices_OfferOneEntryPerConnectionWithItsName()
    {
        // The interface of the profile and the connection the router reports are the same tunnel, so the list
        // carries it once; what the connection is called on the router stands in front of its interface.
        var reported = new List<VpnInterfaceInfo>
        {
            new("sstp0", "SSTP", "Латвия", true),
            new("SSTP1", "SSTP", string.Empty, false)
        };

        var choices = MonitoringWindow.InterfaceChoices("SSTP0", reported);

        Assert.Equal(["SSTP0", "SSTP1"], choices.Select(choice => choice.Value));
        Assert.Equal(["Латвия (SSTP0)", "SSTP1"], choices.Select(choice => choice.Name));

        // A connection the router did not report keeps the name of its interface, and there is no entry for
        // "nothing": the interface of the profile is the same connection under the label it already has.
        Assert.Equal(["L2TP0"], MonitoringWindow.InterfaceChoices("L2TP0", []).Select(choice => choice.Name));
        Assert.Empty(MonitoringWindow.InterfaceChoices(null, []));
    }

    [Fact]
    public void AddInterfaceChoices_RefreshesTheLabelOfAConnectionTheRouterNamesLater()
    {
        var combo = new ComboBox();

        // The interface of the profile is offered before the router is asked, so it arrives without a name.
        MonitoringWindow.AddInterfaceChoices(combo, MonitoringWindow.InterfaceChoices("SSTP0", []));

        Assert.Equal(["SSTP0"], combo.Items.Cast<MonitoringWindow.InterfaceChoice>().Select(choice => choice.Name));

        // The answer carries the name: the entry already in the list is relabelled, not listed twice.
        MonitoringWindow.AddInterfaceChoices(
            combo,
            MonitoringWindow.InterfaceChoices("SSTP0", [new VpnInterfaceInfo("SSTP0", "SSTP", "Работа", true)]));

        Assert.Equal(["Работа (SSTP0)"], combo.Items.Cast<MonitoringWindow.InterfaceChoice>().Select(choice => choice.Name));
    }

    [Fact]
    public void TableDomains_ShowsBoundDomainsTheJournalDoesNotKnow()
    {
        var stats = new[] { new DomainStat("seen.com", 3, new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc)) };
        var bindings = new[]
        {
            new DomainBinding("seen.com", 1, "Home (192.168.1.1)", "SSTP0"),
            new DomainBinding("added.com", 1, "Home (192.168.1.1)", string.Empty)
        };

        var domains = MonitoringWindow.TableDomains(stats, bindings, term: null).ToList();

        // A domain bound by hand has no journal entry: it is shown without hits or a look-up time, after the
        // journal rows, so it can be found by the filters and its state on the router is visible.
        Assert.Equal(["seen.com", "added.com"], domains.Select(stat => stat.Domain));
        Assert.Equal(0, domains[1].Hits);
        Assert.Equal(DateTime.MinValue, domains[1].LastSeen);

        // The journal rows arrive already narrowed by the search; the term narrows the added domains.
        Assert.Equal(["added.com"], MonitoringWindow.TableDomains([], bindings, "added").Select(stat => stat.Domain));
        Assert.Empty(MonitoringWindow.TableDomains([], bindings, "nothing"));
    }

    [Fact]
    public void Constructor_ShowsTheMenuOfTheTableItself()
    {
        using var window = CreateWindow();
        var grid = Descendants(window).OfType<DataGridView>().Single();

        // The window opens the menu on the right click — the first one on a window that is not active yet would
        // otherwise be spent on activating it — so the grid must not carry a menu that would open it a second time.
        Assert.Null(grid.ContextMenuStrip);
    }

    [Fact]
    public void RowMenu_OffersBindingTheSelectedRowsAndUnbindingThem()
    {
        using var window = CreateWindow();
        var grid = Descendants(window).OfType<DataGridView>().Single();
        using var menu = new ContextMenuStrip();

        // Nothing is selected yet, so the menu says so instead of offering an action that cannot run.
        window.FillRowMenu(menu);

        var placeholder = Assert.Single(menu.Items.Cast<ToolStripItem>());
        Assert.Equal("Строки не выбраны", placeholder.Text);
        Assert.False(placeholder.Enabled);

        // A selected row turns the menu into the actions of the panel; the entries of the routers come from the
        // window, so without a profile there is nothing to bind to yet.
        grid.Rows.Add("a.com", string.Empty, string.Empty, 0, "—", string.Empty);
        grid.Rows[0].Selected = true;

        window.FillRowMenu(menu);

        var items = menu.Items.Cast<ToolStripItem>().ToList();
        Assert.StartsWith("Привязать выбранные (1)", items[0].Text, StringComparison.Ordinal);
        Assert.False(items[0].Enabled);
        Assert.Contains(items, item => item.Text!.StartsWith("Отвязать выбранные (1)", StringComparison.Ordinal));

        // The connections are a plain list inside the item, not a submenu that has to be opened by hovering first.
        var bind = Assert.IsType<ToolStripMenuItem>(items[0]);
        Assert.Equal("Роутеров нет", Assert.Single(bind.DropDownItems.Cast<ToolStripItem>()).Text);
    }

    [Fact]
    public void Constructor_OffersClearingTheJournalOfUnboundEntries()
    {
        using var window = CreateWindow();

        // The journal fills up with every lookup, so the window offers dropping the domains nothing is routed to.
        // The action sits in the row of the filters as a bin, next to the list it belongs to.
        var bin = Descendants(window)
            .OfType<Button>()
            .Single(button => button.AccessibleName == "Очистить не привязанные");

        Assert.NotNull(bin.Image);
        Assert.Equal("ClearUnbound", bin.Name);
    }

    [Fact]
    public void Constructor_LaysTheControlsOutWithGridsInsteadOfWrappingRows()
    {
        using var window = CreateWindow();

        // A wrapping flow panel hides its last controls when the row cannot grow; every row is a grid.
        Assert.DoesNotContain(Descendants(window), control => control is FlowLayoutPanel { WrapContents: true });
        Assert.Contains(Descendants(window), control => control is TableLayoutPanel);
    }

    private static IEnumerable<string> Names(IEnumerable<MonitoringWindow.TableRow> rows) =>
        rows.Select(row => row.Stat.Domain);

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => Descendants(child).Prepend(child));

    private static MonitoringWindow CreateWindow(FakeHost? host = null) => new(host ?? new FakeHost());

    /// <summary>The window talks to the application only through this interface, so a test needs no tray.</summary>
    private sealed class FakeHost : IMonitoringWindowHost
    {
        /// <summary>Profiles the window sees; a test sets them when it needs routers in the drop-downs.</summary>
        public IReadOnlyList<RouterProfile> Routers { get; set; } = [];

        /// <summary>Terms the window asked about; a test checks that typing reaches the journal.</summary>
        public List<string?> Terms { get; } = [];

        public IReadOnlyList<DomainStat> SearchDomains(string? term, int limit)
        {
            Terms.Add(term);
            return [];
        }

        public IReadOnlyList<RouterProfile> GetRouters() => Routers;

        public IReadOnlyList<DomainBinding> GetBindings() => [];

        public IReadOnlyList<VpnInterfaceInfo> GetVpnInterfaces(int routerId) => [];

        public IReadOnlyDictionary<BindingKey, string> GetRouterState() => new Dictionary<BindingKey, string>();

        public int Bind(int routerId, IReadOnlyList<string> domains, string interfaceName) => 0;

        public int Unbind(IReadOnlyList<BindingKey> bindings) => 0;

        public int ClearUnboundDomains() => 0;
    }
}
