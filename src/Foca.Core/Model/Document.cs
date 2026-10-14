namespace Foca.Core.Model;

public enum DocumentStatus
{
    /// <summary>Found by a search engine, not downloaded yet.</summary>
    Discovered,
    Downloaded,
    Analyzed,
    Failed,
}

/// <summary>A document of a project: found on the web, downloaded, or added from a local folder.</summary>
public class Document
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    /// <summary>Where the document was found (http/https URL), or the local path for local files.</summary>
    public string Source { get; set; } = "";

    /// <summary>Path of the local copy, when there is one.</summary>
    public string? LocalPath { get; set; }

    /// <summary>Lower-case extension with dot, possibly detected from content.</summary>
    public string Extension { get; set; } = "";

    public long Size { get; set; }

    /// <summary>SHA-256 of the content; identical documents found at different URLs share it.</summary>
    public string? Sha256 { get; set; }

    public DocumentStatus Status { get; set; }

    public string? Error { get; set; }

    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    public DateTime? AnalyzedAt { get; set; }

    public string? Title { get; set; }

    public DateTime? CreatedDate { get; set; }

    public DateTime? ModifiedDate { get; set; }

    public DateTime? PrintedDate { get; set; }

    public List<Finding> Findings { get; set; } = new();

    public bool IsLocal => !Uri.TryCreate(Source, UriKind.Absolute, out Uri? uri) || uri.IsFile;
}
