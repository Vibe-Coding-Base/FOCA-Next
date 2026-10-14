using System.Runtime.CompilerServices;
using System.Text.Json;
using Foca.Core.Analysis;
using Foca.Core.Model;
using Foca.Core.Reporting;
using Foca.Core.Search;
using Foca.Core.Web;
using Xunit;

namespace Foca.Core.Tests;

internal static class Corpus
{
    public static string Dir([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "corpus"));
}

public class NormalizerTests
{
    [Theory]
    [InlineData(@"ACME\jdoe", "jdoe")]
    [InlineData("jdoe@acme.com", "jdoe")]
    [InlineData("  JDoe ", "jdoe")]
    [InlineData("José Pérez", "jose perez")]
    [InlineData("Nguyễn Văn Đức", "nguyen van duc")]
    public void UsersGroupAcrossSpellings(string raw, string expected)
    {
        Assert.Equal(expected, FindingNormalizer.Normalize(FindingKind.User, raw));
    }

    [Theory]
    [InlineData(@"ACME\jdoe", "acme")]
    [InlineData("jdoe@acme.com", "acme.com")]
    [InlineData("jdoe", null)]
    public void AccountDomain(string raw, string? expected)
    {
        Assert.Equal(expected, FindingNormalizer.AccountDomain(raw));
    }

    [Fact]
    public void PathsIgnoreSlashStyleAndCase()
    {
        Assert.Equal(FindingNormalizer.Normalize(FindingKind.Path, @"C:\Users\Bob\"), FindingNormalizer.Normalize(FindingKind.Path, "c:/users/bob"));
    }
}

public class AnalysisTests
{
    [Fact]
    public async Task FolderAnalysisFindsLeaksAndDeduplicatesByContent()
    {
        string dir = Directory.CreateTempSubdirectory("foca-test").FullName;
        try
        {
            File.Copy(Path.Combine(Corpus.Dir(), "sample.docx"), Path.Combine(dir, "a.docx"));
            File.Copy(Path.Combine(Corpus.Dir(), "sample.docx"), Path.Combine(dir, "copy-of-a.docx"));
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.Copy(Path.Combine(Corpus.Dir(), "sample.odt"), Path.Combine(dir, "sub", "b.odt"));
            // ODT saved without extension: detected from content
            File.Copy(Path.Combine(Corpus.Dir(), "sample.odt"), Path.Combine(dir, "sub", "noext"));
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a document");

            List<ScannedFile> files = DocumentScanner.Scan(dir, recursive: true).ToList();
            Assert.Equal(4, files.Count);
            Assert.Contains(files, f => f.Path.EndsWith("noext") && f.Extension == ".odt");

            IReadOnlyList<Document> docs = await AnalysisPipeline.AnalyzeAsync(files, 1);
            Assert.All(docs, d => Assert.Equal(DocumentStatus.Analyzed, d.Status));
            Assert.Equal(2, docs.Select(d => d.Sha256).Distinct().Count());

            IReadOnlyList<SummaryEntry> summary = MetadataSummary.Build(docs);
            SummaryEntry alice = Assert.Single(summary, e => e.Kind == FindingKind.User && e.NormalizedValue == "alice.author");
            Assert.Equal(2, alice.DocumentCount);
            Assert.Contains(summary, e => e.Kind == FindingKind.Printer && e.Value.Contains("PRINTSRV01"));
            Assert.Contains(summary, e => e.Kind == FindingKind.Email && e.Value == "it-support@acme.local");
            // EXIF of the embedded image is reported with its context
            Assert.Contains(docs.SelectMany(d => d.Findings), f => f.Value == "exif.photographer" && f.Context != null && f.Context.Contains("embedded image"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CraftedDocumentFailsWithoutStoppingTheBatch()
    {
        string cyclic = Path.Combine(Corpus.Dir(), "cyclic-chain.doc");
        string good = Path.Combine(Corpus.Dir(), "sample.pdf");
        var files = new List<ScannedFile> { new(cyclic, ".doc", new FileInfo(cyclic).Length), new(good, ".pdf", new FileInfo(good).Length) };
        IReadOnlyList<Document> docs = await AnalysisPipeline.AnalyzeAsync(files, 1).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(DocumentStatus.Analyzed, docs[1].Status);
        Assert.Contains(docs[1].Findings, f => f.Kind == FindingKind.User && f.Value == "henry.pdfauthor");
    }
}

public class ReportTests
{
    private static IReadOnlyList<Document> Malicious() => new[]
    {
        new Document
        {
            Source = "https://acme.com/<script>alert(1)</script>.pdf",
            Status = DocumentStatus.Analyzed,
            Findings = { new Finding { Kind = FindingKind.User, Value = "<img src=x onerror=alert(1)>", NormalizedValue = "x" } },
        },
    };

    [Fact]
    public void HtmlReportEncodesDocumentValues()
    {
        var w = new StringWriter();
        ReportWriter.Write(w, ReportFormat.Html, new Project { Name = "<b>p</b>" }, Malicious());
        string html = w.ToString();
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
        Assert.Contains("Content-Security-Policy", html);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("+cmd", "\"'+cmd\"")]
    [InlineData("jdoe", "\"jdoe\"")]
    public void CsvCellsCannotBecomeFormulas(string value, string expected)
    {
        Assert.Equal(expected, ReportWriter.CsvCell(value));
    }

    [Fact]
    public void JsonReportIsValid()
    {
        var w = new StringWriter();
        ReportWriter.Write(w, ReportFormat.Json, new Project { Name = "p" }, Malicious());
        using JsonDocument json = JsonDocument.Parse(w.ToString());
        Assert.Equal("DocumentAnalysis", json.RootElement.GetProperty("project").GetProperty("Mode").GetString());
    }
}

public class WebTests
{
    [Theory]
    [InlineData("https://acme.com/docs/report.pdf", ".pdf", "acme.com_report.pdf")]
    [InlineData("https://acme.com/..%2F..%2Fetc%2Fpasswd", ".pdf", "acme.com_____etc_passwd.pdf")]
    [InlineData("https://acme.com/a%5C..%5Cevil.docx", ".docx", "acme.com_a___evil.docx")]
    [InlineData("https://acme.com/", null, "acme.com_index")]
    public void DownloadFileNamesCannotEscapeTheFolder(string url, string? ext, string expected)
    {
        string name = DocumentDownloader.SafeFileName(new Uri(url), ext);
        Assert.Equal(expected, name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("\\", name);
        Assert.DoesNotContain("..", name);
    }

    [Fact]
    public void WaybackCdxRowsBecomeArchivedCopies()
    {
        const string cdx = """[["timestamp","original"],["20190101000000","https://acme.com/old.pdf"],["bad","https://x"],["20200101000000","javascript:alert(1)"]]""";
        List<Uri> uris = WaybackProvider.ParseCdx(JsonDocument.Parse(cdx)).ToList();
        Uri archived = Assert.Single(uris);
        Assert.Equal("https://web.archive.org/web/20190101000000id_/https://acme.com/old.pdf", archived.AbsoluteUri);
        Assert.Equal("https://acme.com/old.pdf", WaybackProvider.OriginalOf(archived)!.AbsoluteUri);
    }

    [Fact]
    public void WaybackQueryEscapesExtensions()
    {
        string q = WaybackProvider.BuildQuery("acme.com", new[] { ".pdf", "docx" }, 100);
        Assert.Contains("url=acme.com", q);
        Assert.Contains("matchType=domain", q);
        Assert.Contains(Uri.EscapeDataString("(pdf|docx)$"), q);
    }

    [Fact]
    public async Task SearchRequiresADomain()
    {
        var provider = SearchEngineProvider.Create(SearchEngine.DuckDuckGo);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (Uri _ in provider.SearchAsync("", new[] { "pdf" })) { }
        });
    }
}
