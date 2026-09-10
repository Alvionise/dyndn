using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Real-time ETW listener for DNS lookups performed on this machine. Only the queried names are
/// kept; nothing is written to disk by the monitor itself. Creating a real-time session requires
/// administrator rights, which the application manifest requests.
/// </summary>
internal sealed class DnsMonitor : IDisposable
{
    private const string ProviderName = "Microsoft-Windows-DNS-Client";
    private const string SessionName = "DyndDns-DnsMonitor";

    /// <summary>All keywords: without them the provider stays silent.</summary>
    private const ulong AnyKeyword = 0xFFFFFFFFFFFFFFFF;

    private static readonly string[] QueryNameFields = { "QueryName", "Name", "Query", "HostName" };

    private ConcurrentDictionary<string, int> _hits = new(StringComparer.OrdinalIgnoreCase);
    private TraceEventSession? _session;
    private Thread? _worker;

    public bool IsRunning => _session is not null;

    public bool Start()
    {
        if (_session is not null)
            return true;

        try
        {
            var session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableProvider(ProviderName, TraceEventLevel.Verbose, AnyKeyword);
            session.Source.Dynamic.All += OnEvent;

            _session = session;
            _worker = new Thread(() => session.Source.Process())
            {
                IsBackground = true,
                Name = "DyndDns.DnsMonitor"
            };
            _worker.Start();

            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"DNS monitor failed to start: {ex.Message}");

            _session?.Dispose();
            _session = null;
            return false;
        }
    }

    private void OnEvent(TraceEvent data)
    {
        var name = ExtractQueryName(data);

        // Events of this provider also carry payloads without a name (server list changes and the
        // like); they are of no interest here.
        if (name is null)
            return;

        var domain = DomainNormalizer.Normalize(name.TrimEnd('.'));
        if (domain.Length == 0)
            return;

        _hits.AddOrUpdate(domain, 1, (_, current) => current + 1);
    }

    /// <summary>
    /// The provider field carrying the queried name changed between Windows builds, so a few known
    /// spellings are accepted before giving up on the event.
    /// </summary>
    private static string? ExtractQueryName(TraceEvent data)
    {
        foreach (var field in QueryNameFields)
        {
            if (data.PayloadByName(field) is string value && value.Length > 0)
                return value;
        }

        return null;
    }

    /// <summary>Hands over the collected counters and starts a fresh batch.</summary>
    public Dictionary<string, int> DrainHits()
    {
        var drained = Interlocked.Exchange(
            ref _hits,
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        return new Dictionary<string, int>(drained, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Stops collecting; the monitor can be started again afterwards.</summary>
    public void Stop()
    {
        try
        {
            _session?.Source.StopProcessing();
        }
        catch (Exception ex)
        {
            // Session teardown is best-effort: the app may already be shutting down.
            Trace.TraceError($"DNS monitor stop failed: {ex.Message}");
        }

        _session?.Dispose();
        _session = null;
        _worker = null;
    }

    public void Dispose() => Stop();
}
