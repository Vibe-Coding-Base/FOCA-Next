using System.Net.Http.Headers;

namespace Foca.Core.Network;

public sealed record HttpFingerprintReport(
    Uri Url,
    int StatusCode,
    string? Server,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> MissingSecurityHeaders,
    IReadOnlyList<string> Issues);

/// <summary>
/// Passive HTTP fingerprinting: software banners and technologies from headers and cookies, and the
/// security headers a modern site is expected to send.
/// </summary>
public static class HttpFingerprint
{
    private static readonly (string Header, string Name)[] ExpectedHeaders =
    {
        ("Strict-Transport-Security", "HSTS"),
        ("Content-Security-Policy", "Content-Security-Policy"),
        ("X-Content-Type-Options", "X-Content-Type-Options"),
        ("Referrer-Policy", "Referrer-Policy"),
    };

    // Cookie names that identify the platform that issued them
    private static readonly (string Cookie, string Technology)[] CookieSignatures =
    {
        ("ASP.NET_SessionId", "ASP.NET"),
        ("PHPSESSID", "PHP"),
        ("JSESSIONID", "Java servlet container"),
        ("CFID", "Adobe ColdFusion"),
        ("laravel_session", "Laravel"),
        ("wordpress_", "WordPress"),
        ("wp-settings-", "WordPress"),
        ("BIGipServer", "F5 BIG-IP load balancer"),
        ("AWSALB", "AWS Application Load Balancer"),
        ("__cf_bm", "Cloudflare"),
    };

    public static async Task<HttpFingerprintReport> ScanAsync(HttpClient http, Uri url, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return Analyze(url, response);
    }

    public static HttpFingerprintReport Analyze(Uri url, HttpResponseMessage response)
    {
        HttpResponseHeaders h = response.Headers;
        // Raw header text: the parsed view splits "Apache/2.4.29 (Ubuntu)" into product tokens
        string? Header(string name) =>
            h.NonValidated.TryGetValues(name, out HeaderStringValues v) ? v.ToString()
            : response.Content.Headers.NonValidated.TryGetValues(name, out HeaderStringValues c) ? c.ToString() : null;

        var technologies = new List<string>();
        string? server = Header("Server");
        foreach (string name in new[] { "X-Powered-By", "X-AspNet-Version", "X-AspNetMvc-Version", "X-Generator", "X-Drupal-Cache", "X-Varnish", "Via" })
        {
            string? value = Header(name);
            if (value != null)
                technologies.Add($"{name}: {value}");
        }
        if (h.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            foreach (string cookie in cookies)
            {
                foreach ((string prefix, string technology) in CookieSignatures)
                {
                    if (cookie.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !technologies.Contains(technology))
                        technologies.Add(technology);
                }
            }
        }

        var missing = ExpectedHeaders.Where(e => Header(e.Header) == null && (e.Header != "Strict-Transport-Security" || url.Scheme == Uri.UriSchemeHttps))
            .Select(e => e.Name).ToList();
        if (Header("X-Frame-Options") == null && Header("Content-Security-Policy")?.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase) != true)
            missing.Add("Clickjacking protection (X-Frame-Options or CSP frame-ancestors)");

        var issues = new List<string>();
        if (server != null && server.Any(char.IsDigit))
            issues.Add($"Server banner discloses a version: {server}");
        foreach (string t in technologies.Where(t => t.StartsWith("X-Powered-By", StringComparison.Ordinal) || t.StartsWith("X-AspNet", StringComparison.Ordinal)))
            issues.Add($"Header discloses the platform: {t}");

        return new HttpFingerprintReport(url, (int)response.StatusCode, server, technologies, missing, issues);
    }
}
