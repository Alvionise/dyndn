using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using DyndDns.TrayApp.ViewModels;
using Xunit;

namespace DyndDns.TrayApp.Tests;

/// <summary>
/// The reader keeps the routing state of the routers and aligns the stored bindings with it. The routers are
/// the source of truth, so most of these tests are about what happens to a local binding when the router
/// reports something else.
/// </summary>
public class RouterStateReaderTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly SettingsStore _store;
    private readonly RouterProfile _profile;

    private bool _syncBusy;

    public RouterStateReaderTests()
    {
        _store = _temp.Store;
        _profile = _store.SaveRouter(new RouterProfile { Name = "Home", Address = "192.168.1.1", VpnInterface = "SSTP0" });
    }

    [Fact]
    public void Read_AdoptsTheDomainsTheRouterKnows()
    {
        var reader = CreateReader(groups: [new RouterRouteGroup("dyndns-SSTP0", "SSTP0", ["a.com"])]);

        var state = reader.Read();

        Assert.Equal("SSTP0", state[new BindingKey(_profile.Id, "a.com")]);
        Assert.Equal("a.com", Assert.Single(_store.GetRoutes(_profile.Id)).Domain);
    }

    [Fact]
    public void Read_KeepsLocalBindingsTheRouterDoesNotKnowYet()
    {
        _store.AddRoute(_profile.Id, "waiting.com", string.Empty);

        var reader = CreateReader(groups: [new RouterRouteGroup("dyndns-SSTP0", "SSTP0", ["a.com"])]);

        reader.Read();

        // The local entry may simply be waiting for its first sync, so it is never dropped.
        var routes = _store.GetRoutes(_profile.Id).ToDictionary(route => route.Domain, route => route.Interface);
        Assert.Equal(2, routes.Count);
        Assert.Equal(string.Empty, routes["waiting.com"]);
    }

    [Fact]
    public void Read_TakesTheInterfaceTheRouterReports()
    {
        _store.AddRoute(_profile.Id, "a.com", "L2TP0");

        var reader = CreateReader(groups: [new RouterRouteGroup("dyndns-SSTP0", "SSTP0", ["a.com"])]);

        reader.Read();

        Assert.Equal("SSTP0", Assert.Single(_store.GetRoutes(_profile.Id)).Interface);
    }

    [Fact]
    public void Read_ShowsEveryRouteButAdoptsOnlyItsOwnGroups()
    {
        var reader = CreateReader(groups: [new RouterRouteGroup("someone-else", "SSTP0", ["a.com"])]);

        var state = reader.Read();

        // The state mirrors the router, so the window can show that the domain is routed there, but the
        // bindings only follow the groups this app manages.
        Assert.Equal("SSTP0", state[new BindingKey(_profile.Id, "a.com")]);
        Assert.Empty(_store.GetRoutes(_profile.Id));
    }

    [Fact]
    public void Read_ShowsTheStateWithoutReconcilingWhileASyncIsInFlight()
    {
        _syncBusy = true;

        var reader = CreateReader(groups: [new RouterRouteGroup("dyndns-SSTP0", "SSTP0", ["a.com"])]);

        var state = reader.Read();

        // Until the change of our own reaches the router, its answer is only displayed.
        Assert.Equal("SSTP0", state[new BindingKey(_profile.Id, "a.com")]);
        Assert.Empty(_store.GetRoutes(_profile.Id));
    }

    [Fact]
    public void Read_KeepsThePreviousStateWhenTheRouterFails()
    {
        var fails = false;
        var reader = CreateReader(
            groups: [new RouterRouteGroup("dyndns-SSTP0", "SSTP0", ["a.com"])],
            fail: () => fails);

        Assert.Equal("SSTP0", reader.Read()[new BindingKey(_profile.Id, "a.com")]);

        fails = true;
        reader.Read();

        // An unreachable router must not make the profile look empty.
        Assert.Equal("SSTP0", reader.Snapshot()[new BindingKey(_profile.Id, "a.com")]);
    }

    [Fact]
    public void Forget_DropsTheStateOfARemovedProfile()
    {
        var reader = CreateReader(groups: [new RouterRouteGroup("dyndns-SSTP0", "SSTP0", ["a.com"])]);

        reader.Read();
        reader.Forget(_profile.Id);

        Assert.Empty(reader.Snapshot());
    }

    private RouterStateReader CreateReader(IReadOnlyList<RouterRouteGroup> groups, Func<bool>? fail = null) =>
        new(
            _store,
            _ => fail?.Invoke() == true
                ? Task.FromException<IReadOnlyList<RouterRouteGroup>>(new InvalidOperationException("router is down"))
                : Task.FromResult(groups),
            () => _syncBusy);

    public void Dispose() => _temp.Dispose();
}
