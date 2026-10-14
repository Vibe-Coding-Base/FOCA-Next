namespace Foca.Core.Model;

/// <summary>
/// A FOCA project. <see cref="Domain"/> is optional: document analysis projects work on local
/// files and folders, and a domain can be added later to enable web search and DNS reconnaissance.
/// </summary>
public class Project
{
    public const string DomainRequiredMessage =
        "This is a document analysis project without a domain. Add a domain to the project to use web search and DNS reconnaissance.";

    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Main domain under reconnaissance, or null for document analysis projects.</summary>
    public string? Domain { get; set; }

    public List<string> AlternativeDomains { get; set; } = new();

    /// <summary>Folder where downloaded documents are stored.</summary>
    public string DownloadFolder { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string Notes { get; set; } = "";

    public bool HasDomain => !string.IsNullOrWhiteSpace(Domain);

    public ProjectMode Mode => HasDomain ? ProjectMode.DomainReconnaissance : ProjectMode.DocumentAnalysis;
}

public enum ProjectMode
{
    DomainReconnaissance,
    DocumentAnalysis,
}
