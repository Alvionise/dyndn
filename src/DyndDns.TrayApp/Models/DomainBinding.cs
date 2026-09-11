namespace DyndDns.TrayApp.Models;

/// <summary>
/// One binding of a domain: which router routes it and through which VPN interface. A domain may have
/// several bindings, one per router, which is exactly what the monitoring table shows.
/// </summary>
internal sealed record DomainBinding(string Domain, int RouterId, string RouterName, string Interface);

/// <summary>
/// Identity of a binding: a domain on one router. The domain is lower-cased, so lookups and comparisons
/// do not depend on how it was typed.
/// </summary>
internal readonly record struct BindingKey
{
    public BindingKey(int routerId, string domain)
    {
        RouterId = routerId;
        Domain = domain.ToLowerInvariant();
    }

    public int RouterId { get; }

    public string Domain { get; }
}
