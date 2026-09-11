using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Registers the app for the current user, so Windows starts it after signing in. The registry value is
/// the source of truth — the settings dialog only reflects and changes it. The key path and the value
/// name can be overridden so tests never touch the real autostart entry.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DyndDns";

    public static bool IsEnabled(string keyPath = RunKeyPath, string valueName = ValueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        return key?.GetValue(valueName) is string value && value.Length > 0;
    }

    /// <summary>Adds or removes the startup entry; returns <c>false</c> when the change failed.</summary>
    public static bool Set(bool enabled, string keyPath = RunKeyPath, string valueName = ValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(keyPath);

            if (key is null)
                return false;

            if (enabled)
                key.SetValue(valueName, BuildCommand(), RegistryValueKind.String);
            else
                key.DeleteValue(valueName, throwOnMissingValue: false);

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Trace.TraceError($"Startup registration failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Full path of the running executable, quoted for the registry.</summary>
    internal static string BuildCommand()
    {
        var path = Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;
        return $"\"{path}\"";
    }
}
