using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Views;
using Xunit;

namespace DyndDns.TrayApp.Tests;

/// <summary>
/// Every dialog builds its layout from code, so constructing them catches layout mistakes. The checks
/// also pin the properties that keep the content from overlapping: the root panel fills the client area,
/// the window is resizable and it cannot be shrunk below the size it was designed for.
/// </summary>
public class DialogLayoutTests
{
    [Fact]
    public void CredentialsDialog_KeepsItsInput()
    {
        using var dialog = new CredentialsDialog("Home (192.168.1.1)", "192.168.1.1", "admin", "secret");

        AssertResizableAndFilled(dialog);
        Assert.Equal("192.168.1.1", dialog.Address);
        Assert.Equal("admin", dialog.Username);
        Assert.Equal("secret", dialog.Password);
    }

    [Fact]
    public void RouterSelectionDialog_AcceptsAnEmptyScanResult()
    {
        using var dialog = new RouterSelectionDialog([]);

        AssertResizableAndFilled(dialog);
        Assert.Equal(string.Empty, dialog.Address);
    }

    [Fact]
    public void RouterSelectionDialog_AcceptsAScanResult()
    {
        using var dialog = new RouterSelectionDialog(
        [
            new DiscoveredRouter("192.168.1.1", "Home"),
            new DiscoveredRouter("192.168.100.1", "ONT")
        ]);

        AssertResizableAndFilled(dialog);
    }

    [Fact]
    public void SettingsDialog_AppliesTheMonitorSwitches()
    {
        var config = new AppConfig();
        config.Monitor.MonitorEnabled = false;
        config.Monitor.BrowserHistoryEnabled = true;
        config.Monitor.JournalAutoCleanup = false;
        config.Monitor.JournalMaxRows = 15000;

        using var dialog = new SettingsDialog(config, @"c:\data\dyndns.db");

        AssertResizableAndFilled(dialog);
        Assert.False(dialog.MonitorEnabled);
        Assert.True(dialog.BrowserHistoryEnabled);
        Assert.False(dialog.JournalAutoCleanup);

        // The history switch is a source of the same journal, so it is left unusable while the recording is off.
        Assert.False(dialog.CanImportBrowserHistory);

        dialog.ApplyTo(config);

        Assert.False(config.Monitor.MonitorEnabled);
        Assert.True(config.Monitor.BrowserHistoryEnabled);
        Assert.False(config.Monitor.JournalAutoCleanup);
        Assert.Equal(15000, config.Monitor.JournalMaxRows);
    }

    [Fact]
    public void SettingsDialog_MakesTheHistorySwitchAvailableWithTheRecording()
    {
        using var dialog = new SettingsDialog(new AppConfig(), @"c:\data\dyndns.db");

        Assert.True(dialog.MonitorEnabled);
        Assert.True(dialog.CanImportBrowserHistory);
    }

    [Fact]
    public void SettingsDialog_KeepsEveryControlInsideItsParent()
    {
        using var dialog = new SettingsDialog(new AppConfig(), @"c:\data\dyndns.db");

        AssertNothingEscapesItsParent(dialog);
    }

    /// <summary>
    /// The rule every window applies to its own size: the layout asks for more room when its content needs it,
    /// and the window is never allowed below the size it was designed for.
    /// </summary>
    [Fact]
    public void ClientSizeNeeded_FollowsTheContentAndNeverGoesBelowTheDesignedSize()
    {
        var designed = new Size(200, 150);

        using var small = DialogLayout.Stack([new Label { Text = "мелко" }]);
        Assert.Equal(designed, DialogLayout.ClientSizeNeeded(small, designed));

        using var big = DialogLayout.Stack([new Panel { Size = new Size(900, 700) }]);
        var needed = DialogLayout.ClientSizeNeeded(big, designed);

        Assert.True(needed.Width >= 900, $"width was {needed.Width}");
        Assert.True(needed.Height >= 700, $"height was {needed.Height}");
    }

    /// <summary>
    /// An auto-sized container measures a row before the width of the column is known, so a label that turns out
    /// wider than the column wraps into a second line the container was never sized for. Walking the tree catches
    /// that: the explanation under the journal limit sat on the bottom border of its group box exactly this way.
    /// </summary>
    private static void AssertNothingEscapesItsParent(Control parent)
    {
        var area = new Rectangle(Point.Empty, parent.ClientSize);

        foreach (Control child in parent.Controls)
        {
            Assert.True(
                area.Contains(child.Bounds),
                $"{child.GetType().Name} \"{child.Text}\" {child.Bounds} does not fit into {parent.GetType().Name} {area}");

            AssertNothingEscapesItsParent(child);
        }
    }

    private static void AssertResizableAndFilled(Form dialog)
    {
        // The layout lives in a single root panel that fills the client area, so nothing is placed by
        // absolute coordinates any more.
        var root = Assert.Single(dialog.Controls.Cast<Control>());
        Assert.Equal(DockStyle.Fill, root.Dock);

        Assert.Equal(FormBorderStyle.Sizable, dialog.FormBorderStyle);
        Assert.NotEqual(Size.Empty, dialog.MinimumSize);
        Assert.True(dialog.MinimumSize.Width >= 300);
    }
}
