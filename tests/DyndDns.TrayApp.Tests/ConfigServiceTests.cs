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

    public void Dispose()
    {
        if (Directory.Exists(_configDir))
            Directory.Delete(_configDir, recursive: true);

        GC.SuppressFinalize(this);
    }
}
