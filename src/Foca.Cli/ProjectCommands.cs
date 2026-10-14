using System.CommandLine;
using Foca.Core.Model;
using Foca.Data;

namespace Foca.Cli;

/// <summary>foca project list|create|show|set-domain|delete</summary>
internal static class ProjectCommands
{
    public static Command Create(StoreFactory stores)
    {
        var command = new Command("project", "Manage projects");
        command.Subcommands.Add(List(stores));
        command.Subcommands.Add(CreateProject(stores));
        command.Subcommands.Add(Show(stores));
        command.Subcommands.Add(SetDomain(stores));
        command.Subcommands.Add(Delete(stores));
        return command;
    }

    private static Command List(StoreFactory stores)
    {
        var command = new Command("list", "List projects");
        command.SetAction((parse, ct) => Output.Run(async () =>
        {
            ProjectStore store = await stores.OpenAsync(parse, ct);
            foreach (Project p in await store.GetProjectsAsync(ct))
                Console.WriteLine($"{p.Id,4}  {p.Name}  [{(p.HasDomain ? p.Domain : "document analysis")}]");
            return 0;
        }));
        return command;
    }

    private static Command CreateProject(StoreFactory stores)
    {
        var name = new Argument<string>("name") { Description = "Project name" };
        var domain = new Option<string?>("--domain", "-d") { Description = "Domain to investigate; omit for a document analysis project" };
        var alternatives = new Option<string[]>("--alt") { Description = "Alternative domains", AllowMultipleArgumentsPerToken = true };
        var folder = new Option<string?>("--folder") { Description = "Folder for downloaded documents" };
        var notes = new Option<string?>("--notes") { Description = "Project notes" };
        var command = new Command("create", "Create a project") { name, domain, alternatives, folder, notes };
        command.SetAction((parse, ct) => Output.Run(async () =>
        {
            ProjectStore store = await stores.OpenAsync(parse, ct);
            string projectName = parse.GetValue(name)!;
            Project p = await store.CreateProjectAsync(new Project
            {
                Name = projectName,
                Domain = parse.GetValue(domain),
                AlternativeDomains = (parse.GetValue(alternatives) ?? Array.Empty<string>()).Select(a => a.Trim().ToLowerInvariant()).ToList(),
                DownloadFolder = parse.GetValue(folder) ?? DefaultDownloadFolder(projectName),
                Notes = parse.GetValue(notes) ?? "",
            }, ct);
            Console.WriteLine($"Created project {p.Id} '{p.Name}' ({p.Mode}).");
            return 0;
        }));
        return command;
    }

    public static string DefaultDownloadFolder(string projectName)
    {
        string safe = string.Concat(projectName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FOCA", "downloads", safe);
    }

    private static Command Show(StoreFactory stores)
    {
        var name = new Argument<string>("project") { Description = "Project name or id" };
        var command = new Command("show", "Show a project and its metadata summary") { name };
        command.SetAction((parse, ct) => Output.Run(async () =>
        {
            (ProjectStore store, Project p) = await stores.OpenProjectAsync(parse, parse.GetValue(name)!, ct);
            Console.WriteLine($"Project:  {p.Name} (id {p.Id})");
            Console.WriteLine($"Mode:     {p.Mode}");
            if (p.HasDomain)
                Console.WriteLine($"Domain:   {p.Domain}" + (p.AlternativeDomains.Count > 0 ? " + " + string.Join(", ", p.AlternativeDomains) : ""));
            Console.WriteLine($"Folder:   {p.DownloadFolder}");
            Console.WriteLine();
            Output.WriteSummary(Console.Out, await store.GetDocumentsAsync(p.Id, ct));
            return 0;
        }));
        return command;
    }

    private static Command SetDomain(StoreFactory stores)
    {
        var name = new Argument<string>("project") { Description = "Project name or id" };
        var domain = new Argument<string>("domain") { Description = "Domain to investigate" };
        var command = new Command("set-domain", "Add or change the domain of a project (turns a document analysis project into a reconnaissance one)") { name, domain };
        command.SetAction((parse, ct) => Output.Run(async () =>
        {
            (ProjectStore store, Project p) = await stores.OpenProjectAsync(parse, parse.GetValue(name)!, ct);
            p.Domain = parse.GetValue(domain);
            await store.UpdateProjectAsync(p, ct);
            Console.WriteLine($"Project '{p.Name}' now investigates {p.Domain}.");
            return 0;
        }));
        return command;
    }

    private static Command Delete(StoreFactory stores)
    {
        var name = new Argument<string>("project") { Description = "Project name or id" };
        var confirm = new Option<bool>("--yes") { Description = "Confirm the deletion" };
        var command = new Command("delete", "Delete a project and its results (downloaded files are kept)") { name, confirm };
        command.SetAction((parse, ct) => Output.Run(async () =>
        {
            (ProjectStore store, Project p) = await stores.OpenProjectAsync(parse, parse.GetValue(name)!, ct);
            if (!parse.GetValue(confirm))
                throw new CliException($"This deletes project '{p.Name}' and all its results. Re-run with --yes to confirm.");
            await store.DeleteProjectAsync(p.Id, ct);
            Console.WriteLine($"Deleted project '{p.Name}'.");
            return 0;
        }));
        return command;
    }
}
