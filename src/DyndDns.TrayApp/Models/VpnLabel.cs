namespace DyndDns.TrayApp.Models;

/// <summary>
/// How a VPN connection is written where one is listed or picked: the name it carries on the router — the
/// description of its interface — with the interface itself in parentheses, because the bindings refer to that
/// name and the connection is what the user recognises. A connection the router describes by its own interface
/// name, or does not describe at all, is written by the interface alone.
/// </summary>
internal static class VpnLabel
{
    public static string Format(string? interfaceName, string? description)
    {
        var name = interfaceName?.Trim() ?? string.Empty;
        var label = description?.Trim();

        if (string.IsNullOrEmpty(label) || string.Equals(label, name, StringComparison.OrdinalIgnoreCase))
            return name;

        return $"{label} ({name})";
    }
}
