namespace DyndDns.TrayApp.Models;

/// <summary>One aggregated DNS row shown in the search window.</summary>
public sealed record DomainStat(string Domain, long Hits, DateTime FirstSeen, DateTime LastSeen);
