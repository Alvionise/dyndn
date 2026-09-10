using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
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
        : base("DyndDns — Поиск роутера", new Size(420, 125))
    {
        _discovery = discovery;

        // No window close button: the dialog closes itself once the scan settles, which removes
        // the race between an abrupt user close and the background scan finishing.
        ControlBox = false;

        _statusLabel = CreateLabel("Поиск роутера Keenetic в сети...", new Point(15, 18));

        _progressBar = new ProgressBar
        {
            Location = new Point(15, 48),
            Width = 390,
            Height = 18,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100
        };

        _cancelButton = CreateButton("Отмена", new Point(305, 78));
        _cancelButton.Click += (_, _) =>
        {
            _cancelButton.Enabled = false;
            _statusLabel.Text = "Отмена...";
            _cancellation.Cancel();
        };

        Controls.AddRange(new Control[] { _statusLabel, _progressBar, _cancelButton });

        Shown += (_, _) => _ = RunDiscoveryAsync();
    }

    public List<DiscoveredRouter> Results { get; private set; } = new();

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

    private void Post(Action action)
    {
        try
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
                BeginInvoke(action);
            else
                action();
        }
        catch (ObjectDisposedException)
        {
            // The dialog closed while a background report was in flight; nothing left to update.
        }
    }

    private void Complete(List<DiscoveredRouter>? routers, bool canceled, string? error)
    {
        if (routers is not null)
            Results = routers;

        Canceled = canceled;

        if (error is not null)
            MessageBox.Show(this, $"Не удалось выполнить поиск: {error}", "DyndDns", MessageBoxButtons.OK, MessageBoxIcon.Warning);

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
