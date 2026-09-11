using System.Diagnostics;
using System.IO;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;

namespace DyndDns.TrayApp.Views;

/// <summary>
/// Edits the settings that are not tied to a router: the DNS monitor, the browser history import and
/// whether Windows starts the app after signing in. Everything router specific lives in a profile and is
/// edited in the monitoring window or in the tray menu.
/// </summary>
internal sealed class SettingsDialog : SetupDialogBase
{
    // The limit counts journal rows, so a handful of them makes no sense and a million is already far beyond
    // what the search window can show.
    private const int JournalMinRows = 100;
    private const int JournalMaxRowsLimit = 1_000_000;

    /// <summary>Width the explanation may take, i.e. the width of the column of the settings below the caption.</summary>
    private const int NoteWidth = 440;

    // Assigned while the dialog builds its groups, i.e. still from the constructor.
    private CheckBox _monitorEnabled = null!;
    private CheckBox _browserHistory = null!;
    private CheckBox _journalCleanup = null!;
    private NumericUpDown _journalMaxRows = null!;
    private CheckBox _startWithWindows = null!;

    public SettingsDialog(AppConfig config, string databasePath)
        : base("Настройки", new Size(560, 500))
    {
        AddContent(BuildDatabaseRow(databasePath));
        AddContent(BuildMonitorGroup(config));
        AddContent(BuildStartupGroup());

        var okButton = CreateButton("Сохранить (Enter)", 145);
        okButton.DialogResult = DialogResult.OK;

        var cancelButton = CreateButton("Отмена (Esc)", 115);
        cancelButton.DialogResult = DialogResult.Cancel;

        ButtonBar.Controls.AddRange([okButton, cancelButton]);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        ApplyContentMinimumSize();
    }

    public bool MonitorEnabled => _monitorEnabled.Checked;

    public bool BrowserHistoryEnabled => _browserHistory.Checked;

    /// <summary>
    /// Whether the import can be used at all: it is a second source of the same journal, so it follows the
    /// recording switch and is shown as unavailable while that is off.
    /// </summary>
    public bool CanImportBrowserHistory => _browserHistory.Enabled;

    public bool JournalAutoCleanup => _journalCleanup.Checked;

    public bool StartWithWindows => _startWithWindows.Checked;

    /// <summary>Copies the edited values into the configuration that is then stored.</summary>
    public void ApplyTo(AppConfig config)
    {
        config.Monitor.MonitorEnabled = _monitorEnabled.Checked;
        config.Monitor.BrowserHistoryEnabled = _browserHistory.Checked;
        config.Monitor.JournalAutoCleanup = _journalCleanup.Checked;
        config.Monitor.JournalMaxRows = (int)_journalMaxRows.Value;
    }

    private static Control BuildDatabaseRow(string databasePath)
    {
        var row = DialogLayout.Table(3);

        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        row.Controls.Add(new Label { Text = "База данных:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);

        row.Controls.Add(new Label
        {
            Text = databasePath,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 0);

        var openFolder = CreateButton("Открыть папку", 120);
        openFolder.Click += (_, _) => OpenDataFolder(databasePath);
        row.Controls.Add(openFolder, 2, 0);

        return row;
    }

    private Control BuildMonitorGroup(AppConfig config)
    {
        var group = DialogLayout.Group("Мониторинг DNS");

        _monitorEnabled = new CheckBox
        {
            Text = "Записывать DNS-запросы, пока приложение запущено",
            AutoSize = true,
            Checked = config.Monitor.MonitorEnabled
        };

        _browserHistory = new CheckBox
        {
            // The import is the second source of the same journal, so it belongs to the recording and has no
            // effect while that is off. The text says so, because a switch that is unavailable explains nothing
            // on its own, and the master switch takes it along on every change.
            Text = "Импортировать посещённые сайты из истории браузеров (пока идёт запись)",
            AutoSize = true,
            Checked = config.Monitor.BrowserHistoryEnabled,
            Enabled = config.Monitor.MonitorEnabled
        };

        _monitorEnabled.CheckedChanged += (_, _) => _browserHistory.Enabled = _monitorEnabled.Checked;

        _journalCleanup = new CheckBox
        {
            Text = "Автоматически удалять из журнала неиспользуемые имена",
            AutoSize = true,
            Checked = config.Monitor.JournalAutoCleanup
        };

        var journalRow = BuildJournalRow(config);

        _journalMaxRows.Enabled = _journalCleanup.Checked;
        _journalCleanup.CheckedChanged += (_, _) => _journalMaxRows.Enabled = _journalCleanup.Checked;

        // The note is a line of the group of its own: the stacked panel spaces the lines and keeps the padding
        // of the group box, while the same label inside the row of the fields sat right on the bottom border.
        group.Controls.Add(DialogLayout.Stack(
        [
            _monitorEnabled,
            _browserHistory,
            _journalCleanup,
            journalRow,
            BuildJournalNote()
        ]));

        return group;
    }

    /// <summary>Row with the limit itself: the label and the count, the explanation follows below it.</summary>
    private Control BuildJournalRow(AppConfig config)
    {
        var row = DialogLayout.Table(2);

        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _journalMaxRows = new NumericUpDown
        {
            Minimum = JournalMinRows,
            Maximum = JournalMaxRowsLimit,
            Increment = 500,
            ThousandsSeparator = true,
            Width = 110,
            Anchor = AnchorStyles.Left,
            Value = Math.Clamp(config.Monitor.JournalMaxRows, JournalMinRows, JournalMaxRowsLimit)
        };

        row.Controls.Add(new Label
        {
            Text = "Хранить записей:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0)
        }, 0, 0);

        row.Controls.Add(_journalMaxRows, 1, 0);

        return row;
    }

    /// <summary>
    /// The explanation below the limit. The text stays short enough for one line, and <see cref="Control.MaximumSize"/>
    /// makes the wrapping predictable: a label that is wider than the column wraps only while it is laid out, i.e.
    /// after the group has already been sized for a single line, and the second line then lands on the border.
    /// </summary>
    private static Label BuildJournalNote() => new()
    {
        Text = "Старые неиспользуемые записи удаляются, привязанные домены остаются.",
        AutoSize = true,
        MaximumSize = new Size(NoteWidth, 0),
        ForeColor = SystemColors.GrayText
    };

    private Control BuildStartupGroup()
    {
        var group = DialogLayout.Group("Запуск с Windows");

        _startWithWindows = new CheckBox
        {
            Text = "Запускать при входе в систему",
            AutoSize = true,
            Checked = StartupRegistration.IsEnabled()
        };

        var hint = new Label
        {
            Text = "Приложению нужны права администратора, поэтому Windows спросит подтверждение при входе в систему.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = SystemColors.GrayText
        };

        group.Controls.Add(DialogLayout.Stack([_startWithWindows, hint]));
        return group;
    }

    private static void OpenDataFolder(string databasePath)
    {
        var folder = Path.GetDirectoryName(databasePath);

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return;

        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
