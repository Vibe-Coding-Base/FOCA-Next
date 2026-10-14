using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FOCA.Searcher;
using FOCA.Threads;

namespace Foca.Core.Search;

public enum SearchEngine
{
    GoogleWeb,
    BingWeb,
    DuckDuckGo,
    GoogleApi,
    /// <summary>Microsoft retired the Bing Search APIs in August 2025; kept for existing keys only.</summary>
    BingApi,
}

/// <summary>Adapts the event-based SearcherCore engines (shared with the WinForms app) to <see cref="IDocumentSearchProvider"/>.</summary>
public sealed class SearchEngineProvider : IDocumentSearchProvider
{
    private readonly Func<LinkSearcher> factory;

    public string Name { get; }

    public event Action<string>? Log;

    private SearchEngineProvider(string name, Func<LinkSearcher> factory)
    {
        Name = name;
        this.factory = factory;
    }

    public static SearchEngineProvider Create(SearchEngine engine, string? apiKey = null, string? googleCx = null) => engine switch
    {
        SearchEngine.GoogleWeb => new("Google", () => new GoogleWebSearcher()),
        SearchEngine.BingWeb => new("Bing", () => new BingWebSearcher()),
        SearchEngine.DuckDuckGo => new("DuckDuckGo", () => new DuckduckgoWebSearcher()),
        SearchEngine.GoogleApi => new("Google API", () => new GoogleAPISearcher(Require(apiKey, "Google API key"), Require(googleCx, "Google custom search engine id (cx)"))),
        SearchEngine.BingApi => new("Bing API", () => new BingAPISearcher(Require(apiKey, "Bing API key"))),
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };

    private static string Require(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{what} is required.") : value;

    public async IAsyncEnumerable<Uri> SearchAsync(string domain, IReadOnlyCollection<string> extensions, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new InvalidOperationException(Foca.Core.Model.Project.DomainRequiredMessage);

        var channel = Channel.CreateUnbounded<Uri>(new UnboundedChannelOptions { SingleReader = true });
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        LinkSearcher searcher = factory();
        searcher.ItemsFoundEvent += (_, e) =>
        {
            foreach (Uri uri in e.Data)
                channel.Writer.TryWrite(uri);
        };
        searcher.SearcherLogEvent += (_, e) => Log?.Invoke($"[{Name}] {e.Message}");
        searcher.SearcherChangeStateEvent += (_, e) => Log?.Invoke($"[{Name}] {e.Message}");

        string[] filter = extensions.Select(x => x.TrimStart('.').ToLowerInvariant()).ToArray();
        Task<int> search = searcher.SearchBySite(cts, domain.Trim(), filter);
        _ = search.ContinueWith(t => channel.Writer.TryComplete(t.IsFaulted ? t.Exception!.GetBaseException() : null), TaskScheduler.Default);

        try
        {
            await foreach (Uri uri in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // Engines repeat results across pages and queries
                if (seen.Add(uri.AbsoluteUri))
                    yield return uri;
            }
        }
        finally
        {
            cts.Cancel();
            searcher.Dispose();
        }
    }
}
