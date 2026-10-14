using Foca.Core.Model;

namespace Foca.Core.Reporting;

/// <summary>One distinct value across the project, e.g. a user and the documents that leak it.</summary>
public sealed record SummaryEntry(FindingKind Kind, string Value, string NormalizedValue, IReadOnlyList<string> Variants, IReadOnlyList<string> Documents)
{
    public int DocumentCount => Documents.Count;
}

/// <summary>Project-wide view of the findings, the equivalent of the WinForms "Metadata summary" node.</summary>
public static class MetadataSummary
{
    public static IReadOnlyList<SummaryEntry> Build(IEnumerable<Document> documents)
    {
        return documents
            .Where(d => d.Status == DocumentStatus.Analyzed)
            .SelectMany(d => d.Findings.Select(f => (Finding: f, Document: d.LocalPath ?? d.Source)))
            .GroupBy(x => (x.Finding.Kind, x.Finding.NormalizedValue))
            .Select(g =>
            {
                List<string> variants = g.Select(x => x.Finding.Value).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();
                // Display the spelling that appears most often
                string display = g.GroupBy(x => x.Finding.Value).OrderByDescending(v => v.Count()).ThenBy(v => v.Key, StringComparer.Ordinal).First().Key;
                List<string> docs = g.Select(x => x.Document).Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();
                return new SummaryEntry(g.Key.Kind, display, g.Key.NormalizedValue, variants, docs);
            })
            .OrderBy(e => e.Kind)
            .ThenByDescending(e => e.DocumentCount)
            .ThenBy(e => e.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
