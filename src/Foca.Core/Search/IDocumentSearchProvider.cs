namespace Foca.Core.Search;

/// <summary>Searches the web for documents of a domain.</summary>
public interface IDocumentSearchProvider
{
    string Name { get; }

    /// <summary>Streams document URLs on <paramref name="domain"/> with one of <paramref name="extensions"/> (".pdf", ...).</summary>
    IAsyncEnumerable<Uri> SearchAsync(string domain, IReadOnlyCollection<string> extensions, CancellationToken cancellationToken = default);
}
