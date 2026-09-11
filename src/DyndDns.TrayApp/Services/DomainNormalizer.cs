namespace DyndDns.TrayApp.Services;

/// <summary>
/// Turns free-form user input (a pasted URL or host) into a bare domain that can be
/// pushed to a Keenetic FQDN object-group.
/// </summary>
internal static class DomainNormalizer
{
    private const string WwwPrefix = "www.";

    /// <summary>Where the host ends: a path, a query, a fragment or a port.</summary>
    private static readonly char[] HostSeparators = ['/', '?', '#', ':'];

    public static string Normalize(string input)
    {
        var value = CutAfterScheme(input.Trim());
        var end = value.Length;

        foreach (var separator in HostSeparators)
        {
            var index = value.IndexOf(separator);

            if (index > 0 && index < end)
                end = index;
        }

        value = value[..end];

        // "www." belongs to a longer name and goes away with it; a name that is left with a single label after it
        // ("www.com") is a domain of its own, and dropping the prefix would turn it into "com".
        return value.StartsWith(WwwPrefix, StringComparison.OrdinalIgnoreCase) &&
            value[WwwPrefix.Length..].Contains('.')
                ? value[WwwPrefix.Length..]
                : value;
    }

    private static string CutAfterScheme(string value) =>
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? value[8..]
        : value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? value[7..]
        : value;
}
