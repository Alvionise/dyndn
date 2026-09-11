using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DyndDns.TrayApp.Services;

/// <summary>
/// The Keenetic sign-in handshake. The router answers with a realm and a per-request challenge, and the
/// password is proved with <c>sha256(challenge + md5(user:realm:password))</c> instead of being sent, so the
/// secret never travels. Both the router client (which keeps its own session) and the credential check of
/// the setup wizard (a single request) share this scheme from one place.
/// </summary>
internal static class KeeneticAuth
{
    /// <summary>
    /// Signs in over the given client. The client owns the cookie container, so the session sticks to it.
    /// Transport failures are thrown to the caller, which knows how to report them.
    /// </summary>
    public static async Task<bool> SignInAsync(
        HttpClient client,
        string baseUrl,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var probe = await client.GetAsync($"{baseUrl}/auth", cancellationToken).ConfigureAwait(false);

        if (probe.StatusCode != HttpStatusCode.Unauthorized)
            return probe.IsSuccessStatusCode;

        var challenge = Header(probe, "X-NDM-Challenge");
        var realm = Header(probe, "X-NDM-Realm");

        if (string.IsNullOrEmpty(challenge) || string.IsNullOrEmpty(realm))
            return false;

        using var content = new StringContent(
            JsonSerializer.Serialize(new { login = username, password = ComputePasswordHash(username, realm, challenge, password) }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync($"{baseUrl}/auth", content, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <summary>The two-stage hash the router expects in place of the plaintext password.</summary>
    public static string ComputePasswordHash(string username, string realm, string challenge, string password)
    {
        var stage1 = MD5.HashData(Encoding.UTF8.GetBytes($"{username}:{realm}:{password}"));
        var stage1Hex = Convert.ToHexString(stage1).ToLowerInvariant();

        var stage2 = SHA256.HashData(Encoding.UTF8.GetBytes(challenge + stage1Hex));
        return Convert.ToHexString(stage2).ToLowerInvariant();
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
