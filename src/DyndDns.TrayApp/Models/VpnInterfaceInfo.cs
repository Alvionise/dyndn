namespace DyndDns.TrayApp.Models;

/// <summary>
/// A router interface as reported by <c>show interface</c>. Only the VPN-capable ones are used,
/// but the raw values are kept so the chosen connection can be described to the user.
/// </summary>
public sealed record VpnInterfaceInfo(string Name, string Type, string Description, bool IsUp, string Address)
{
    public string StatusText => IsUp ? "подключён" : "не подключён";

    public string DisplayName => string.IsNullOrWhiteSpace(Description)
        ? $"{Name} ({Type}) — {StatusText}"
        : $"{Description} — {Name} ({Type}) — {StatusText}";
}
