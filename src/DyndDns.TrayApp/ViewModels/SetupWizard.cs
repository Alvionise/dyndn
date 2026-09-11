using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using DyndDns.TrayApp.Views;

namespace DyndDns.TrayApp.ViewModels;

/// <summary>
/// Drives the router setup. Adding a router scans the network, lets the user pick a device from the list
/// (or type an address), then verifies the credentials and stores them as a profile. Editing an existing
/// profile asks for the credentials straight away — the router is already known, so there is nothing to
/// search for.
/// </summary>
internal sealed class SetupWizard
{
    private const string DefaultUsername = "admin";

    private readonly SettingsStore _store;

    public SetupWizard(SettingsStore store) => _store = store;

    /// <summary>
    /// Runs the wizard and returns the stored profile, or <c>null</c> when it was cancelled. Without
    /// <paramref name="profileToUpdate"/> a new router is searched for; with it the credentials of that
    /// profile are edited, and no scan is performed because the router is already known.
    /// </summary>
    public RouterProfile? Run(RouterProfile? profileToUpdate) =>
        profileToUpdate is null ? AddRouter() : EditCredentials(profileToUpdate);

    /// <summary>Scans the network, lets the user pick a device and stores the credentials as a new profile.</summary>
    private RouterProfile? AddRouter()
    {
        List<DiscoveredRouter> routers;
        bool canceled;

        using (var scan = new ScanProgressDialog(new RouterDiscoveryService()))
        {
            scan.ShowDialog();
            routers = scan.Results;
            canceled = scan.Canceled;
        }

        if (canceled)
        {
            Dismiss();
            return null;
        }

        // The list is shown again when the picked address turns out to be configured already, so the user can
        // choose another router right away instead of typing through the credentials first.
        while (true)
        {
            using var selection = new RouterSelectionDialog(routers);

            if (selection.ShowDialog() != DialogResult.OK)
            {
                Dismiss();
                return null;
            }

            var address = RouterAddress.Normalize(selection.Address);

            if (WarnIfAddressIsTaken(address, editing: null))
                continue;

            var name = selection.SelectedRouter is { Name.Length: > 0 } discovered ? discovered.Name : address;

            return PromptForCredentials(name, address, profile: null);
        }
    }

    /// <summary>Updates the profile the user opened; a changed address does not create a second one.</summary>
    private RouterProfile? EditCredentials(RouterProfile profile) =>
        PromptForCredentials(profile.Name, profile.Address, profile);

    /// <param name="profile">
    /// The profile to update, or <c>null</c> to store a new one.
    /// </param>
    private RouterProfile? PromptForCredentials(string name, string address, RouterProfile? profile)
    {
        var username = profile is { Username.Length: > 0 } ? profile.Username : DefaultUsername;
        var password = profile?.Password ?? string.Empty;

        while (true)
        {
            using var dialog = new CredentialsDialog(RouterLabel.Format(name, address), address, username, password);

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                Dismiss();
                return null;
            }

            address = RouterAddress.Normalize(dialog.Address);
            username = dialog.Username;
            password = dialog.Password;

            // The address may also have been corrected in this dialog, so it is checked again here.
            if (WarnIfAddressIsTaken(address, profile))
                continue;

            var valid = RunOnPool(() => KeeneticApiService.ValidateCredentialsAsync(address, username, password));

            if (!valid)
            {
                Dialogs.Warn(null, "Не удалось войти с указанными логином и паролем. Проверьте данные и попробуйте снова.");
                continue;
            }

            // The name of the device is taken from the router itself: a router that does not answer the UPnP
            // scan carries no name, and a profile stored under its address would be listed as a bare IP.
            var deviceName = RunOnPool(() => KeeneticApiService.ReadDeviceNameAsync(address, username, password));

            var target = profile ?? new RouterProfile { Name = name };

            if (deviceName.Length > 0)
                target.Name = deviceName;

            target.Address = address;
            target.Username = username;
            target.Password = password;

            var saved = _store.SaveRouter(target);

            var config = _store.LoadConfig();
            config.SetupDismissed = false;
            _store.SaveConfig(config);

            return saved;
        }
    }

    /// <summary>
    /// Runs a router call on the thread pool and waits for its answer. The wizard is modal and its steps are a
    /// plain loop, so the call itself is moved off the UI thread — a slow or unreachable router would freeze the
    /// dialog for as long as its request timeout lasts — while the answer is still awaited here.
    /// </summary>
    private static T RunOnPool<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();

    /// <summary>
    /// Warns that the address already belongs to a profile and returns <c>true</c>, so the caller asks for
    /// another one. One device is one profile: a second profile for the same router would write the same
    /// groups on it and remove the domains of the first. A profile editing itself is not a conflict.
    /// </summary>
    private bool WarnIfAddressIsTaken(string address, RouterProfile? editing)
    {
        var taken = _store.FindByHost(address, editing?.Id ?? 0);

        if (taken is null)
            return false;

        Dialogs.Warn(null, $"По адресу {address} уже добавлен роутер «{taken.DisplayName}». Настройте его в меню «Роутеры».");

        return true;
    }

    /// <summary>
    /// Remembers a cancelled setup, so the wizard is not offered on every launch. Only relevant while
    /// no profile exists yet: cancelling a later run must not change anything.
    /// </summary>
    private void Dismiss()
    {
        if (_store.GetRouters().Count > 0)
            return;

        var config = _store.LoadConfig();

        if (config.SetupDismissed)
            return;

        config.SetupDismissed = true;
        _store.SaveConfig(config);
    }
}
