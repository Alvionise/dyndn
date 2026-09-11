namespace DyndDns.TrayApp.Models;

internal sealed class MonitorConfig
{
    /// <summary>Whether DNS queries are recorded while the app runs.</summary>
    public bool MonitorEnabled { get; set; } = true;

    /// <summary>
    /// Whether visited domains are also imported from the browser history. Browsers resolve names
    /// with their own resolver, so their lookups are not visible to the ETW monitor.
    /// </summary>
    public bool BrowserHistoryEnabled { get; set; } = true;

    /// <summary>
    /// Whether the journal is kept to <see cref="JournalMaxRows"/> entries: once more domains that no binding
    /// mentions have been recorded, the oldest of them are dropped. Bound domains are never removed.
    /// </summary>
    public bool JournalAutoCleanup { get; set; } = true;

    /// <summary>How many entries of the journal the cleanup keeps.</summary>
    public int JournalMaxRows { get; set; } = 10000;
}

/// <summary>
/// Global settings. Everything router specific (address, credentials, VPN interface, managed domains)
/// belongs to a <see cref="RouterProfile"/>.
/// </summary>
internal sealed class AppConfig
{
    public MonitorConfig Monitor { get; set; } = new();

    /// <summary>
    /// Set when the user declines the first-run setup, so it is not shown again on every launch.
    /// Cleared once a router profile is saved.
    /// </summary>
    public bool SetupDismissed { get; set; }
}
