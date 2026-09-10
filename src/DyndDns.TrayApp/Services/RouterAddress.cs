namespace DyndDns.TrayApp.Services;

/// <summary>
/// Normalizes a router address that may be a bare host/IP or carry an explicit http/https
/// scheme. Bare values are treated as HTTP, which is what Keenetic exposes by default.
/// </summary>
internal static class RouterAddress
{
    public const string DefaultScheme = "http";

    public static string Normalize(string address) =>
        (address ?? string.Empty).Trim().TrimEnd('/');

    public static string ToBaseUrl(string address)
    {
        var value = Normalize(address);
        if (value.Length == 0)
            return value;

        return value.Contains("://", StringComparison.Ordinal) ? value : $"{DefaultScheme}://{value}";
    }

    public static string GetHost(string address)
    {
        var value = Normalize(address);
        if (value.Length == 0)
            return value;

        var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
        var host = schemeIndex >= 0 ? value[(schemeIndex + 3)..] : value;

        var slashIndex = host.IndexOf('/');
        if (slashIndex >= 0)
            host = host[..slashIndex];

        var colonIndex = host.IndexOf(':');
        if (colonIndex >= 0)
            host = host[..colonIndex];

        return host;
    }
}
