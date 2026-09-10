using DyndDns.TrayApp.Services;
using Xunit;

namespace DyndDns.TrayApp.Tests;

public class DomainNormalizerTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("  example.com  ", "example.com")]
    [InlineData("http://example.com", "example.com")]
    [InlineData("https://example.com", "example.com")]
    [InlineData("https://www.example.com", "example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("https://www.example.com/path/to/page", "example.com")]
    [InlineData("example.com/path", "example.com")]
    [InlineData("example.com?query=1", "example.com")]
    [InlineData("example.com/path?query=1", "example.com")]
    [InlineData("https://sub.example.org/a/b?c=d", "sub.example.org")]
    [InlineData("HTTP://WWW.EXAMPLE.COM/X", "EXAMPLE.COM")]
    [InlineData("", "")]
    public void Normalize_ReducesInputToBareDomain(string input, string expected) =>
        Assert.Equal(expected, DomainNormalizer.Normalize(input));

    [Fact]
    public void Normalize_PreservesCase()
    {
        var result = DomainNormalizer.Normalize("Example.COM");

        Assert.Equal("Example.COM", result);
    }
}
