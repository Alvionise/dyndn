namespace DyndDns.TrayApp.Models;

/// <summary>
/// One router the app manages: where it is, how to sign in and which VPN connection its domains go through
/// when they have none of their own. Every profile takes part in synchronization; a domain can be bound to
/// several of them.
/// </summary>
internal sealed class RouterProfile
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = "192.168.1.1";

    public string Username { get; set; } = "admin";

    /// <summary>Plaintext while in memory; stored protected with DPAPI.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>VPN interface the domains of this profile use when they have no interface of their own.</summary>
    public string VpnInterface { get; set; } = string.Empty;

    /// <summary>How the router is written in the menus, the lists and the table; see <see cref="RouterLabel"/>.</summary>
    public string DisplayName => RouterLabel.Format(Name, Address);
}
