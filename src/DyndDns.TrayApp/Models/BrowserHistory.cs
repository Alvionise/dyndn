namespace DyndDns.TrayApp.Models;

/// <summary>Layout of a browser history database.</summary>
public enum BrowserHistoryFormat
{
    /// <summary>Chromium family: Chrome, Edge, Yandex, Brave, Vivaldi, Opera.</summary>
    Chromium,

    /// <summary>Mozilla Firefox.</summary>
    Firefox
}

/// <summary>A browser history database that can be imported.</summary>
public sealed record BrowserHistoryDatabase(string Path, BrowserHistoryFormat Format);

/// <summary>Visits of a single domain taken from a browser history.</summary>
public readonly record struct DomainVisit(long Hits, DateTime LastSeen);
