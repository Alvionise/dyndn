namespace DyndDns.TrayApp.Services;

/// <summary>
/// Turns free-form user input (a pasted URL or host) into a bare domain that can be
/// pushed to a Keenetic FQDN object-group.
/// </summary>
internal static class DomainNormalizer
{
    public static string Normalize(string input)
    {
        var value = input.Trim();

        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = value[8..];
        else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            value = value[7..];

        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            value = value[4..];

        var slash = value.IndexOf('/');
        var query = value.IndexOf('?');
        var end = (slash, query) switch
        {
            (> 0, > 0) => Math.Min(slash, query),
            (> 0, _) => slash,
            (_, > 0) => query,
            _ => value.Length
        };

        return value[..end];
    }
}
