using System.Diagnostics;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;

namespace DyndDns.TrayApp.Views;

/// <summary>
/// Modal progress window that runs <see cref="RouterDiscoveryService.DiscoverAsync"/> while it is
/// on screen. The bar is driven by the real scan counters, and every UI touch is marshalled back
/// explicitly so the dialog works whether or not a WindowsForms synchronization context is set.
/// </summary>
internal sealed class ScanProgressDialog : SetupDialogBase
{
    private readonly RouterDiscoveryService _discovery;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Button _cancelButton;

    public ScanProgressDialog(RouterDiscoveryService discovery)
        : base("Поиск роутера", new Size(440, 160))
    {
        _discovery = discovery;

        // No window close button: the dialog closes itself once the scan settles, which removes
        // the race between an abrupt user close and the background scan finishing.
        ControlBox = false;

        _statusLabel = CreateLabel("Поиск роутера Keenetic в сети...");
        AddContent(_statusLabel);

        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100
        };
        AddContent(_progressBar);

        _cancelButton = CreateButton("Отмена (Esc)", 120);
        _cancelButton.Click += (_, _) =>
        {
            _cancelButton.Enabled = false;
            _statusLabel.Text = "Отмена...";
            _cancellation.Cancel();
        };

        ButtonBar.Controls.Add(_cancelButton);
        CancelButton = _cancelButton;

        Shown += (_, _) => _ = RunDiscoveryAsync();

        ApplyContentMinimumSize();
    }

    public List<DiscoveredRouter> Results { get; private set; } = [];

    public bool Canceled { get; private set; }

    private async Task RunDiscoveryAsync()
    {
        var progress = new Progress<DiscoveryProgress>(report => Post(() => ApplyProgress(report)));

        try
        {
            var routers = await _discovery.DiscoverAsync(progress, _cancellation.Token).ConfigureAwait(false);
            Post(() => Complete(routers, canceled: false, error: null));
        }
        catch (OperationCanceledException)
        {
            Post(() => Complete(null, canceled: true, error: null));
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Router discovery failed: {ex}");
            Post(() => Complete(null, canceled: true, error: ex.Message));
        }
    }

    private void ApplyProgress(DiscoveryProgress report)
    {
        if (report.Total > 0)
        {
            _progressBar.Maximum = report.Total;
            _progressBar.Value = Math.Clamp(report.Scanned, 0, report.Total);
        }

        _statusLabel.Text = report.Message;
    }

    /// <summary>Queues a report of the scan for this dialog's thread; see <see cref="ViewDispatch.Post"/>.</summary>
    private void Post(Action action) => ViewDispatch.Post(this, action);

    private void Complete(List<DiscoveredRouter>? routers, bool canceled, string? error)
    {
        if (routers is not null)
            Results = routers;

        Canceled = canceled;

        if (error is not null)
            Dialogs.Warn(this, $"Не удалось выполнить поиск: {error}");

        DialogResult = Canceled ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cancellation.Dispose();

        base.Dispose(disposing);
    }
}
