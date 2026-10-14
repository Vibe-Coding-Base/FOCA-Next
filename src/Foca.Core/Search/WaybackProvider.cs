using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Foca.Core.Search;

/// <summary>
/// Documents archived by the Internet Archive (CDX API). Finds files that were removed from the site
/// long ago, which are often the most revealing ones, without scraping search engines. Results are
/// URLs of the archived raw copies ("id_" mode), which remain downloadable after the original is gone.
/// </summary>
public sealed class WaybackProvider : IDocumentSearchProvider
{
    private readonly HttpClient http;

    public string Name => "Wayback Machine";

    /// <summary>Maximum rows requested from the CDX API per run.</summary>
    public int Limit { get; init; } = 10000;

    public WaybackProvider(HttpClient http)
    {
        this.http = http;
    }

    public static string BuildQuery(string domain, IReadOnlyCollection<string> extensions, int limit)
    {
        string pattern = extensions.Count == 0
            ? ".*"
            : ".*\\.(" + string.Join("|", extensions.Select(e => System.Text.RegularExpressions.Regex.Escape(e.TrimStart('.').ToLowerInvariant()))) + ")$";
        return "https://web.archive.org/cdx/search/cdx?url=" + Uri.EscapeDataString(domain.Trim()) +
               "&matchType=domain&output=json&fl=timestamp,original&collapse=urlkey" +
               "&filter=statuscode:200&filter=" + Uri.EscapeDataString("original:(?i)" + pattern) +
               "&limit=" + limit;
    }

    public async IAsyncEnumerable<Uri> SearchAsync(string domain, IReadOnlyCollection<string> extensions, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new InvalidOperationException(Foca.Core.Model.Project.DomainRequiredMessage);

        using HttpResponseMessage response = await http.GetAsync(BuildQuery(domain, extensions, Limit), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        foreach (Uri uri in ParseCdx(await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false)))
            yield return uri;
    }

    /// <summary>Parses CDX JSON (a header row, then [timestamp, original] rows) into archived-copy URLs.</summary>
    public static IEnumerable<Uri> ParseCdx(JsonDocument json)
    {
        using (json)
        {
            if (json.RootElement.ValueKind != JsonValueKind.Array)
                yield break;
            bool header = true;
            foreach (JsonElement row in json.RootElement.EnumerateArray())
            {
                if (header)
                {
                    header = false;
                    continue;
                }
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 2)
                    continue;
                string? timestamp = row[0].GetString();
                string? original = row[1].GetString();
                if (timestamp == null || !timestamp.All(char.IsAsciiDigit) || original == null)
                    continue;
                if (Uri.TryCreate(original, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    yield return new Uri($"https://web.archive.org/web/{timestamp}id_/{uri.AbsoluteUri}");
            }
        }
    }

    /// <summary>The original URL of an archived copy produced by <see cref="ParseCdx"/>.</summary>
    public static Uri? OriginalOf(Uri archived)
    {
        const string marker = "id_/";
        string s = archived.AbsoluteUri;
        int i = s.IndexOf(marker, StringComparison.Ordinal);
        return archived.Host == "web.archive.org" && i > 0 && Uri.TryCreate(s.Substring(i + marker.Length), UriKind.Absolute, out Uri? original) ? original : null;
    }
}
