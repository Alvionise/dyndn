using System.IO;
using System.Text.Json;
using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

public class ConfigService
{
    private readonly string _configDir;
    private readonly string _configPath;
    private readonly string _dnsListPath;
    private readonly string _dnsRoutesPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ConfigService(string configDir)
    {
        _configDir = configDir;
        _configPath = Path.Combine(_configDir, "dyndns.json");
        _dnsListPath = Path.Combine(_configDir, "dns-list.json");
        _dnsRoutesPath = Path.Combine(_configDir, "dns-routes.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public string ConfigPath => _configPath;
    public string DnsListPath => _dnsListPath;
    public string DnsRoutesPath => _dnsRoutesPath;

    /// <summary>
    /// A router counts as configured once both an address and a password are present.
    /// </summary>
    public static bool IsRouterConfigured(AppConfig config) =>
        !string.IsNullOrWhiteSpace(config.Router.Address) &&
        !string.IsNullOrWhiteSpace(config.Router.Password);

    /// <summary>
    /// The first-run wizard is offered while the router is unconfigured and the user has not
    /// already dismissed it.
    /// </summary>
    public static bool SetupRequired(AppConfig config) =>
        !IsRouterConfigured(config) && !config.SetupDismissed;

    public AppConfig LoadConfig()
    {
        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            config.Router.Password = PasswordProtector.Unprotect(config.Router.Password);
            return config;
        }

        return new AppConfig();
    }

    public void SaveConfig(AppConfig config)
    {
        // Protect only for the serialized form; callers keep working with the plaintext value.
        var plaintextPassword = config.Router.Password;
        config.Router.Password = PasswordProtector.Protect(plaintextPassword);

        try
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(_configPath, json);
        }
        finally
        {
            config.Router.Password = plaintextPassword;
        }
    }

    /// <summary>
    /// The classic list the tray menu maintains: <c>{ "groupName", "domains": [ ... ] }</c>, pushed
    /// to the router as the group named in the file (default <c>default</c>).
    /// </summary>
    public DnsGroup LoadDnsList()
    {
        if (File.Exists(_dnsListPath))
        {
            var json = File.ReadAllText(_dnsListPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var domains = new List<string>();
            if (root.TryGetProperty("domains", out var domainsProp) && domainsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in domainsProp.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s))
                        domains.Add(s);
                }
            }

            return new DnsGroup
            {
                Name = root.TryGetProperty("groupName", out var nameProp) ? (nameProp.GetString() ?? "default") : "default",
                Domains = domains,
                DnsListFile = _dnsListPath
            };
        }

        return new DnsGroup { Name = "default", Domains = new List<string>(), DnsListFile = _dnsListPath };
    }

    public void SaveDnsList(DnsGroup group)
    {
        Directory.CreateDirectory(_configDir);

        var saveGroup = new
        {
            groupName = group.Name,
            domains = group.Domains
        };

        var json = JsonSerializer.Serialize(saveGroup, _jsonOptions);
        File.WriteAllText(_dnsListPath, json);
        group.DnsListFile = _dnsListPath;
    }

    /// <summary>
    /// Domains added from the search window together with the VPN interface they should use. Kept in
    /// a separate file so the classic list above keeps its own format and router group.
    /// </summary>
    public List<DnsRoute> LoadRoutes()
    {
        var routes = new List<DnsRoute>();

        if (!File.Exists(_dnsRoutesPath))
            return routes;

        var json = File.ReadAllText(_dnsRoutesPath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("routes", out var routesProperty) || routesProperty.ValueKind != JsonValueKind.Array)
            return routes;

        foreach (var item in routesProperty.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var domain = item.TryGetProperty("domain", out var domainProperty) ? domainProperty.GetString() : null;
            if (string.IsNullOrWhiteSpace(domain))
                continue;

            var interfaceName = item.TryGetProperty("interface", out var interfaceProperty)
                ? interfaceProperty.GetString()
                : null;

            routes.Add(new DnsRoute(domain, interfaceName ?? string.Empty));
        }

        return routes;
    }

    public void SaveRoutes(IReadOnlyList<DnsRoute> routes)
    {
        Directory.CreateDirectory(_configDir);

        var payload = new
        {
            routes = routes.Select(route => new { domain = route.Domain, @interface = route.Interface })
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        File.WriteAllText(_dnsRoutesPath, json);
    }

    public void EnsureConfigExists()
    {
        if (!Directory.Exists(_configDir))
        {
            Directory.CreateDirectory(_configDir);
        }

        if (!File.Exists(_configPath))
        {
            var config = new AppConfig
            {
                Router = new RouterConfig
                {
                    Address = "192.168.1.1",
                    Username = "admin",
                    Password = string.Empty
                },
                VpnInterface = "OpenVPN0",
                Sync = new SyncConfig { AutoSync = true },
                Hotkey = new HotkeyConfig { Enabled = true, Key = "V", Modifiers = "Control+Shift" }
            };

            SaveConfig(config);
        }

        if (!File.Exists(_dnsListPath))
        {
            SaveDnsList(new DnsGroup { Name = "default", Domains = new List<string>() });
        }

        if (!File.Exists(_dnsRoutesPath))
        {
            SaveRoutes(Array.Empty<DnsRoute>());
        }
    }
}
