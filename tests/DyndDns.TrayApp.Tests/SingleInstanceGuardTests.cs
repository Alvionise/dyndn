using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

/// <summary>
/// The guard is a named mutex, so every test uses its own name: a shared one would let one test see the
/// mutex the other one holds.
/// </summary>
public class SingleInstanceGuardTests
{
    [Fact]
    public void TryAcquire_RefusesASecondGuard()
    {
        var name = NewName();

        using var first = new SingleInstanceGuard();
        using var second = new SingleInstanceGuard();

        Assert.True(first.TryAcquire(name));
        Assert.False(second.TryAcquire(name));
    }

    [Fact]
    public void TryAcquire_SucceedsAgainAfterTheHolderIsGone()
    {
        var name = NewName();

        using (var first = new SingleInstanceGuard())
            Assert.True(first.TryAcquire(name));

        using var second = new SingleInstanceGuard();
        Assert.True(second.TryAcquire(name));
    }

    [Fact]
    public void TryAcquire_KeepsTheOwnMutexAndReportsSuccess()
    {
        using var guard = new SingleInstanceGuard();
        var name = NewName();

        Assert.True(guard.TryAcquire(name));
        Assert.True(guard.TryAcquire(name));
    }

    private static string NewName() => $@"Local\DyndDns.Tests.{Guid.NewGuid():N}";
}
