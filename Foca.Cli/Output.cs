using Foca.Core.Model;
using Foca.Core.Reporting;

namespace Foca.Cli;

internal static class Output
{
    /// <summary>Runs a command body, turning expected failures into a message and exit code 1.</summary>
    public static async Task<int> Run(Func<Task<int>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception e) when (e is CliException || e is InvalidOperationException || e is ArgumentException || e is IOException
                                  || e is UnauthorizedAccessException || e is System.Data.Common.DbException || e is HttpRequestException)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
    }

    /// <summary>Human readable summary, grouped like the "Metadata summary" of the desktop application.</summary>
    public static void WriteSummary(TextWriter w, IReadOnlyList<Document> documents)
    {
        int analyzed = documents.Count(d => d.Status == DocumentStatus.Analyzed);
        int failed = documents.Count(d => d.Status == DocumentStatus.Failed);
        w.WriteLine($"Documents: {documents.Count} ({analyzed} analysed, {failed} failed)");

        foreach (IGrouping<FindingKind, SummaryEntry> group in MetadataSummary.Build(documents).GroupBy(e => e.Kind))
        {
            w.WriteLine();
            w.WriteLine($"{group.Key} ({group.Count()})");
            foreach (SummaryEntry entry in group)
            {
                string variants = entry.Variants.Count > 1 ? "  [" + string.Join(", ", entry.Variants) + "]" : "";
                w.WriteLine($"  {entry.Value}  - {entry.DocumentCount} document(s){variants}");
            }
        }

        foreach (Document d in documents.Where(d => d.Status == DocumentStatus.Failed))
            Console.Error.WriteLine($"failed: {d.LocalPath ?? d.Source}: {d.Error}");
    }

    public static void Write(IReadOnlyList<Document> documents, Project project, string format, string? outputPath)
    {
        TextWriter writer = outputPath == null ? Console.Out : new StreamWriter(outputPath, append: false, new System.Text.UTF8Encoding(false));
        try
        {
            if (format == "table")
                WriteSummary(writer, documents);
            else
                ReportWriter.Write(writer, Enum.Parse<ReportFormat>(format, ignoreCase: true), project, documents);
        }
        finally
        {
            if (outputPath != null)
            {
                writer.Dispose();
                Console.Error.WriteLine($"Report written to {Path.GetFullPath(outputPath)}");
            }
        }
    }

    public static readonly string[] Formats = { "table", "json", "csv", "html" };
}
