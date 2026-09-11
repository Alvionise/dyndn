using System.Text.Json;
using DyndDns.TrayApp.Models;
using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class KeeneticRciTests
{
    [Fact]
    public void BuildGroupUpdateCommand_ReturnsNullWhenGroupMatches()
    {
        var existing = new FqdnGroupEntry { Name = "g", Description = "", Domains = ["a.com", "b.com"] };
        var desired = new DnsGroup { Name = "g", Description = "", Domains = ["b.com", "a.com"] };

        Assert.Null(KeeneticRci.BuildGroupUpdateCommand(existing, desired));
    }

    [Fact]
    public void BuildGroupUpdateCommand_AddsMissingDomains()
    {
        var existing = new FqdnGroupEntry { Name = "g", Description = "", Domains = ["a.com"] };
        var desired = new DnsGroup { Name = "g", Description = "", Domains = ["a.com", "b.com"] };

        var json = KeeneticRci.BuildGroupUpdateCommand(existing, desired);
        using var document = JsonDocument.Parse(json!);
        var include = document.RootElement
            .GetProperty("object-group")
            .GetProperty("fqdn")
            .GetProperty("g")
            .GetProperty("include");

        var entry = Assert.Single(include.EnumerateArray());
        Assert.Equal("b.com", entry.GetProperty("address").GetString());
        Assert.False(entry.TryGetProperty("no", out _));
    }

    [Fact]
    public void BuildGroupUpdateCommand_RemovesStaleDomains()
    {
        var existing = new FqdnGroupEntry { Name = "g", Description = "", Domains = ["a.com", "b.com"] };
        var desired = new DnsGroup { Name = "g", Description = "", Domains = ["a.com"] };

        var json = KeeneticRci.BuildGroupUpdateCommand(existing, desired);
        using var document = JsonDocument.Parse(json!);
        var include = document.RootElement
            .GetProperty("object-group")
            .GetProperty("fqdn")
            .GetProperty("g")
            .GetProperty("include");

        var entry = Assert.Single(include.EnumerateArray());
        Assert.Equal("b.com", entry.GetProperty("address").GetString());
        Assert.True(entry.GetProperty("no").GetBoolean());
    }

    [Fact]
    public void BuildGroupUpdateCommand_UpdatesDescription()
    {
        var existing = new FqdnGroupEntry { Name = "g", Description = "", Domains = ["a.com"] };
        var desired = new DnsGroup { Name = "g", Description = "VPN", Domains = ["a.com"] };

        var json = KeeneticRci.BuildGroupUpdateCommand(existing, desired);
        using var document = JsonDocument.Parse(json!);
        var group = document.RootElement.GetProperty("object-group").GetProperty("fqdn").GetProperty("g");

        Assert.Equal("VPN", group.GetProperty("description").GetString());
        Assert.False(group.TryGetProperty("include", out _));
    }

    [Fact]
    public void ParseFqdnGroups_ReadsDescriptionAndIncludeAddresses()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "mygroup": {
                "description": "my desc",
                "include": [
                  { "address": "a.com" },
                  { "address": "b.com" }
                ]
              }
            }
            """);

        var groups = KeeneticRci.ParseFqdnGroups(document.RootElement);

        var group = Assert.Single(groups);
        Assert.Equal("mygroup", group.Name);
        Assert.Equal("my desc", group.Description);
        Assert.Equal(new[] { "a.com", "b.com" }, group.Domains);
    }

    [Fact]
    public void ParseFqdnGroups_AcceptsPlainStringIncludes()
    {
        using var document = JsonDocument.Parse("""{"g":{"include":["x.com"]}}""");

        var groups = KeeneticRci.ParseFqdnGroups(document.RootElement);

        Assert.Equal(new[] { "x.com" }, Assert.Single(groups).Domains);
    }

    [Fact]
    public void ParseInterfaces_KeepsOneOptionPerConnection()
    {
        // The router listed the same tunnel twice, the connected entry coming after the idle one.
        using var document = JsonDocument.Parse(
            """
            {
              "SSTP0": { "type": "SSTP", "connected": "no", "description": "tunnel" },
              "sstp0": { "type": "SSTP", "connected": "yes", "description": "tunnel" }
            }
            """);

        var interfaces = KeeneticRci.ParseInterfaces(document.RootElement);

        // Two spellings of one connection stay one interface, and the state that is kept is the connected one.
        var info = Assert.Single(interfaces);
        Assert.True(info.IsUp);
    }

    /// <summary>
    /// The state of a connection is spelled both ways by the firmwares, and an entry of an unexpected shape may
    /// not throw away the connections that are readable.
    /// </summary>
    [Theory]
    [InlineData("""{ "SSTP0": { "type": "SSTP", "connected": true } }""", true)]
    [InlineData("""{ "SSTP0": { "type": "SSTP", "connected": "yes" } }""", true)]
    [InlineData("""{ "SSTP0": { "type": "SSTP", "connected": false, "state": "up" } }""", false)]
    [InlineData("""{ "SSTP0": { "type": "SSTP", "state": "up" } }""", true)]
    [InlineData("""{ "SSTP0": { "type": "SSTP", "connected": 1 } }""", false)]
    [InlineData("""{ "SSTP0": { "type": 2, "connected": "yes" } }""", true)]
    [InlineData("""{ "SSTP0": "не объект", "L2TP0": { "connected": "yes" } }""", true)]
    public void ParseInterfaces_ReadsTheStateTheFirmwareReports(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        var interfaces = KeeneticRci.ParseInterfaces(document.RootElement);

        Assert.Equal(expected, Assert.Single(interfaces).IsUp);
    }

    [Fact]
    public void ParseDnsRoutes_ReadsEntries()
    {
        using var document = JsonDocument.Parse(
            """
            [
              { "index": "1", "group": "g", "interface": "OpenVPN0", "comment": "c" }
            ]
            """);

        var routes = KeeneticRci.ParseDnsRoutes(document.RootElement);

        var route = Assert.Single(routes);
        Assert.Equal("1", route.Index);
        Assert.Equal("g", route.Group);
        Assert.Equal("OpenVPN0", route.Interface);
    }

    [Fact]
    public void ParseDnsRoutes_SkipsEntriesThatAreNotRoutes()
    {
        // The section is an array: an entry of another shape, and one whose group is not a name, are not routes.
        using var document = JsonDocument.Parse(
            """
            [
              "странно",
              { "group": 7 },
              { "index": "2", "group": "g", "interface": "SSTP0" }
            ]
            """);

        var routes = KeeneticRci.ParseDnsRoutes(document.RootElement);

        var route = Assert.Single(routes);
        Assert.Equal("g", route.Group);
        Assert.Equal("SSTP0", route.Interface);
    }

    [Fact]
    public void BuildCreateGroupCommand_IncludesDomains()
    {
        var group = new DnsGroup
        {
            Name = "g",
            Description = "d",
            Domains = ["a.com"]
        };

        var json = KeeneticRci.BuildCreateGroupCommand(group);

        using var document = JsonDocument.Parse(json);
        var fqdn = document.RootElement
            .GetProperty("object-group")
            .GetProperty("fqdn")
            .GetProperty("g");

        Assert.Equal("d", fqdn.GetProperty("description").GetString());
        Assert.Equal("a.com", fqdn.GetProperty("include")[0].GetProperty("address").GetString());
    }

    [Fact]
    public void BuildCreateRouteCommand_TargetsVpnInterface()
    {
        var json = KeeneticRci.BuildCreateRouteCommand("g", "OpenVPN0");

        using var document = JsonDocument.Parse(json);
        var route = document.RootElement.GetProperty("dns-proxy").GetProperty("route");

        Assert.Equal("g", route.GetProperty("group").GetString());
        Assert.Equal("OpenVPN0", route.GetProperty("interface").GetString());
    }

    [Fact]
    public void BuildDeleteRouteCommand_UsesIndexWhenPresent()
    {
        var json = KeeneticRci.BuildDeleteRouteCommand(new DnsRouteEntry { Index = "7", Group = "g" });

        using var document = JsonDocument.Parse(json);
        var route = document.RootElement.GetProperty("dns-proxy").GetProperty("route");

        Assert.True(route.GetProperty("no").GetBoolean());
        Assert.Equal("7", route.GetProperty("index").GetString());
    }

    [Fact]
    public void BuildDeleteRouteCommand_FallsBackToGroup()
    {
        var json = KeeneticRci.BuildDeleteRouteCommand(new DnsRouteEntry { Group = "g" });

        using var document = JsonDocument.Parse(json);
        var route = document.RootElement.GetProperty("dns-proxy").GetProperty("route");

        Assert.Equal("g", route.GetProperty("group").GetString());
    }

    [Fact]
    public void ReadError_ReportsTheErrorRciHidesBehindA200Response()
    {
        // The router answered exactly like this to an unknown path (code 1179781).
        const string body =
            """
            [{"show":{"status":[{"status":"error","code":"1179781","ident":"Core::Configurator","message":"not found: show/ip/name [http/rci]."}]}}]
            """;

        Assert.Equal("not found: show/ip/name [http/rci].", KeeneticRci.ReadError(body));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[{\"show\":{\"sc\":{\"dns-proxy\":{\"route\":[]}}}}]")]
    [InlineData("")]
    public void ReadError_ReturnsNullForSuccessfulAnswers(string body) =>
        Assert.Null(KeeneticRci.ReadError(body));

    [Theory]
    [InlineData("""{"hostname":"Keenetic-1234","model":"KN-1011"}""", "Keenetic-1234")]
    [InlineData("""{"name":"Дом","model":"KN-1011"}""", "Дом")]
    [InlineData("""{"model":"KN-1011"}""", "KN-1011")]
    [InlineData("""{"hostname":"","name":"Дом"}""", "Дом")]
    [InlineData("""{"hostname":"   "}""", "")]
    [InlineData("""{"hostname":{"name":"nested"}}""", "")]
    [InlineData("[]", "")]
    public void ParseDeviceName_TakesTheFirstNameTheFirmwareReports(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, KeeneticRci.ParseDeviceName(document.RootElement));
    }

    [Fact]
    public void BuildDeleteGroupCommand_ReferencesGroup()
    {
        var json = KeeneticRci.BuildDeleteGroupCommand("g");

        using var document = JsonDocument.Parse(json);
        var group = document.RootElement
            .GetProperty("object-group")
            .GetProperty("fqdn")
            .GetProperty("g");

        Assert.True(group.GetProperty("no").GetBoolean());
    }
}
