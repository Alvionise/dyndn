namespace DyndDns.TrayApp.Models;

/// <summary>
/// How a router is written wherever one is listed or picked: the name it is known by with its address in
/// parentheses, for example <c>Home (192.168.1.1)</c>. The rule lives here alone, so the tray menu, the
/// drop-downs of the monitoring window and the table name a router the same way. A router that carries no
/// name of its own — or whose name is the address it was found at — is shown by the address alone, instead of
/// repeating it as <c>192.168.1.1 (192.168.1.1)</c>.
/// </summary>
internal static class RouterLabel
{
    public static string Format(string? name, string? address)
    {
        var host = address?.Trim() ?? string.Empty;
        var label = name?.Trim();

        if (string.IsNullOrEmpty(label) || string.Equals(label, host, StringComparison.OrdinalIgnoreCase))
            return host;

        return $"{label} ({host})";
    }
}
