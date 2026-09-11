namespace DyndDns.TrayApp.Models;

/// <summary>One aggregated DNS row shown in the search window.</summary>
internal sealed record DomainStat(string Domain, long Hits, DateTime LastSeen);
