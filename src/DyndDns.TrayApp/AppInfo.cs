namespace DyndDns.TrayApp;

/// <summary>
/// The name the app shows, in one place. It appears in the tray, in every window title and in every
/// message box, so a rename must not mean hunting for literals through the code.
/// </summary>
internal static class AppInfo
{
    public const string Name = "DyndDns";

    /// <summary>Title of a window: the app name, a dash and what the window shows.</summary>
    public static string Title(string window) => $"{Name} — {window}";
}
