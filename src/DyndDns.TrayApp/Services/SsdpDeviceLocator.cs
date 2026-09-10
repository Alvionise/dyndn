using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// Best-effort UPnP/SSDP lookup that maps a local IP address to a human-readable device name.
/// Failures are swallowed on purpose: network discovery must still work when SSDP is blocked,
/// unsupported, or the device description cannot be fetched.
/// </summary>
internal static class SsdpDeviceLocator
{
    private const string MulticastAddress = "239.255.255.250";
    private const int MulticastPort = 1900;
    private static readonly TimeSpan ResponseWindow = TimeSpan.FromMilliseconds(1500);

    private static readonly HttpClient DescriptionClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public static async Task<Dictionary<string, string>> ResolveNamesAsync(CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var locations = await CollectLocationsAsync(cancellationToken).ConfigureAwait(false);

            foreach (var location in locations)
            {
                var (host, name) = await ReadDeviceNameAsync(location, cancellationToken).ConfigureAwait(false);

                if (host.Length > 0 && name.Length > 0 && !names.ContainsKey(host))
                    names[host] = name;
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"SSDP name resolution failed: {ex.Message}");
        }

        return names;
    }

    private static async Task<List<string>> CollectLocationsAsync(CancellationToken cancellationToken)
    {
        var locations = new List<string>();

        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var request = BuildSearchRequest();
        await client.SendAsync(request, request.Length, new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort))
            .ConfigureAwait(false);

        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(ResponseWindow);

        while (!window.IsCancellationRequested)
        {
            try
            {
                var response = await client.ReceiveAsync(window.Token).ConfigureAwait(false);
                var location = ExtractHeader(Encoding.UTF8.GetString(response.Buffer), "LOCATION");

                if (location.Length > 0 && !locations.Contains(location, StringComparer.OrdinalIgnoreCase))
                    locations.Add(location);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }

        return locations;
    }

    private static byte[] BuildSearchRequest()
    {
        var builder = new StringBuilder();
        builder.Append("M-SEARCH * HTTP/1.1\r\n");
        builder.Append($"HOST: {MulticastAddress}:{MulticastPort}\r\n");
        builder.Append("MAN: \"ssdp:discover\"\r\n");
        builder.Append("MX: 1\r\n");
        builder.Append("ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n");
        builder.Append("\r\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string ExtractHeader(string response, string header)
    {
        foreach (var line in response.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(header + ":", StringComparison.OrdinalIgnoreCase))
                return trimmed[(header.Length + 1)..].Trim();
        }

        return string.Empty;
    }

    private static async Task<(string Host, string Name)> ReadDeviceNameAsync(string location, CancellationToken cancellationToken)
    {
        try
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
                return (string.Empty, string.Empty);

            var xml = await DescriptionClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            var document = XDocument.Parse(xml);

            var name = FindElementValue(document, "friendlyName");
            if (name.Length == 0)
                name = FindElementValue(document, "modelName");

            return (uri.Host, name);
        }
        catch (Exception)
        {
            // A single unreachable description must not abort the rest of the lookup.
            return (string.Empty, string.Empty);
        }
    }

    private static string FindElementValue(XDocument document, string localName) =>
        document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName)?.Value.Trim()
        ?? string.Empty;
}
