using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class RouterAddressTests
{
    [Theory]
    [InlineData("192.168.1.1", "192.168.1.1")]
    [InlineData("  192.168.1.1/  ", "192.168.1.1")]
    [InlineData("http://192.168.1.1/", "http://192.168.1.1")]
    public void Normalize_TrimsAndDropsTrailingSlash(string input, string expected) =>
        Assert.Equal(expected, RouterAddress.Normalize(input));

    [Theory]
    [InlineData("192.168.1.1", "http://192.168.1.1")]
    [InlineData("https://192.168.1.1", "https://192.168.1.1")]
    [InlineData("", "")]
    public void ToBaseUrl_AddsDefaultSchemeOnlyWhenMissing(string input, string expected) =>
        Assert.Equal(expected, RouterAddress.ToBaseUrl(input));

    [Theory]
    [InlineData("192.168.1.1", "192.168.1.1")]
    [InlineData("http://192.168.1.1", "192.168.1.1")]
    [InlineData("https://router.local:8443/path", "router.local")]
    [InlineData("router.local", "router.local")]
    [InlineData("", "")]
    public void GetHost_ExtractsHostWithoutSchemePortOrPath(string input, string expected) =>
        Assert.Equal(expected, RouterAddress.GetHost(input));

    [Theory]
    [InlineData("192.168.1.1", "http://192.168.1.1", true)]
    [InlineData("192.168.1.1", "https://192.168.1.1/rci/", true)]
    [InlineData("ROUTER.local", "router.local", true)]
    [InlineData("192.168.1.1", "192.168.1.2", false)]
    [InlineData("192.168.1.1", "", false)]
    [InlineData("", "", false)]
    public void IsSameHost_TellsOneDeviceFromAnother(string left, string right, bool expected) =>
        Assert.Equal(expected, RouterAddress.IsSameHost(left, right));
}
