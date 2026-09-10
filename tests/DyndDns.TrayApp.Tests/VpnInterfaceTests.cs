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
              "L2TP0": { "type": "L2TP", "description": "vpn", "connected": "yes", "state": "up" },
              "Home": { "type": "Bridge", "connected": "no", "state": "up" }
            }
            """);

        var interfaces = KeeneticApiService.ParseInterfaces(document.RootElement);

        var vpn = Assert.Single(interfaces, item => item.Name == "L2TP0");
        Assert.Equal("L2TP", vpn.Type);
        Assert.Equal("vpn", vpn.Description);
        Assert.True(vpn.IsUp);

        var bridge = Assert.Single(interfaces, item => item.Name == "Home");
        Assert.False(bridge.IsUp);
    }

    [Fact]
    public void ParseInterfaces_FallsBackToStateWhenConnectedIsAbsent()
    {
        using var document = JsonDocument.Parse("""{ "GigabitEthernet0": { "type": "GigabitEthernet", "state": "up" } }""");

        var parsed = Assert.Single(KeeneticApiService.ParseInterfaces(document.RootElement));

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
        Assert.Equal(expected, KeeneticApiService.IsVpnInterface(name, type));

    [Fact]
    public void PickActive_PrefersConnectedInterface()
    {
        var interfaces = new List<VpnInterfaceInfo>
        {
            new("L2TP0", "L2TP", "down tunnel", false, string.Empty),
            new("SSTP0", "SSTP", "live tunnel", true, string.Empty)
        };

        Assert.Equal("SSTP0", VpnInterfaceResolver.PickActive(interfaces)?.Name);
    }

    [Fact]
    public void PickActive_FallsBackToFirstInterface()
    {
        var interfaces = new List<VpnInterfaceInfo>
        {
            new("L2TP0", "L2TP", string.Empty, false, string.Empty)
        };

        Assert.Equal("L2TP0", VpnInterfaceResolver.PickActive(interfaces)?.Name);
    }

    [Fact]
    public void PickActive_ReturnsNullWhenThereAreNoInterfaces() =>
        Assert.Null(VpnInterfaceResolver.PickActive(new List<VpnInterfaceInfo>()));
}
