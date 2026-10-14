using System.CommandLine;
using Foca.Core.Model;
using Foca.Data;

namespace Foca.Cli;

/// <summary>foca export &lt;project&gt;: report of a saved project.</summary>
internal static class ExportCommand
{
    public static Command Create(StoreFactory stores)
    {
        var project = new Argument<string>("project") { Description = "Project name or id" };
        var format = new Option<string>("--format", "-f") { Description = "Report format", DefaultValueFactory = _ => "html" };
        format.AcceptOnlyFromAmong(Output.Formats);
        var output = new Option<string?>("--output", "-o") { Description = "Output file (default: console)" };
        var command = new Command("export", "Export a project report (HTML, JSON, CSV)") { project, format, output };
        command.SetAction((parse, ct) => Output.Run(async () =>
        {
            (ProjectStore store, Project p) = await stores.OpenProjectAsync(parse, parse.GetValue(project)!, ct);
            Output.Write(await store.GetDocumentsAsync(p.Id, ct), p, parse.GetValue(format)!, parse.GetValue(output));
            return 0;
        }));
        return command;
    }
}

/// <summary>foca import-legacy: bring projects over from the original FOCA's SQL Server database.</summary>
internal static class ImportLegacyCommand
{
    public static Command Create(StoreFactory stores)
    {
        var connection = new Option<string>("--connection", "-c")
        {
            Description = "Connection string of the old FOCA database",
            DefaultValueFactory = _ => @"Server=.\SQLEXPRESS;Database=Foca;Integrated Security=true;TrustServerCertificate=true",
        };
        var command = new Command("import-legacy", "Import projects from the original FOCA database (documents still on disk are re-analysed)") { connection };
        command.SetAction((parse, ct) => Output.Run(async () =>
        {
            ProjectStore store = await stores.OpenAsync(parse, ct);
            var importer = new LegacyImporter(parse.GetValue(connection)!, store);
            var log = new Progress<string>(m => Console.Error.WriteLine(m));
            foreach (LegacyImportResult r in await importer.ImportAllAsync(log, ct))
                Console.WriteLine($"{r.ProjectName}: {r.Documents} document(s), {r.Reanalyzed} re-analysed, {r.MissingFiles} downloaded file(s) no longer on disk");
            return 0;
        }));
        return command;
    }
}
