using System.Text.Json;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class VpnInterfaceTests
{
    [Fact]
    public void ParseInterfaces_ReadsTypeDescriptionAndState()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "L2TP0": { "type": "L2TP", "description": "Латвия", "connected": "yes", "state": "up" },
              "Home": { "type": "Bridge", "connected": "no", "state": "up" }
            }
            """);

        var interfaces = KeeneticRci.ParseInterfaces(document.RootElement);

        var vpn = Assert.Single(interfaces, item => item.Name == "L2TP0");
        Assert.Equal("L2TP", vpn.Type);
        Assert.Equal("Латвия", vpn.Description);
        Assert.True(vpn.IsUp);

        var bridge = Assert.Single(interfaces, item => item.Name == "Home");
        Assert.False(bridge.IsUp);
    }

    [Theory]
    [InlineData("SSTP0", "Латвия", "Латвия (SSTP0)")]
    [InlineData("SSTP0", "sstp0", "SSTP0")]
    [InlineData("SSTP0", "", "SSTP0")]
    [InlineData("SSTP0", null, "SSTP0")]
    [InlineData(" SSTP0 ", " Латвия ", "Латвия (SSTP0)")]
    public void VpnLabel_PutsTheNameOfTheConnectionBeforeItsInterface(string name, string? description, string expected) =>
        Assert.Equal(expected, VpnLabel.Format(name, description));

    [Fact]
    public void ParseInterfaces_FallsBackToStateWhenConnectedIsAbsent()
    {
        using var document = JsonDocument.Parse("""{ "GigabitEthernet0": { "type": "GigabitEthernet", "state": "up" } }""");

        var parsed = Assert.Single(KeeneticRci.ParseInterfaces(document.RootElement));

        Assert.True(parsed.IsUp);
    }

    [Theory]
    [InlineData("L2TP0", "L2TP", true)]
    [InlineData("l2tp1", "L2TP", true)]
    [InlineData("PPTP0", "PPTP", true)]
    [InlineData("SSTP0", "SSTP", true)]
    [InlineData("Wireguard0", "Wireguard", true)]
    [InlineData("OpenVPN0", "OpenVPN", true)]
    [InlineData("GigabitEthernet0", "GigabitEthernet", false)]
    [InlineData("Home", "Bridge", false)]
    public void IsVpnInterface_RecognizesVpnTypes(string name, string type, bool expected) =>
        Assert.Equal(expected, KeeneticRci.IsVpnInterface(name, type));

    [Fact]
    public void PickPreferred_PrefersConnectedInterfaceWhenNothingIsConfigured()
    {
        var interfaces = new List<VpnInterfaceInfo>
        {
            new("L2TP0", "L2TP", string.Empty, false),
            new("SSTP0", "SSTP", "Латвия", true)
        };

        Assert.Equal("SSTP0", VpnInterfaceResolver.PickPreferred(interfaces, null)?.Name);
    }

    [Fact]
    public void PickPreferred_FallsBackToFirstInterfaceWhenNoneIsConnected()
    {
        var interfaces = new List<VpnInterfaceInfo> { new("L2TP0", "L2TP", string.Empty, false) };

        Assert.Equal("L2TP0", VpnInterfaceResolver.PickPreferred(interfaces, null)?.Name);
    }

    [Fact]
    public void PickPreferred_ReturnsNullWhenNothingIsConfiguredAndThereAreNoInterfaces() =>
        Assert.Null(VpnInterfaceResolver.PickPreferred([], null));

    [Fact]
    public void PickPreferred_KeepsConfiguredInterfaceWhileItExists()
    {
        var interfaces = new List<VpnInterfaceInfo>
        {
            new("L2TP0", "L2TP", string.Empty, true),
            new("SSTP0", "SSTP", string.Empty, false)
        };

        Assert.Equal("SSTP0", VpnInterfaceResolver.PickPreferred(interfaces, "SSTP0")?.Name);
    }

    [Fact]
    public void PickPreferred_FallsBackToConnectedWhenConfiguredIsGone()
    {
        var interfaces = new List<VpnInterfaceInfo>
        {
            new("L2TP0", "L2TP", string.Empty, false),
            new("SSTP0", "SSTP", string.Empty, true)
        };

        Assert.Equal("SSTP0", VpnInterfaceResolver.PickPreferred(interfaces, "OpenVPN0")?.Name);
    }

    [Fact]
    public void PickPreferred_ReturnsFirstWhenNothingIsConfiguredOrConnected()
    {
        var interfaces = new List<VpnInterfaceInfo> { new("L2TP0", "L2TP", string.Empty, false) };

        Assert.Equal("L2TP0", VpnInterfaceResolver.PickPreferred(interfaces, null)?.Name);
    }

    [Fact]
    public void PickPreferred_ReturnsNullWhenThereAreNoInterfaces() =>
        Assert.Null(VpnInterfaceResolver.PickPreferred([], "L2TP0"));
}
