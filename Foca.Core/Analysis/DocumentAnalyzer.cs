using Foca.Core.Model;
using MetadataExtractCore.Diagrams;
using MetadataExtractCore.Extractors;
using MetadataExtractCore.Utilities;
using MetadataPath = MetadataExtractCore.Diagrams.Path;

namespace Foca.Core.Analysis;

/// <summary>Result of analysing one document.</summary>
public sealed class DocumentAnalysis
{
    public string Extension { get; init; } = "";
    public string? Title { get; init; }
    public DateTime? CreatedDate { get; init; }
    public DateTime? ModifiedDate { get; init; }
    public DateTime? PrintedDate { get; init; }
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
}

/// <summary>
/// Runs the MetadataExtractCore extractors and flattens their output, including embedded images
/// and old versions, into <see cref="Finding"/>s.
/// </summary>
public static class DocumentAnalyzer
{
    /// <summary>Hard cap on the bytes read from one document; extractors buffer the whole file in memory.</summary>
    public const long MaxDocumentBytes = 512L * 1024 * 1024;

    public static IReadOnlyCollection<string> SupportedExtensions => DocumentExtractor.SupportedExtensions;

    public static bool IsSupported(string extension) =>
        !string.IsNullOrWhiteSpace(extension) && DocumentExtractor.IsSupportedExtension(extension);

    /// <summary>
    /// Supported extension of a file: its own extension when supported, otherwise the type detected
    /// from its content (zip/OLE/PDF signatures), or null.
    /// </summary>
    public static string? ResolveExtension(string path)
    {
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (IsSupported(extension))
            return extension;
        try
        {
            using FileStream fs = File.OpenRead(path);
            string detected = Functions.GetFormatFile(fs);
            return IsSupported(detected) ? detected : null;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static DocumentAnalysis AnalyzeFile(string path, string? extension = null)
    {
        extension ??= ResolveExtension(path) ?? throw new NotSupportedException($"Unsupported document type: {path}");
        var info = new FileInfo(path);
        if (info.Length > MaxDocumentBytes)
            throw new InvalidDataException($"{path} is larger than {MaxDocumentBytes} bytes.");
        using FileStream fs = File.OpenRead(path);
        return Analyze(fs, extension);
    }

    public static DocumentAnalysis Analyze(Stream content, string extension)
    {
        using DocumentExtractor extractor = DocumentExtractor.Create(extension, content);
        FileMetadata metadata = extractor.AnalyzeFile() ?? new FileMetadata();

        var findings = new List<Finding>();
        Collect(metadata, null, findings);

        return new DocumentAnalysis
        {
            Extension = extension.ToLowerInvariant(),
            Title = string.IsNullOrWhiteSpace(metadata.Title) ? null : metadata.Title.Trim(),
            CreatedDate = metadata.Dates.CreationDate,
            ModifiedDate = metadata.Dates.ModificationDate,
            PrintedDate = metadata.Dates.PrintingDate,
            Findings = Deduplicate(findings),
        };
    }

    private static void Collect(FileMetadata m, string? context, List<Finding> into)
    {
        void Add(FindingKind kind, string? value, string? detail = null)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            string ctx = detail == null ? context ?? "" : string.IsNullOrEmpty(context) ? detail : context + " / " + detail;
            into.Add(new Finding
            {
                Kind = kind,
                Value = value.Trim(),
                NormalizedValue = FindingNormalizer.Normalize(kind, value),
                Context = string.IsNullOrEmpty(ctx) ? null : ctx,
            });
        }

        foreach (User u in m.Users.Where(u => u.IsValid()))
            Add(FindingKind.User, u.Value, string.IsNullOrWhiteSpace(u.Notes) ? null : u.Notes);
        foreach (Email e in m.Emails.Where(e => e.IsValid()))
            Add(FindingKind.Email, e.Value);
        foreach (MetadataPath p in m.Paths.Where(p => p.IsValid()))
        {
            bool isLink = Uri.TryCreate(p.Value, UriKind.Absolute, out Uri? uri) && !uri.IsFile && !uri.IsUnc;
            Add(isLink ? FindingKind.Link : FindingKind.Path, p.Value);
        }
        foreach (Printer p in m.Printers.Where(p => p.IsValid()))
            Add(FindingKind.Printer, p.Value);
        foreach (Application a in m.Applications.Where(a => a.IsValid()))
            Add(FindingKind.Software, a.Value);
        foreach (Server s in m.Servers.Where(s => s.IsValid()))
            Add(FindingKind.Server, s.Value, string.IsNullOrWhiteSpace(s.Source) ? null : s.Source);
        foreach (Password p in m.Passwords.Where(p => p.IsValid()))
            Add(FindingKind.Password, p.Value, string.IsNullOrWhiteSpace(p.Type) ? null : p.Type);
        foreach (History h in m.History.Where(h => h.IsValid()))
            Add(FindingKind.User, h.Value, "revision history");

        Add(FindingKind.OperatingSystem, m.OperatingSystem);
        Add(FindingKind.Company, m.Company);
        Add(FindingKind.Template, m.Template);
        if (m.GPS != null)
            Add(FindingKind.GeoLocation, m.GPS.Value);

        foreach (KeyValuePair<string, FileMetadata> image in m.EmbeddedImages)
            Collect(image.Value, Join(context, "embedded image " + image.Key), into);

        foreach (OldVersion version in m.OldVersions)
        {
            Add(FindingKind.User, version.Author, "old version " + version.Value);
            if (version.Metadata != null)
                Collect(version.Metadata, Join(context, "old version " + version.Value), into);
        }
    }

    private static string Join(string? context, string part) => string.IsNullOrEmpty(context) ? part : context + " / " + part;

    /// <summary>
    /// One finding per kind and normalised value. The extractors copy users and software of embedded
    /// images up to the document without saying so; keep the most specific context (the longest path,
    /// e.g. "embedded image image1.jpeg / Artist" rather than "Artist").
    /// </summary>
    private static IReadOnlyList<Finding> Deduplicate(List<Finding> findings) =>
        findings
            .Where(f => f.NormalizedValue.Length > 0)
            .GroupBy(f => (f.Kind, f.NormalizedValue))
            .Select(g => g.OrderByDescending(f => f.Context?.Length ?? -1).First())
            .ToList();
}
