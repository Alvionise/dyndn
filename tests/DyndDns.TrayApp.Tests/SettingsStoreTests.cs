using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly AppDatabase _database;
    private readonly SettingsStore _store;

    public SettingsStoreTests()
    {
        _database = _temp.Database;
        _store = _temp.Store;
    }

    [Fact]
    public void LoadConfig_ReturnsDefaultsOnAFreshDatabase()
    {
        var config = _store.LoadConfig();

        Assert.True(config.Monitor.MonitorEnabled);
        Assert.True(config.Monitor.BrowserHistoryEnabled);
        Assert.True(config.Monitor.JournalAutoCleanup);
        Assert.Equal(10000, config.Monitor.JournalMaxRows);
        Assert.False(config.SetupDismissed);

        Assert.Empty(_store.GetRouters());
        Assert.Empty(_store.GetBindings());
    }

    [Fact]
    public void SaveConfig_RoundTripsEverySetting()
    {
        var config = _store.LoadConfig();
        config.Monitor.MonitorEnabled = false;
        config.Monitor.BrowserHistoryEnabled = false;
        config.Monitor.JournalAutoCleanup = false;
        config.Monitor.JournalMaxRows = 2500;
        config.SetupDismissed = true;

        _store.SaveConfig(config);

        var reloaded = _store.LoadConfig();

        Assert.False(reloaded.Monitor.MonitorEnabled);
        Assert.False(reloaded.Monitor.BrowserHistoryEnabled);
        Assert.False(reloaded.Monitor.JournalAutoCleanup);
        Assert.Equal(2500, reloaded.Monitor.JournalMaxRows);
        Assert.True(reloaded.SetupDismissed);

        // The limit is stored as a plain number, independent of the language of the machine.
        using var connection = _database.Open();

        Assert.Equal("0", AppDatabase.ReadSetting(connection, "Monitor.JournalCleanup"));
        Assert.Equal("2500", AppDatabase.ReadSetting(connection, "Monitor.JournalMaxRows"));
    }

    [Fact]
    public void LoadConfig_FallsBackToTheDefaultLimitWhenTheStoredValueIsNotANumber()
    {
        using (var connection = _database.Open())
            AppDatabase.WriteSetting(connection, "Monitor.JournalMaxRows", "many");

        Assert.Equal(10000, _store.LoadConfig().Monitor.JournalMaxRows);
    }

    [Fact]
    public void SaveRouter_ProtectsThePasswordAndKeepsTheProfile()
    {
        var stored = _store.SaveRouter(new RouterProfile
        {
            Name = "Home",
            Address = "192.168.1.1",
            Username = "admin",
            Password = "secret",
            VpnInterface = "L2TP0"
        });

        Assert.NotEqual(0, stored.Id);

        using (var connection = _database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT password FROM routers WHERE id = $id;";
            command.Parameters.AddWithValue("$id", stored.Id);

            var raw = Assert.IsType<string>(command.ExecuteScalar());
            Assert.DoesNotContain("secret", raw, StringComparison.Ordinal);
        }

        var reloaded = _store.GetRouter(stored.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("secret", reloaded!.Password);
        Assert.Equal("L2TP0", reloaded.VpnInterface);
        Assert.Equal("Home (192.168.1.1)", reloaded.DisplayName);
    }

    [Fact]
    public void SaveRouter_UpdatesAnExistingProfileWithoutAddingAnother()
    {
        var stored = _store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1", Password = "secret" });

        stored.Address = "192.168.1.2";
        stored.VpnInterface = "SSTP0";
        _store.SaveRouter(stored);

        var profile = Assert.Single(_store.GetRouters());

        Assert.Equal(stored.Id, profile.Id);
        Assert.Equal("192.168.1.2", profile.Address);
        Assert.Equal("SSTP0", profile.VpnInterface);
        Assert.Equal("secret", profile.Password);
        Assert.Null(_store.GetRouter(999));
    }

    [Fact]
    public void AddRoute_BindsTheSameDomainToSeveralRouters()
    {
        var first = _store.SaveRouter(new RouterProfile { Name = "First", Address = "192.168.1.1" });
        var second = _store.SaveRouter(new RouterProfile { Name = "Second", Address = "192.168.100.1" });

        Assert.True(_store.AddRoute(first.Id, "youtube.com", "L2TP0"));
        Assert.True(_store.AddRoute(second.Id, "youtube.com", "SSTP0"));

        Assert.Equal("L2TP0", Assert.Single(_store.GetRoutes(first.Id)).Interface);

        var bindings = _store.GetBindings();

        Assert.Equal(2, bindings.Count);
        Assert.Equal(["First (192.168.1.1)", "Second (192.168.100.1)"], bindings.Select(binding => binding.RouterName));
        Assert.All(bindings, binding => Assert.Equal("youtube.com", binding.Domain));
    }

    [Fact]
    public void AddRoute_UpdatesTheInterfaceOfAnExistingBindingOnly()
    {
        var profile = _store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1" });

        Assert.True(_store.AddRoute(profile.Id, "a.com", "L2TP0"));

        // The same binding with the same interface is not a change.
        Assert.False(_store.AddRoute(profile.Id, "a.com", "L2TP0"));

        // Another interface is stored on the same row.
        Assert.True(_store.AddRoute(profile.Id, "a.com", "SSTP0"));

        Assert.Equal("SSTP0", Assert.Single(_store.GetRoutes(profile.Id)).Interface);
    }

    [Fact]
    public void RemoveRoute_DropsOnlyTheBindingOfThatRouter()
    {
        var first = _store.SaveRouter(new RouterProfile { Name = "First", Address = "192.168.1.1" });
        var second = _store.SaveRouter(new RouterProfile { Name = "Second", Address = "192.168.100.1" });

        _store.AddRoute(first.Id, "a.com", "L2TP0");
        _store.AddRoute(second.Id, "a.com", "SSTP0");

        Assert.True(_store.RemoveRoute(first.Id, "a.com"));
        Assert.False(_store.RemoveRoute(first.Id, "a.com"));

        Assert.Empty(_store.GetRoutes(first.Id));
        Assert.Single(_store.GetRoutes(second.Id));

        Assert.True(_store.RemoveRoute(second.Id, "a.com"));

        Assert.Empty(_store.GetRoutes(second.Id));
        Assert.Empty(_store.GetBindings());
    }

    [Fact]
    public void DeleteRouter_RemovesItsBindingsAndKeepsTheOtherProfile()
    {
        var first = _store.SaveRouter(new RouterProfile { Name = "First", Address = "192.168.1.1" });
        var second = _store.SaveRouter(new RouterProfile { Name = "Second", Address = "192.168.100.1" });

        _store.AddRoute(first.Id, "a.com", "L2TP0");
        _store.AddRoute(second.Id, "youtube.com", string.Empty);

        _store.DeleteRouter(second.Id);

        var remaining = Assert.Single(_store.GetRouters());

        Assert.Equal(first.Id, remaining.Id);
        Assert.Empty(_store.GetRoutes(second.Id));
        Assert.Single(_store.GetRoutes(first.Id));

        _store.DeleteRouter(first.Id);

        Assert.Empty(_store.GetRouters());
        Assert.Empty(_store.GetBindings());
    }

    [Fact]
    public void RemoveBindingsOfMissingInterfaces_DropsOnlyTheBindingsOfAGoneTunnel()
    {
        var profile = _store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1" });

        _store.AddRoute(profile.Id, "kept.com", "SSTP0");
        _store.AddRoute(profile.Id, "gone.com", "L2TP0");
        _store.AddRoute(profile.Id, "default.com", string.Empty);

        // The router still has SSTP0, so the binding through the deleted L2TP0 goes away; the binding without an
        // interface follows the default one of the profile and stays.
        Assert.Equal(1, _store.RemoveBindingsOfMissingInterfaces(profile.Id, ["SSTP0"]));

        var routes = _store.GetRoutes(profile.Id).ToDictionary(route => route.Domain, route => route.Interface);

        Assert.Equal(2, routes.Count);
        Assert.Equal("SSTP0", routes["kept.com"]);
        Assert.Equal(string.Empty, routes["default.com"]);
    }

    [Fact]
    public void SetRouterName_WritesOneColumnAndKeepsAnEditMadeMeanwhile()
    {
        var profile = _store.SaveRouter(new RouterProfile
        {
            Name = "192.168.1.1",
            Address = "192.168.1.1",
            Username = "admin",
            Password = "secret"
        });

        // The name and the connection are learned from the router while the user may be editing the same profile:
        // each one is written on its own, so the copy the reader started with cannot undo the edit.
        var stale = _store.GetRouter(profile.Id)!;
        stale.Address = "192.168.1.2";
        _store.SaveRouter(stale);

        _store.SetRouterName(profile.Id, "Keenetic Giga SE");
        _store.SetRouterVpnInterface(profile.Id, "SSTP0");

        var stored = _store.GetRouter(profile.Id)!;

        Assert.Equal("Keenetic Giga SE", stored.Name);
        Assert.Equal("SSTP0", stored.VpnInterface);
        Assert.Equal("192.168.1.2", stored.Address);
        Assert.Equal("admin", stored.Username);
        Assert.Equal("secret", stored.Password);
    }

    [Fact]
    public void AddRoute_KeepsOneBindingWhenWritersRace()
    {
        var profile = _store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1" });

        // The window binds a domain while the reconciliation that follows a router read does the same, in another
        // casing and on another thread: writing a binding is a read followed by a write, so it is serialized and
        // exactly one row survives whatever the order is.
        Parallel.For(0, 200, index =>
            _store.AddRoute(profile.Id, index % 2 == 0 ? "Example.com" : "example.com", "L2TP0"));

        Assert.Single(_store.GetBindings());
    }

    [Fact]
    public void GetBindings_NamesTheRouterLikeTheMenusDo()
    {
        var profile = _store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1" });
        _store.AddRoute(profile.Id, "a.com", "L2TP0");

        Assert.Equal("Home (192.168.1.1)", Assert.Single(_store.GetBindings()).RouterName);
    }

    [Fact]
    public void FindByHost_SeesTheSameDeviceInAnotherForm()
    {
        var profile = _store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1" });

        // A scheme or a trailing slash does not make a second router, so the wizard refuses a duplicate.
        var sameDevice = _store.FindByHost("http://192.168.1.1");

        Assert.NotNull(sameDevice);
        Assert.Equal(profile.Id, sameDevice!.Id);

        // The profile being edited is not its own conflict, and another device is not one either.
        Assert.Null(_store.FindByHost("192.168.1.1", excludedRouterId: profile.Id));
        Assert.Null(_store.FindByHost("192.168.1.2"));
        Assert.Null(_store.FindByHost(string.Empty));
    }

    [Fact]
    public void AddRoute_DoesNotBindTheSameDomainTwiceInAnotherCase()
    {
        var profile = _store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1" });

        Assert.True(_store.AddRoute(profile.Id, "Example.com", "L2TP0"));

        // Another casing is the same binding, so it updates that row instead of adding one: the stored
        // form keeps the casing it was first written with.
        Assert.True(_store.AddRoute(profile.Id, "example.com", "SSTP0"));

        var route = Assert.Single(_store.GetRoutes(profile.Id));

        Assert.Equal("Example.com", route.Domain);
        Assert.Equal("SSTP0", route.Interface);

        Assert.True(_store.RemoveRoute(profile.Id, "EXAMPLE.COM"));
        Assert.Empty(_store.GetRoutes(profile.Id));
    }

    public void Dispose() => _temp.Dispose();
}
