using DyndDns.TrayApp.Models;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// One <see cref="KeeneticApiService"/> per router profile. Each router keeps its own session and cookie
/// container, so synchronizing several of them never mixes their authentication. The client is recreated
/// when the address or the credentials of the profile changed.
/// </summary>
internal sealed class RouterApiPool : IDisposable
{
    private readonly Dictionary<int, Entry> _clients = [];
    private readonly Lock _gate = new();

    private sealed record Entry(KeeneticApiService Api, string Fingerprint);

    public KeeneticApiService Get(RouterProfile profile)
    {
        var fingerprint = $"{profile.Address}|{profile.Username}|{profile.Password}";

        lock (_gate)
        {
            if (_clients.TryGetValue(profile.Id, out var existing) && existing.Fingerprint == fingerprint)
                return existing.Api;

            existing?.Api.Dispose();

            var api = new KeeneticApiService(profile);
            _clients[profile.Id] = new Entry(api, fingerprint);
            return api;
        }
    }

    /// <summary>Closes the session of a profile, so the next call starts a fresh one.</summary>
    public void Invalidate(int routerId)
    {
        lock (_gate)
        {
            if (_clients.Remove(routerId, out var existing))
                existing.Api.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _clients.Values)
                entry.Api.Dispose();

            _clients.Clear();
        }
    }
}
