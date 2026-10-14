using System.Collections.Concurrent;
using System.CommandLine;
using Foca.Core.Analysis;
using Foca.Core.Model;
using Foca.Core.Search;
using Foca.Core.Web;
using Foca.Data;

namespace Foca.Cli;

/// <summary>foca search &lt;project&gt;: find, download and analyse the documents published by the project's domain.</summary>
internal static class SearchCommand
{
    private static readonly string[] DefaultExtensions = { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp" };

    public static Command Create(StoreFactory stores)
    {
        var project = new Argument<string>("project") { Description = "Project name or id (must have a domain)" };
        var engines = new Option<SearchEngine[]>("--engine", "-e")
        {
            Description = "Search engines (repeatable)",
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => new[] { SearchEngine.DuckDuckGo, SearchEngine.BingWeb },
        };
        var wayback = new Option<bool>("--wayback") { Description = "Also list documents archived by the Internet Archive" };
        var extensions = new Option<string[]>("--ext") { Description = "File types to search", AllowMultipleArgumentsPerToken = true, DefaultValueFactory = _ => DefaultExtensions };
        var download = new Option<bool>("--download") { Description = "Download and analyse the documents found" };
        var concurrency = new Option<int>("--concurrency") { Description = "Parallel downloads", DefaultValueFactory = _ => 4 };
        var apiKey = new Option<string?>("--api-key") { Description = "Key for Google API / Bing API engines" };
        var googleCx = new Option<string?>("--google-cx") { Description = "Google custom search engine id" };

        var command = new Command("search", "Search the web for documents of the project's domain")
        {
            project, engines, wayback, extensions, download, concurrency, apiKey, googleCx,
        };

        command.SetAction((parse, ct) => Output.Run(async () =>
        {
            (ProjectStore store, Project p) = await stores.OpenProjectAsync(parse, parse.GetValue(project)!, ct);
            if (!p.HasDomain)
                throw new CliException(Project.DomainRequiredMessage + $" Use: foca project set-domain \"{p.Name}\" <domain>");

            string[] exts = parse.GetValue(extensions)!;
            using HttpClient services = TargetHttpClient.ForServices();
            var providers = new List<IDocumentSearchProvider>();
            foreach (SearchEngine engine in parse.GetValue(engines)!)
            {
                SearchEngineProvider provider = SearchEngineProvider.Create(engine, parse.GetValue(apiKey), parse.GetValue(googleCx));
                provider.Log += message => Console.Error.WriteLine(message);
                providers.Add(provider);
            }
            if (parse.GetValue(wayback))
                providers.Add(new WaybackProvider(services));

            var found = new ConcurrentDictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            foreach (string domain in new[] { p.Domain! }.Concat(p.AlternativeDomains))
            {
                foreach (IDocumentSearchProvider provider in providers)
                {
                    Console.Error.WriteLine($"Searching {domain} with {provider.Name}...");
                    try
                    {
                        await foreach (Uri uri in provider.SearchAsync(domain, exts, ct))
                        {
                            if (found.TryAdd(uri.AbsoluteUri, uri))
                                Console.WriteLine(uri.AbsoluteUri);
                        }
                    }
                    catch (Exception e) when (e is HttpRequestException || e is InvalidOperationException || e is ArgumentException || e is System.Text.Json.JsonException)
                    {
                        // One engine failing (blocked, retired API, bad key) must not stop the others
                        Console.Error.WriteLine($"{provider.Name}: {e.Message}");
                    }
                }
            }
            Console.Error.WriteLine($"{found.Count} document URL(s) found.");

            List<Document> documents = found.Values
                .Select(u => new Document { Source = u.AbsoluteUri, Extension = Path.GetExtension(u.AbsolutePath).ToLowerInvariant(), Status = DocumentStatus.Discovered })
                .ToList();

            if (parse.GetValue(download) && documents.Count > 0)
                documents = await DownloadAndAnalyzeAsync(p, documents, services, parse.GetValue(concurrency), ct);

            await store.SaveDocumentsAsync(p.Id, documents, ct);
            Console.Error.WriteLine($"Saved to project '{p.Name}'. Report: foca export \"{p.Name}\" -f html -o report.html");
            return 0;
        }));
        return command;
    }

    private static async Task<List<Document>> DownloadAndAnalyzeAsync(Project p, List<Document> documents, HttpClient services, int concurrency, CancellationToken ct)
    {
        string folder = string.IsNullOrWhiteSpace(p.DownloadFolder) ? ProjectCommands.DefaultDownloadFolder(p.Name) : p.DownloadFolder;
        using HttpClient targets = TargetHttpClient.ForTargets();
        var toTarget = new DocumentDownloader(targets, folder);
        // Archived copies come from the Internet Archive, which has a valid certificate
        var toArchive = new DocumentDownloader(services, folder);
        int done = 0;

        await Parallel.ForEachAsync(documents, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, concurrency), CancellationToken = ct }, async (doc, token) =>
        {
            var url = new Uri(doc.Source);
            DownloadResult result = await (WaybackProvider.OriginalOf(url) != null ? toArchive : toTarget).DownloadAsync(url, token);
            if (result.Succeeded)
            {
                doc.LocalPath = result.LocalPath;
                doc.Extension = result.Extension!;
                doc.Size = result.Size;
                doc.Sha256 = result.Sha256;
                doc.Status = DocumentStatus.Downloaded;
            }
            else
            {
                doc.LocalPath = result.LocalPath;
                doc.Status = DocumentStatus.Failed;
                doc.Error = result.Error;
            }
            Console.Error.WriteLine($"[{Interlocked.Increment(ref done)}/{documents.Count}] {(result.Succeeded ? "ok  " : "fail")} {doc.Source}{(result.Succeeded ? "" : ": " + result.Error)}");
        });

        List<Document> downloaded = documents.Where(d => d.Status == DocumentStatus.Downloaded).ToList();
        IReadOnlyList<Document> analyzed = await AnalysisPipeline.AnalyzeAsync(
            downloaded.Select(d => new ScannedFile(d.LocalPath!, d.Extension, d.Size)).ToList(), p.Id, cancellationToken: ct);
        for (int i = 0; i < downloaded.Count; i++)
        {
            Document source = downloaded[i];
            Document result = analyzed[i];
            result.Source = source.Source;
            result.DiscoveredAt = source.DiscoveredAt;
        }
        return documents.Where(d => d.Status != DocumentStatus.Downloaded).Concat(analyzed).ToList();
    }
}
