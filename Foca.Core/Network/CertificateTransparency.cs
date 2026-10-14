using System.Text.Json;

namespace Foca.Core.Network;

/// <summary>
/// Subdomains from Certificate Transparency logs (crt.sh). Replaces scraping DNSDumpster, whose HTML
/// and anti-bot token changed repeatedly: CT logs list every name a public CA issued a certificate for.
/// </summary>
public sealed class CertificateTransparency
{
    private readonly HttpClient http;

    public CertificateTransparency(HttpClient http)
    {
        this.http = http;
    }

    public async Task<IReadOnlyList<string>> GetSubdomainsAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new InvalidOperationException(Foca.Core.Model.Project.DomainRequiredMessage);

        string url = "https://crt.sh/?q=" + Uri.EscapeDataString("%." + domain.Trim()) + "&output=json";
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Parse(json, domain);
    }

    /// <summary>Extracts the names under <paramref name="domain"/> from crt.sh JSON (name_value may hold several names per line).</summary>
    public static IReadOnlyList<string> Parse(JsonDocument json, string domain)
    {
        string root = domain.Trim().TrimEnd('.').ToLowerInvariant();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        foreach (JsonElement entry in json.RootElement.EnumerateArray())
        {
            foreach (string property in new[] { "name_value", "common_name" })
            {
                if (!entry.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
                    continue;
                foreach (string raw in value.GetString()!.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string name = raw.ToLowerInvariant().TrimEnd('.');
                    if (name.StartsWith("*.", StringComparison.Ordinal))
                        name = name.Substring(2);
                    if (IsHostName(name) && (name == root || name.EndsWith("." + root, StringComparison.Ordinal)))
                        names.Add(name);
                }
            }
        }
        return names.ToList();
    }

    private static bool IsHostName(string name) =>
        name.Length is > 0 and <= 253 && Uri.CheckHostName(name) == UriHostNameType.Dns;
}
