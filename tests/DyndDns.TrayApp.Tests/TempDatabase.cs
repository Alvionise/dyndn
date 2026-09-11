using System.IO;
using DyndDns.TrayApp.Services;
using Microsoft.Data.Sqlite;

namespace DyndDns.TrayApp.Tests;

/// <summary>
/// A throwaway database for one test: a folder of its own under the temp path, the schema created, and the
/// cleanup the platform needs — a pooled connection keeps the file open, so the pools are cleared before the
/// folder goes away. Every test class that works with a database used to repeat exactly this.
/// </summary>
internal sealed class TempDatabase : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dyndn-" + Guid.NewGuid().ToString("N"));

    public TempDatabase()
    {
        Directory.CreateDirectory(_folder);

        Database = new AppDatabase(Path.Combine(_folder, AppDatabase.FileName));
        Database.Initialize();
    }

    public AppDatabase Database { get; }

    public SettingsStore Store => new(Database);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);

        GC.SuppressFinalize(this);
    }
}
