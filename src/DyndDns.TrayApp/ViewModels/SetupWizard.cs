using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using DyndDns.TrayApp.Views;

namespace DyndDns.TrayApp.ViewModels;

/// <summary>
/// Drives the first-run (and manually re-runnable) router setup: scan the network, let the user
/// pick a device when several are found, then verify and persist the credentials.
/// </summary>
internal sealed class SetupWizard
{
    private const string DefaultUsername = "admin";

    private readonly ConfigService _configService;

    public SetupWizard(ConfigService configService) => _configService = configService;

    /// <summary>
    /// Runs the wizard. Returns <c>true</c> when valid credentials were saved to the configuration.
    /// </summary>
    public bool Run(AppConfig config)
    {
        using var scan = new ScanProgressDialog(new RouterDiscoveryService());
        scan.ShowDialog();

        if (scan.Canceled)
        {
            Dismiss(config);
            return false;
        }

        if (!TryPickAddress(config, scan.Results, out var address))
            return false;

        return PromptForCredentials(config, address);
    }

    private bool TryPickAddress(AppConfig config, IReadOnlyList<DiscoveredRouter> routers, out string address)
    {
        address = string.Empty;

        if (routers.Count == 0)
        {
            var openConfig = MessageBox.Show(
                "Роутер Keenetic не найден.\n\nОткрыть dyndns.json для ручной настройки?",
                "DyndDns",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (openConfig == DialogResult.Yes)
                OpenConfigFile();

            Dismiss(config);
            return false;
        }

        if (routers.Count == 1)
        {
            address = routers[0].Address;
            return true;
        }

        using var selection = new RouterSelectionDialog(routers);

        if (selection.ShowDialog() != DialogResult.OK)
        {
            Dismiss(config);
            return false;
        }

        address = selection.Address;
        return true;
    }

    private bool PromptForCredentials(AppConfig config, string address)
    {
        var username = string.IsNullOrWhiteSpace(config.Router.Username) ? DefaultUsername : config.Router.Username;
        var password = config.Router.Password;

        while (true)
        {
            using var dialog = new CredentialsDialog(address, username, password);

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                Dismiss(config);
                return false;
            }

            address = dialog.Address;
            username = dialog.Username;
            password = dialog.Password;

            // Runs on the thread pool so a slow or unreachable router never blocks the UI thread
            // and the continuation never needs a UI synchronization context.
            var valid = Task.Run(() => KeeneticApiService.ValidateCredentialsAsync(address, username, password))
                .GetAwaiter().GetResult();

            if (valid)
            {
                config.Router.Address = RouterAddress.Normalize(address);
                config.Router.Username = username;
                config.Router.Password = password;
                config.SetupDismissed = false;
                _configService.SaveConfig(config);
                return true;
            }

            MessageBox.Show(
                "Не удалось войти с указанными логином и паролем. Проверьте данные и попробуйте снова.",
                "DyndDns",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void Dismiss(AppConfig config)
    {
        // Only an unconfigured router needs the cancellation remembered; once credentials exist,
        // a cancelled re-run must not leave a stale flag that would suppress a future wizard.
        if (config.SetupDismissed || ConfigService.IsRouterConfigured(config))
            return;

        config.SetupDismissed = true;
        _configService.SaveConfig(config);
    }

    private void OpenConfigFile()
    {
        var path = _configService.ConfigPath;

        if (!File.Exists(path))
            return;

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
