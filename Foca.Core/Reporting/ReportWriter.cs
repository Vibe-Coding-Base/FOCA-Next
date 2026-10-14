using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foca.Core.Model;

namespace Foca.Core.Reporting;

public enum ReportFormat
{
    Json,
    Csv,
    Html,
}

/// <summary>
/// Exports a project's documents and findings. Every value comes from untrusted documents, so HTML
/// output is encoded and CSV cells are protected against spreadsheet formula injection.
/// </summary>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Write(TextWriter writer, ReportFormat format, Project project, IReadOnlyList<Document> documents)
    {
        switch (format)
        {
            case ReportFormat.Json:
                WriteJson(writer, project, documents);
                break;
            case ReportFormat.Csv:
                WriteCsv(writer, documents);
                break;
            case ReportFormat.Html:
                WriteHtml(writer, project, documents);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void WriteJson(TextWriter writer, Project project, IReadOnlyList<Document> documents)
    {
        var report = new
        {
            project = new { project.Name, project.Domain, project.AlternativeDomains, Mode = project.Mode.ToString(), project.CreatedAt },
            generatedAt = DateTime.UtcNow,
            summary = MetadataSummary.Build(documents).Select(e => new { e.Kind, e.Value, e.Variants, e.DocumentCount, e.Documents }),
            documents = documents.Select(d => new
            {
                d.Source,
                d.LocalPath,
                d.Extension,
                d.Size,
                d.Sha256,
                Status = d.Status.ToString(),
                d.Error,
                d.Title,
                d.CreatedDate,
                d.ModifiedDate,
                d.PrintedDate,
                findings = d.Findings.Select(f => new { f.Kind, f.Value, f.Context }),
            }),
        };
        writer.Write(JsonSerializer.Serialize(report, JsonOptions));
        writer.WriteLine();
    }

    private static void WriteCsv(TextWriter writer, IReadOnlyList<Document> documents)
    {
        writer.WriteLine("document,extension,sha256,kind,value,context");
        foreach (Document d in documents)
        {
            foreach (Finding f in d.Findings)
            {
                writer.WriteLine(string.Join(",",
                    CsvCell(d.LocalPath ?? d.Source), CsvCell(d.Extension), CsvCell(d.Sha256),
                    CsvCell(f.Kind.ToString()), CsvCell(f.Value), CsvCell(f.Context)));
            }
        }
    }

    /// <summary>Quotes a CSV cell and neutralises leading characters that spreadsheets evaluate as formulas.</summary>
    public static string CsvCell(string? value)
    {
        string v = value ?? "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0]))
            v = "'" + v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    private static void WriteHtml(TextWriter writer, Project project, IReadOnlyList<Document> documents)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        IReadOnlyList<SummaryEntry> summary = MetadataSummary.Build(documents);

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">");
        sb.Append("<title>FOCA report - ").Append(E(project.Name)).AppendLine("</title>");
        sb.AppendLine("<style>body{font-family:system-ui,sans-serif;margin:2rem;color:#222}table{border-collapse:collapse;margin-bottom:2rem;width:100%}" +
                      "th,td{border:1px solid #ccc;padding:4px 8px;text-align:left;vertical-align:top;word-break:break-all}th{background:#f3f3f3}" +
                      "h2{border-bottom:2px solid #c2185b;padding-bottom:4px}</style></head><body>");
        sb.Append("<h1>").Append(E(project.Name)).AppendLine("</h1>");
        sb.Append("<p>Mode: ").Append(E(project.Mode.ToString()));
        if (project.HasDomain)
            sb.Append(" &middot; Domain: ").Append(E(project.Domain));
        sb.Append(" &middot; Documents: ").Append(documents.Count)
          .Append(" &middot; Generated: ").Append(E(DateTime.UtcNow.ToString("u"))).AppendLine("</p>");

        foreach (IGrouping<FindingKind, SummaryEntry> group in summary.GroupBy(e => e.Kind))
        {
            sb.Append("<h2>").Append(E(group.Key.ToString())).Append(" (").Append(group.Count()).AppendLine(")</h2>");
            sb.AppendLine("<table><tr><th>Value</th><th>Documents</th><th>Found in</th></tr>");
            foreach (SummaryEntry entry in group)
            {
                sb.Append("<tr><td>").Append(E(entry.Value));
                if (entry.Variants.Count > 1)
                    sb.Append("<br><small>").Append(E(string.Join(", ", entry.Variants))).Append("</small>");
                sb.Append("</td><td>").Append(entry.DocumentCount).Append("</td><td>")
                  .Append(string.Join("<br>", entry.Documents.Select(E))).AppendLine("</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<h2>Documents</h2><table><tr><th>Document</th><th>Type</th><th>Status</th><th>Created</th><th>Modified</th><th>SHA-256</th></tr>");
        foreach (Document d in documents)
        {
            sb.Append("<tr><td>").Append(E(d.LocalPath ?? d.Source)).Append("</td><td>").Append(E(d.Extension))
              .Append("</td><td>").Append(E(d.Status.ToString())).Append(d.Error == null ? "" : "<br><small>" + E(d.Error) + "</small>")
              .Append("</td><td>").Append(E(d.CreatedDate?.ToString("u"))).Append("</td><td>").Append(E(d.ModifiedDate?.ToString("u")))
              .Append("</td><td><small>").Append(E(d.Sha256)).AppendLine("</small></td></tr>");
        }
        sb.AppendLine("</table></body></html>");
        writer.Write(sb.ToString());
    }
}
