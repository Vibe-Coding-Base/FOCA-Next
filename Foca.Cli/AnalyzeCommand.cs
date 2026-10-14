using System.CommandLine;
using Foca.Core.Analysis;
using Foca.Core.Model;
using Foca.Data;

namespace Foca.Cli;

/// <summary>
/// foca analyze &lt;paths...&gt;: document analysis without any domain. Results are printed, and saved when
/// --project is given (the project is created in document analysis mode if it does not exist).
/// </summary>
internal static class AnalyzeCommand
{
    public static Command Create(StoreFactory stores)
    {
        var paths = new Argument<string[]>("paths") { Description = "Files or folders to analyse", Arity = ArgumentArity.OneOrMore };
        var recursive = new Option<bool>("--recursive", "-r") { Description = "Include subfolders" };
        var project = new Option<string?>("--project", "-p") { Description = "Save the results in this project (created if missing)" };
        var format = new Option<string>("--format", "-f") { Description = "Output format", DefaultValueFactory = _ => "table" };
        format.AcceptOnlyFromAmong(Output.Formats);
        var output = new Option<string?>("--output", "-o") { Description = "Write the report to a file instead of the console" };
        var parallel = new Option<int>("--parallel") { Description = "Documents analysed concurrently", DefaultValueFactory = _ => Environment.ProcessorCount };

        var command = new Command("analyze", "Extract metadata from local documents and folders (no domain needed)")
        {
            paths, recursive, project, format, output, parallel,
        };

        command.SetAction((parse, ct) => Output.Run(async () =>
        {
            var files = new List<ScannedFile>();
            foreach (string path in parse.GetValue(paths)!)
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                    throw new CliException($"Not found: {path}");
                files.AddRange(DocumentScanner.Scan(path, parse.GetValue(recursive), (dir, e) => Console.Error.WriteLine($"skipped {dir}: {e.Message}")));
            }
            files = files.DistinctBy(f => Path.GetFullPath(f.Path)).ToList();
            if (files.Count == 0)
                throw new CliException("No supported documents found.");

            Console.Error.WriteLine($"Analysing {files.Count} document(s)...");
            var progress = new Progress<AnalysisProgress>(p =>
            {
                if (!Console.IsErrorRedirected)
                    Console.Error.Write($"\r{p.Completed}/{p.Total}");
            });
            IReadOnlyList<Document> documents = await AnalysisPipeline.AnalyzeAsync(files, 0,
                new AnalysisOptions { MaxDegreeOfParallelism = parse.GetValue(parallel) }, progress, ct);
            if (!Console.IsErrorRedirected)
                Console.Error.WriteLine();

            var target = new Project { Name = "Ad hoc analysis" };
            string? projectName = parse.GetValue(project);
            if (projectName != null)
            {
                ProjectStore store = await stores.OpenAsync(parse, ct);
                target = await store.FindProjectAsync(projectName, ct)
                    ?? await store.CreateProjectAsync(new Project { Name = projectName }, ct);
                await store.SaveDocumentsAsync(target.Id, documents, ct);
                Console.Error.WriteLine($"Saved {documents.Count} document(s) to project '{target.Name}'.");
            }

            Output.Write(documents, target, parse.GetValue(format)!, parse.GetValue(output));
            return documents.Any(d => d.Status == DocumentStatus.Failed) ? 2 : 0;
        }));
        return command;
    }
}
