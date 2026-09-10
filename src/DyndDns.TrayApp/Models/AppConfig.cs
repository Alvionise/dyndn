namespace DyndDns.TrayApp.Models;

public class RouterConfig
{
    public string Address { get; set; } = "192.168.1.1";
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = string.Empty;
}

public class SyncConfig
{
    public bool AutoSync { get; set; } = true;
}

public class HotkeyConfig
{
    public bool Enabled { get; set; } = true;
    public string Key { get; set; } = "V";
    public string Modifiers { get; set; } = "Control+Shift";
}

public class AppConfig
{
    public RouterConfig Router { get; set; } = new();
    public string VpnInterface { get; set; } = string.Empty;
    public SyncConfig Sync { get; set; } = new();
    public HotkeyConfig Hotkey { get; set; } = new();

    /// <summary>
    /// Set when the user declines the first-run setup wizard, so it is not shown again
    /// on every launch. Cleared once valid router credentials are saved.
    /// </summary>
    public bool SetupDismissed { get; set; }
}
