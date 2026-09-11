using System.Globalization;
using DyndDns.TrayApp.Models;
using Microsoft.Data.Sqlite;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Everything the app persists, on top of <see cref="AppDatabase"/>: the global settings, the router
/// profiles and the domains bound to each of them. A domain may be bound to several profiles, each with
/// its own VPN interface.
/// </summary>
internal sealed class SettingsStore
{
    /// <summary>Column list of a profile, shared by every query that reads one.</summary>
    private const string ProfileColumns = "id, name, address, username, password, vpn_interface";

    private const string RouterNameColumn = "name";
    private const string RouterVpnInterfaceColumn = "vpn_interface";

    private readonly AppDatabase _database;

    /// <summary>
    /// Guards the read-then-write of a binding. The casing of a domain is not part of the key, so two writers —
    /// a bind in the window and the reconciliation that follows a router read on a worker thread — could both
    /// fail to see the stored row and insert one each, leaving two bindings for the same name. The app runs as a
    /// single process, so one lock is enough.
    /// </summary>
    private readonly Lock _bindingGate = new();

    public SettingsStore(AppDatabase database) => _database = database;

    public AppConfig LoadConfig()
    {
        var config = new AppConfig();

        using var connection = _database.Open();

        config.SetupDismissed = ReadFlag(connection, "SetupDismissed", config.SetupDismissed);
        config.Monitor.MonitorEnabled = ReadFlag(connection, "Monitor.Enabled", config.Monitor.MonitorEnabled);
        config.Monitor.BrowserHistoryEnabled = ReadFlag(connection, "Monitor.BrowserHistory", config.Monitor.BrowserHistoryEnabled);
        config.Monitor.JournalAutoCleanup = ReadFlag(connection, "Monitor.JournalCleanup", config.Monitor.JournalAutoCleanup);
        config.Monitor.JournalMaxRows = ReadNumber(connection, "Monitor.JournalMaxRows", config.Monitor.JournalMaxRows);

        return config;
    }

    public void SaveConfig(AppConfig config)
    {
        using var connection = _database.Open();

        WriteSetting(connection, "SetupDismissed", config.SetupDismissed ? "1" : "0");
        WriteSetting(connection, "Monitor.Enabled", config.Monitor.MonitorEnabled ? "1" : "0");
        WriteSetting(connection, "Monitor.BrowserHistory", config.Monitor.BrowserHistoryEnabled ? "1" : "0");
        WriteSetting(connection, "Monitor.JournalCleanup", config.Monitor.JournalAutoCleanup ? "1" : "0");
        WriteSetting(connection, "Monitor.JournalMaxRows", config.Monitor.JournalMaxRows.ToString(CultureInfo.InvariantCulture));
    }

    // --- профили ---

    public IReadOnlyList<RouterProfile> GetRouters()
    {
        var profiles = new List<RouterProfile>();

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {ProfileColumns} FROM routers ORDER BY id;";

        using var reader = command.ExecuteReader();

        while (reader.Read())
            profiles.Add(ReadProfile(reader));

        return profiles;
    }

    public RouterProfile? GetRouter(int routerId)
    {
        using var connection = _database.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {ProfileColumns} FROM routers WHERE id = $id;";
        command.Parameters.AddWithValue("$id", routerId);

        using var reader = command.ExecuteReader();

        return reader.Read() ? ReadProfile(reader) : null;
    }

    /// <summary>
    /// The profile that answers on the same device as <paramref name="address"/>, if any; the profile being
    /// edited is skipped. One device is one profile: a second one would write the same groups on that router
    /// and remove the domains of the first.
    /// </summary>
    public RouterProfile? FindByHost(string address, int excludedRouterId = 0) =>
        GetRouters().FirstOrDefault(profile =>
            profile.Id != excludedRouterId && RouterAddress.IsSameHost(profile.Address, address));

    /// <summary>
    /// Inserts a new profile or updates an existing one and returns it with the id it was stored under; the
    /// password of the given profile stays in memory as plaintext, only the stored copy is protected.
    /// </summary>
    public RouterProfile SaveRouter(RouterProfile profile)
    {
        using var connection = _database.Open();

        var protectedPassword = PasswordProtector.Protect(profile.Password);

        if (profile.Id == 0)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO routers (name, address, username, password, vpn_interface)
                VALUES ($name, $address, $username, $password, $vpnInterface);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$name", profile.Name);
            insert.Parameters.AddWithValue("$address", profile.Address);
            insert.Parameters.AddWithValue("$username", profile.Username);
            insert.Parameters.AddWithValue("$password", protectedPassword);
            insert.Parameters.AddWithValue("$vpnInterface", profile.VpnInterface);

            profile.Id = Convert.ToInt32(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        else
        {
            using var update = connection.CreateCommand();
            update.CommandText =
                """
                UPDATE routers
                   SET name = $name, address = $address, username = $username,
                       password = $password, vpn_interface = $vpnInterface
                 WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$name", profile.Name);
            update.Parameters.AddWithValue("$address", profile.Address);
            update.Parameters.AddWithValue("$username", profile.Username);
            update.Parameters.AddWithValue("$password", protectedPassword);
            update.Parameters.AddWithValue("$vpnInterface", profile.VpnInterface);
            update.Parameters.AddWithValue("$id", profile.Id);
            update.ExecuteNonQuery();
        }

        return profile;
    }

    /// <summary>
    /// Stores the name the router gave itself. The profile is edited in the dialogs and written whole there, so a
    /// reader that learned one value must not write its copy back: that would undo an edit made while the router
    /// was answering.
    /// </summary>
    public void SetRouterName(int routerId, string name) => SetRouterValue(RouterNameColumn, routerId, name);

    /// <summary>Stores the VPN connection the router turned out to have; see <see cref="SetRouterName"/>.</summary>
    public void SetRouterVpnInterface(int routerId, string interfaceName) =>
        SetRouterValue(RouterVpnInterfaceColumn, routerId, interfaceName);

    /// <summary>Deletes the profile together with its bindings; the router itself is left alone.</summary>
    public void DeleteRouter(int routerId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM routers WHERE id = $id;";
        command.Parameters.AddWithValue("$id", routerId);
        command.ExecuteNonQuery();
    }

    // --- домены профиля ---

    public IReadOnlyList<DnsRoute> GetRoutes(int routerId)
    {
        var routes = new List<DnsRoute>();

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT domain, interface FROM routes WHERE router_id = $routerId ORDER BY domain;";
        command.Parameters.AddWithValue("$routerId", routerId);

        using var reader = command.ExecuteReader();

        while (reader.Read())
            routes.Add(new DnsRoute(reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));

        return routes;
    }

    /// <summary>Every binding of every profile, for the monitoring table.</summary>
    public IReadOnlyList<DomainBinding> GetBindings()
    {
        var bindings = new List<DomainBinding>();

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.domain, r.router_id, p.name, p.address, COALESCE(r.interface, '')
              FROM routes r
              JOIN routers p ON p.id = r.router_id
             ORDER BY r.domain, p.id;
            """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            // The row names the router exactly like the menus and the drop-downs do, so a binding and a choice read alike.
            var display = RouterLabel.Format(reader.GetString(2), reader.GetString(3));

            bindings.Add(new DomainBinding(reader.GetString(0), reader.GetInt32(1), display, reader.GetString(4)));
        }

        return bindings;
    }

    /// <summary>
    /// Binds the domain to the profile, or updates the interface of an existing binding. Returns
    /// <c>false</c> when the binding was already there unchanged.
    /// </summary>
    public bool AddRoute(int routerId, string domain, string interfaceName)
    {
        lock (_bindingGate)
        {
            // An empty interface means "through the connection of the profile", which is stored as no value;
            // the decision is made once and used for the comparison as well as for the write.
            var wanted = string.IsNullOrWhiteSpace(interfaceName) ? string.Empty : interfaceName;

            using var connection = _database.Open();

            bool exists;
            string current;

            using (var query = connection.CreateCommand())
            {
                // The domain is looked up without regard to case, so binding the same host twice only updates
                // the interface; the stored casing stays the one the user typed.
                query.CommandText = "SELECT interface FROM routes WHERE router_id = $routerId AND domain = $domain COLLATE NOCASE;";
                query.Parameters.AddWithValue("$routerId", routerId);
                query.Parameters.AddWithValue("$domain", domain);

                using var reader = query.ExecuteReader();

                exists = reader.Read();
                current = exists && !reader.IsDBNull(0) ? reader.GetString(0) : string.Empty;
            }

            if (exists && string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
                return false;

            using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$routerId", routerId);
            command.Parameters.AddWithValue("$domain", domain);
            command.Parameters.AddWithValue("$interface", wanted.Length > 0 ? wanted : DBNull.Value);

            if (exists)
            {
                // The row found above is updated rather than inserted again: the primary key compares the domain
                // byte for byte, so an insert would make a second row for the same site and keep the casing the
                // stored one was written with.
                command.CommandText = "UPDATE routes SET interface = $interface WHERE router_id = $routerId AND domain = $domain COLLATE NOCASE;";
            }
            else
            {
                command.CommandText =
                    """
                    INSERT INTO routes (router_id, domain, interface)
                    VALUES ($routerId, $domain, $interface)
                    ON CONFLICT(router_id, domain) DO UPDATE SET interface = excluded.interface;
                    """;
            }

            command.ExecuteNonQuery();

            return true;
        }
    }

    /// <summary>
    /// Drops the bindings of the profile that route through an interface the router does not have (any more);
    /// returns how many were removed. A binding without an interface of its own follows the default one of the
    /// profile and is left alone. The router is the source of truth for its connections: a binding whose tunnel
    /// is gone cannot be pushed, and the group of that interface disappears with the next synchronization.
    /// </summary>
    public int RemoveBindingsOfMissingInterfaces(int routerId, IReadOnlyCollection<string> knownInterfaces)
    {
        var known = new HashSet<string>(knownInterfaces, StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        foreach (var route in GetRoutes(routerId))
        {
            if (route.Interface.Length == 0 || known.Contains(route.Interface))
                continue;

            if (RemoveRoute(routerId, route.Domain))
                removed++;
        }

        return removed;
    }

    /// <summary>Drops the binding of that domain on that profile; returns <c>false</c> when it was absent.</summary>
    public bool RemoveRoute(int routerId, string domain)
    {
        lock (_bindingGate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();

            // Case is not part of the binding, so it is not part of the lookup either.
            command.CommandText = "DELETE FROM routes WHERE router_id = $routerId AND domain = $domain COLLATE NOCASE;";
            command.Parameters.AddWithValue("$routerId", routerId);
            command.Parameters.AddWithValue("$domain", domain);

            return command.ExecuteNonQuery() > 0;
        }
    }

    // --- служебное ---

    /// <summary>
    /// Writes one column of a profile. The names are constants of this class, never user input, which is what makes
    /// building the statement here safe; a single column keeps every other value as the user last stored it.
    /// </summary>
    private void SetRouterValue(string column, int routerId, string value)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE routers SET {column} = $value WHERE id = $id;";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$id", routerId);
        command.ExecuteNonQuery();
    }

    private static RouterProfile ReadProfile(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Address = reader.GetString(2),
            Username = reader.GetString(3),
            Password = PasswordProtector.Unprotect(reader.GetString(4)),
            VpnInterface = reader.GetString(5)
        };

    private static void WriteSetting(SqliteConnection connection, string key, string value) =>
        AppDatabase.WriteSetting(connection, key, value);

    private static string? ReadText(SqliteConnection connection, string key) => AppDatabase.ReadSetting(connection, key);

    private static bool ReadFlag(SqliteConnection connection, string key, bool fallback) =>
        ReadText(connection, key) is { Length: > 0 } value ? value is "1" or "true" or "True" : fallback;

    private static int ReadNumber(SqliteConnection connection, string key, int fallback) =>
        int.TryParse(ReadText(connection, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
