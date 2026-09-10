using System.IO;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class ConfigServiceTests : IDisposable
{
    private readonly string _configDir = Path.Combine(Path.GetTempPath(), "dyndn-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureConfigExists_LeavesFreshConfigUnconfigured()
    {
        var service = new ConfigService(_configDir);
        service.EnsureConfigExists();

        var config = service.LoadConfig();

        Assert.False(config.SetupDismissed);
        Assert.True(ConfigService.SetupRequired(config));
    }

    [Fact]
    public void SaveConfig_RoundTripsSetupDismissedFlag()
    {
        var service = new ConfigService(_configDir);
        service.EnsureConfigExists();

        var config = service.LoadConfig();
        config.SetupDismissed = true;
        service.SaveConfig(config);

        var reloaded = service.LoadConfig();

        Assert.True(reloaded.SetupDismissed);
        Assert.False(ConfigService.SetupRequired(reloaded));
    }

    [Fact]
    public void SetupRequired_IsFalseWhenCredentialsPresent()
    {
        var config = new AppConfig();
        config.Router.Address = "192.168.1.1";
        config.Router.Password = "secret";

        Assert.False(ConfigService.SetupRequired(config));
    }

    [Fact]
    public void SaveRoutes_AndLoadRoutes_RoundTrip()
    {
        var service = new ConfigService(_configDir);

        service.SaveRoutes(new[] { new DnsRoute("a.com", "L2TP0"), new DnsRoute("b.com", string.Empty) });

        var routes = service.LoadRoutes();

        Assert.Equal(2, routes.Count);
        Assert.Equal("a.com", routes[0].Domain);
        Assert.Equal("L2TP0", routes[0].Interface);
        Assert.Equal(string.Empty, routes[1].Interface);
    }

    [Fact]
    public void LoadDnsList_KeepsClassicFormat()
    {
        var service = new ConfigService(_configDir);
        Directory.CreateDirectory(_configDir);
        File.WriteAllText(
            Path.Combine(_configDir, "dns-list.json"),
            """{ "groupName": "default", "domains": ["a.com", "b.com"] }""");

        var list = service.LoadDnsList();

        Assert.Equal("default", list.Name);
        Assert.Equal(new[] { "a.com", "b.com" }, list.Domains);
    }

    [Fact]
    public void SaveDnsList_AndLoadDnsList_RoundTrip()
    {
        var service = new ConfigService(_configDir);

        service.SaveDnsList(new DnsGroup { Name = "default", Domains = { "a.com" } });

        var list = service.LoadDnsList();

        Assert.Equal("default", list.Name);
        Assert.Equal(new[] { "a.com" }, list.Domains);
    }

    [Fact]
    public void Routes_AreStoredInTheirOwnFile()
    {
        var service = new ConfigService(_configDir);

        service.SaveRoutes(new[] { new DnsRoute("a.com", "L2TP0") });

        Assert.True(File.Exists(service.DnsRoutesPath));
        Assert.False(File.Exists(service.DnsListPath));
        Assert.Equal(new[] { "a.com" }, service.LoadRoutes().Select(route => route.Domain));
    }

    public void Dispose()
    {
        if (Directory.Exists(_configDir))
            Directory.Delete(_configDir, recursive: true);

        GC.SuppressFinalize(this);
    }
}
