namespace DyndDns.TrayApp.Models;

/// <summary>Layout of a browser history database.</summary>
internal enum BrowserHistoryFormat
{
    /// <summary>Chromium family: Chrome, Edge, Yandex, Brave, Vivaldi, Opera.</summary>
    Chromium,

    /// <summary>Mozilla Firefox.</summary>
    Firefox
}

/// <summary>A browser history database that can be imported.</summary>
internal sealed record BrowserHistoryDatabase(string Path, BrowserHistoryFormat Format);

/// <summary>Visits of a single domain taken from a browser history.</summary>
internal readonly record struct DomainVisit(long Hits, DateTime LastSeen);
