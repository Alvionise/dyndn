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

    private static readonly string[] QueryNameFields = ["QueryName", "Name", "Query", "HostName"];

    private ConcurrentDictionary<string, int> _hits = new(StringComparer.OrdinalIgnoreCase);
    private TraceEventSession? _session;

    public bool IsRunning => _session is not null;

    public bool Start()
    {
        if (_session is not null)
            return true;

        TraceEventSession? session = null;

        try
        {
            session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableProvider(ProviderName, TraceEventLevel.Verbose, AnyKeyword);
            session.Source.Dynamic.All += OnEvent;

            _session = session;

            // The thread runs for as long as the session lives; the reference is not kept, because stopping is
            // the session's own business.
            new Thread(() => Process(session))
            {
                IsBackground = true,
                Name = "DyndDns.DnsMonitor"
            }.Start();

            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"DNS monitor failed to start: {ex.Message}");

            // The session is a real-time one on the machine, so it is torn down here: dropping it before
            // it was assigned to the field would leave it to the finalizer.
            session?.Dispose();
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

        // DNS names are case-insensitive, so the journal keeps one form of them: the event carries the name
        // as the caller wrote it, and the same lookup in another casing would become a second row.
        var domain = DomainNormalizer.Normalize(name.TrimEnd('.')).ToLowerInvariant();
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
    }

    /// <summary>
    /// Runs the session on its own thread until it is stopped. An exception escaping here would end the whole
    /// process — the thread belongs to nobody — and the session is disposed from the outside, so it is caught.
    /// </summary>
    private static void Process(TraceEventSession session)
    {
        try
        {
            session.Source.Process();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"DNS monitor processing ended: {ex.Message}");
        }
    }

    public void Dispose() => Stop();
}
