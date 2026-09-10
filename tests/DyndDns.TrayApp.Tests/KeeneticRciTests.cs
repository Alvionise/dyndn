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

        Assert.Null(KeeneticApiService.BuildGroupUpdateCommand(existing, desired));
    }

    [Fact]
    public void BuildGroupUpdateCommand_AddsMissingDomains()
    {
        var existing = new FqdnGroupEntry { Name = "g", Description = "", Domains = ["a.com"] };
        var desired = new DnsGroup { Name = "g", Description = "", Domains = ["a.com", "b.com"] };

        var json = KeeneticApiService.BuildGroupUpdateCommand(existing, desired);
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

        var json = KeeneticApiService.BuildGroupUpdateCommand(existing, desired);
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

        var json = KeeneticApiService.BuildGroupUpdateCommand(existing, desired);
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

        var groups = KeeneticApiService.ParseFqdnGroups(document.RootElement);

        var group = Assert.Single(groups);
        Assert.Equal("mygroup", group.Name);
        Assert.Equal("my desc", group.Description);
        Assert.Equal(new[] { "a.com", "b.com" }, group.Domains);
    }

    [Fact]
    public void ParseFqdnGroups_AcceptsPlainStringIncludes()
    {
        using var document = JsonDocument.Parse("""{"g":{"include":["x.com"]}}""");

        var groups = KeeneticApiService.ParseFqdnGroups(document.RootElement);

        Assert.Equal(new[] { "x.com" }, Assert.Single(groups).Domains);
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

        var routes = KeeneticApiService.ParseDnsRoutes(document.RootElement);

        var route = Assert.Single(routes);
        Assert.Equal("1", route.Index);
        Assert.Equal("g", route.Group);
        Assert.Equal("OpenVPN0", route.Interface);
        Assert.Equal("c", route.Comment);
    }

    [Fact]
    public void BuildCreateGroupCommand_IncludesDomains()
    {
        var group = new DnsGroup
        {
            Name = "g",
            Description = "d",
            Domains = new List<string> { "a.com" }
        };

        var json = KeeneticApiService.BuildCreateGroupCommand(group);

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
        var json = KeeneticApiService.BuildCreateRouteCommand("g", "OpenVPN0");

        using var document = JsonDocument.Parse(json);
        var route = document.RootElement.GetProperty("dns-proxy").GetProperty("route");

        Assert.Equal("g", route.GetProperty("group").GetString());
        Assert.Equal("OpenVPN0", route.GetProperty("interface").GetString());
    }

    [Fact]
    public void BuildDeleteRouteCommand_UsesIndexWhenPresent()
    {
        var json = KeeneticApiService.BuildDeleteRouteCommand(new DnsRouteEntry { Index = "7", Group = "g" });

        using var document = JsonDocument.Parse(json);
        var route = document.RootElement.GetProperty("dns-proxy").GetProperty("route");

        Assert.True(route.GetProperty("no").GetBoolean());
        Assert.Equal("7", route.GetProperty("index").GetString());
    }

    [Fact]
    public void BuildDeleteRouteCommand_FallsBackToGroup()
    {
        var json = KeeneticApiService.BuildDeleteRouteCommand(new DnsRouteEntry { Group = "g" });

        using var document = JsonDocument.Parse(json);
        var route = document.RootElement.GetProperty("dns-proxy").GetProperty("route");

        Assert.Equal("g", route.GetProperty("group").GetString());
    }

    [Fact]
    public void BuildDeleteGroupCommand_ReferencesGroup()
    {
        var json = KeeneticApiService.BuildDeleteGroupCommand("g");

        using var document = JsonDocument.Parse(json);
        var group = document.RootElement
            .GetProperty("object-group")
            .GetProperty("fqdn")
            .GetProperty("g");

        Assert.True(group.GetProperty("no").GetBoolean());
    }
}
