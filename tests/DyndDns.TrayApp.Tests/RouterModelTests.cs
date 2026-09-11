using DyndDns.TrayApp.Models;
using Xunit;

namespace DyndDns.TrayApp.Tests;

/// <summary>How the router is named in the scan list and in the tray menu.</summary>
public class RouterModelTests
{
    [Fact]
    public void DiscoveredRouter_ShowsTheNameWithTheAddressOrTheAddressAlone()
    {
        Assert.Equal("Home (192.168.1.1)", new DiscoveredRouter("192.168.1.1", "Home").DisplayName);
        Assert.Equal("192.168.1.1", new DiscoveredRouter("192.168.1.1", string.Empty).DisplayName);
    }

    [Fact]
    public void RouterProfile_ShowsTheAddressWhenThereIsNoName()
    {
        Assert.Equal("192.168.1.1", new RouterProfile { Address = "192.168.1.1" }.DisplayName);
        Assert.Equal("Home (192.168.1.1)", new RouterProfile { Name = "Home", Address = "192.168.1.1" }.DisplayName);
    }

    [Fact]
    public void RouterLabel_NamesEveryRouterTheSameWay()
    {
        // A profile whose name was filled with its address (a device that answered the scan without a friendly
        // name) is not written as «адрес (адрес)», and a name that only differs in case is still the address.
        Assert.Equal("192.168.1.1", new RouterProfile { Name = "192.168.1.1", Address = "192.168.1.1" }.DisplayName);
        Assert.Equal("192.168.1.1", new DiscoveredRouter("192.168.1.1", "192.168.1.1").DisplayName);

        // The parts are trimmed, so a name coming from SSDP with spaces does not spoil the label.
        Assert.Equal("Home (192.168.1.1)", RouterLabel.Format(" Home ", " 192.168.1.1 "));
        Assert.Equal(string.Empty, RouterLabel.Format(null, null));
    }
}
