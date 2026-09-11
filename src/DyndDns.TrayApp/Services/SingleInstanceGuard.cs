namespace DyndDns.TrayApp.Services;

/// <summary>
/// Keeps a second copy of the tray app from starting: two instances would work on the same database, run
/// two DNS monitors and show two tray icons. The autostart entry and a manual launch can easily overlap.
/// The named mutex belongs to the first process and is released by Windows when that process ends, so a
/// crash does not lock the app out. The name can be overridden to keep tests away from the real one.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultName = @"Local\DyndDns.TrayApp";

    private Mutex? _mutex;

    /// <summary>Returns <c>false</c> when another instance already holds the mutex.</summary>
    public bool TryAcquire(string name = DefaultName)
    {
        if (_mutex is not null)
            return true;

        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);

        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        return true;
    }

    public void Dispose()
    {
        _mutex?.Dispose();
        _mutex = null;
    }
}
