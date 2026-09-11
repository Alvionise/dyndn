using DyndDns.TrayApp.Services;
using Microsoft.Win32;
using Xunit;

namespace DyndDns.TrayApp.Tests;

/// <summary>
/// The autostart entry is written to a scratch key, so the tests never touch the real
/// <c>Run</c> entry of the machine they run on.
/// </summary>
public class StartupRegistrationTests : IDisposable
{
    private const string ScratchKeyPath = @"Software\DyndDns\Tests\Run";

    [Fact]
    public void Set_WritesAndRemovesTheAutostartEntry()
    {
        Assert.True(StartupRegistration.Set(true, ScratchKeyPath));
        Assert.True(StartupRegistration.IsEnabled(ScratchKeyPath));

        using (var key = Registry.CurrentUser.OpenSubKey(ScratchKeyPath, writable: false))
        {
            var command = Assert.IsType<string>(key?.GetValue("DyndDns"));
            Assert.StartsWith("\"", command, StringComparison.Ordinal);
            Assert.EndsWith("\"", command, StringComparison.Ordinal);
        }

        Assert.True(StartupRegistration.Set(false, ScratchKeyPath));
        Assert.False(StartupRegistration.IsEnabled(ScratchKeyPath));
    }

    [Fact]
    public void IsEnabled_IsFalseWhenThereIsNoEntry() =>
        Assert.False(StartupRegistration.IsEnabled(ScratchKeyPath));

    [Fact]
    public void BuildCommand_QuotesTheExecutablePath()
    {
        var command = StartupRegistration.BuildCommand();

        Assert.StartsWith("\"", command, StringComparison.Ordinal);
        Assert.EndsWith("\"", command, StringComparison.Ordinal);
        Assert.True(command.Length > 2);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\DyndDns", throwOnMissingSubKey: false);
        GC.SuppressFinalize(this);
    }
}
