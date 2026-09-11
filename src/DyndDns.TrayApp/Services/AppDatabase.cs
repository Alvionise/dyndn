using System.IO;
using Microsoft.Data.Sqlite;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// The single SQLite file the app keeps everything in: the global settings, the router profiles with the
/// domains bound to each of them, and the DNS journal. The schema is created when the file is missing; a
/// database written by an older version of the app is not upgraded.
/// </summary>
internal sealed class AppDatabase
{
    public const string FileName = "dyndns.db";

    private readonly string _connectionString;

    public AppDatabase(string databasePath)
    {
        Path = databasePath;

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public string Path { get; }

    /// <summary>Creates the folder and the schema when they are missing; safe on every start.</summary>
    public void Initialize()
    {
        var directory = System.IO.Path.GetDirectoryName(Path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = Open();
        CreateSchema(connection);
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Cascades are off by default in SQLite, and profiles rely on them.
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS routers (
                id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                name          TEXT    NOT NULL DEFAULT '',
                address       TEXT    NOT NULL DEFAULT '',
                username      TEXT    NOT NULL DEFAULT '',
                password      TEXT    NOT NULL DEFAULT '',
                vpn_interface TEXT    NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS routes (
                router_id  INTEGER NOT NULL,
                domain     TEXT    NOT NULL,
                interface  TEXT    NULL,
                PRIMARY KEY (router_id, domain),
                FOREIGN KEY (router_id) REFERENCES routers (id) ON DELETE CASCADE
            );

            -- Every comparison of a domain ignores its case — on the router, in the journal cleanup and when
            -- a binding is added again in another spelling — so the index carries that collation.
            CREATE INDEX IF NOT EXISTS routes_domain_nocase_idx ON routes (domain COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS dns_queries (
                domain    TEXT    NOT NULL PRIMARY KEY,
                hits      INTEGER NOT NULL DEFAULT 0,
                last_seen TEXT    NOT NULL DEFAULT ''
            );
            """;
        command.ExecuteNonQuery();
    }

    internal static void WriteSetting(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    internal static string? ReadSetting(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string;
    }
}
