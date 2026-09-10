using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class PasswordProtectorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("plaintext")]
    public void Unprotect_PassesThroughValuesWithoutMarker(string stored) =>
        Assert.Equal(stored, PasswordProtector.Unprotect(stored));

    [Fact]
    public void Protect_LeavesEmptyPasswordEmpty() =>
        Assert.Equal(string.Empty, PasswordProtector.Protect(string.Empty));

    [Fact]
    public void Protect_ThenUnprotect_RoundTrips()
    {
        const string password = "s3cret-p@ss";

        var stored = PasswordProtector.Protect(password);

        Assert.NotEqual(password, stored);
        Assert.StartsWith("dpapi:", stored);
        Assert.Equal(password, PasswordProtector.Unprotect(stored));
    }

    [Fact]
    public void Protect_IsIdempotent()
    {
        var once = PasswordProtector.Protect("s3cret");

        Assert.Equal(once, PasswordProtector.Protect(once));
    }

    [Fact]
    public void Unprotect_ReturnsEmptyForCorruptedPayload() =>
        Assert.Equal(string.Empty, PasswordProtector.Unprotect("dpapi:not-valid-base64!!"));
}
