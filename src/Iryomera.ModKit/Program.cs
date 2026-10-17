using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using Iryomera.Content.SDK;

namespace Iryomera.ModKit;

public static class Program
{
    public static Task<int> Main(string[] args) => CreateCommand().Parse(args).InvokeAsync();
    public static RootCommand CreateCommand()
    {
        var root = new RootCommand("Iryomera ModKit: local data-only mods. Packaging grants no medical approval or redistribution rights.");
        var create = new Command("new", "Create an editable own synthetic example; author real content separately.");
        var newOutput = Required(create, "--output"); var ns = Required(create, "--namespace"); var author = Required(create, "--author"); var name = Required(create, "--name");
        create.SetAction((p, ct) => Guard(async () => { await new ModProjects().CreateAsync(p.GetValue(newOutput)!, p.GetValue(ns)!, p.GetValue(author)!, p.GetValue(name)!, ct); return 0; })); root.Subcommands.Add(create);
        foreach (var verb in new[] { "validate", "edit", "pack", "inspect", "unpack" })
        {
            var command = new Command(verb, verb switch { "validate" => "Validate local data, references, assets and en/de catalogs.", "edit" => "Validate and atomically replace one declared file.", "pack" => "Create a bounded ZIP with SHA-256 integrity index.", "inspect" => "Verify a ZIP completely without publishing a directory.", _ => "Verify and unpack into a new directory; never overwrite." });
            var input = Required(command, "--input");
            Option<string>? output = verb is "pack" or "unpack" ? Required(command, "--output") : null;
            Option<string>? path = verb == "edit" ? Required(command, "--path") : null;
            Option<string>? replacement = verb == "edit" ? Required(command, "--replacement") : null;
            var dependency = new Option<string[]>("--dependency") { Description = "Installed dependency ContentPackage JSON; repeat or supply several paths.", AllowMultipleArgumentsPerToken = true }; command.Options.Add(dependency);
            var game = new Option<string?>("--game-version") { Description = "Optional actual game version to check against the compatibility range." }; command.Options.Add(game);
            command.SetAction((p, ct) => Guard(async () =>
            {
                var dependencies = new List<ContentPackage>();
                foreach (var file in p.GetValue(dependency) ?? [])
                {
                    PackageFiles.NoLinks(file); await using var stream = File.OpenRead(file);
                    if (stream.Length > new PackageLimits().MaxJsonBytes) throw new InvalidDataException("Dependency JSON exceeds size limit.");
                    var package = await ContentDocuments.ReadValidatedAsync<ContentPackage>(stream, _ => [], cancellationToken: ct);
                    if (!package.IsValid) throw new InvalidDataException("Dependency JSON does not match the SDK schema."); dependencies.Add(package.Value!);
                }
                var source = p.GetValue(input)!; var version = p.GetValue(game); var archives = new ModArchive();
                switch (verb)
                {
                    case "validate":
                        var result = await new ModValidator().ValidateAsync(source, dependencies, version, ct: ct); Print(new { result.IsValid, result.Issues }); return result.IsValid ? 0 : 2;
                    case "edit": await new ModProjects().EditAsync(source, p.GetValue(path!)!, p.GetValue(replacement!)!, dependencies, version, ct: ct); break;
                    case "pack": await archives.PackAsync(source, p.GetValue(output!)!, dependencies, version, ct: ct); break;
                    case "inspect":
                        var inspected = await archives.InspectAsync(source, dependencies, version, ct: ct); // Catalog references are reported, never fetched or packaged; the game resolves them.
                        var catalogAssets = inspected.Content.Definitions.SelectMany(d => d.CatalogAssets ?? []).Select(c => c.CatalogId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                        Print(new { inspected.Project.Manifest, Files = inspected.Project.Files.Count, Definitions = inspected.Content.Definitions.Count, UnresolvedCatalogAssets = catalogAssets }); break;
                    case "unpack": await archives.UnpackAsync(source, p.GetValue(output!)!, dependencies, version, ct: ct); break;
                }
                return 0;
            })); root.Subcommands.Add(command);
        }
        return root;
    }
    private static Option<string> Required(Command command, string name)
    { var option = new Option<string>(name) { Required = true }; command.Options.Add(option); return option; }
    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, ContentJson.Options));
    private static async Task<int> Guard(Func<Task<int>> action)
    {
        try { return await action(); }
        catch (OperationCanceledException) { Console.Error.WriteLine("Canceled; no partial destination published."); return 130; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or NotSupportedException or InvalidOperationException)
        { Console.Error.WriteLine(error.Message); return 1; }
    }
}
