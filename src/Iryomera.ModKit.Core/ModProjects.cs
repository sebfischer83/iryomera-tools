using System.Text;
using System.Text.Json;
using Iryomera.Content.SDK;

namespace Iryomera.ModKit;

public sealed class ModProjects
{
    /// <summary>Creates own synthetic content from embedded templates in a new directory; does not approve medical content.</summary>
    public async Task CreateAsync(string output, string ns, string author, string name, CancellationToken ct = default)
    {
        if (!ContentFormat.IsValidNamespace(ns) || string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Valid namespace, author and name are required.");
        output = PackageFiles.NewDestination(output);
        var scratch = Path.Combine(Path.GetDirectoryName(output)!, ".iryomera-new-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        try
        {
            var assembly = typeof(ModProjects).Assembly;
            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Templates/", StringComparison.Ordinal)))
            {
                var relative = resource["Templates/".Length..].Replace('\\', '/');
                if (!PackageFiles.Safe(relative)) throw new InvalidDataException("Invalid bundled template path.");
                var path = Path.Combine(scratch, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var stream = assembly.GetManifestResourceStream(resource)!;
                if (relative.EndsWith(".json", StringComparison.Ordinal))
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8); var text = (await reader.ReadToEndAsync(ct)).Replace("example_mod", ns, StringComparison.Ordinal);
                    await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), ct);
                }
                else { await using var file = File.Create(path); await PackageFiles.CopyAsync(stream, file, relative, new PackageLimits().MaxFileBytes, ct); }
            }
            var projectPath = Path.Combine(scratch, "mod.json");
            var project = await PackageFiles.ReadJsonAsync(scratch, "mod.json", new(), s => ModDocuments.ReadAsync<ModProject>(s, ct));
            project = project with { Manifest = project.Manifest with { Author = author } };
            await File.WriteAllTextAsync(projectPath, JsonSerializer.Serialize(project, ContentJson.Options), new UTF8Encoding(false), ct);
            var enPath = Path.Combine(scratch, "localization/en.json");
            var en = JsonSerializer.Deserialize<LocalizationDefinition>(await File.ReadAllTextAsync(enPath, ct), ContentJson.Options)!;
            // A custom English name is not an invented German translation; the German synthetic template stays explicit.
            en.Entries[project.Manifest.NameKey] = name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("{", "\\{", StringComparison.Ordinal).Replace("}", "\\}", StringComparison.Ordinal);
            await File.WriteAllTextAsync(enPath, JsonSerializer.Serialize(en, ContentJson.Options), new UTF8Encoding(false), ct);
            ModValidator.RequireValid(await new ModValidator().ValidateAsync(scratch, ct: ct));
            ct.ThrowIfCancellationRequested(); Directory.Move(scratch, output);
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }

    /// <summary>Validates a candidate project before replacing one declared file atomically. Larger coordinated edits may use a text editor.</summary>
    public async Task EditAsync(string directory, string relative, string replacement, IEnumerable<ContentPackage>? dependencies = null,
        string? gameVersion = null, PackageLimits? limits = null, CancellationToken ct = default)
    {
        limits ??= new(); limits.EnsureValid();
        PackageFiles.NoLinks(directory); PackageFiles.NoLinks(replacement); PackageFiles.NoLinks(Path.Combine(directory, ".iryomera-mod.lock"));
        await using var projectLock = new FileStream(Path.Combine(directory, ".iryomera-mod.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var target = PackageFiles.PathFor(directory, relative);
        var original = await PackageFiles.ReadJsonAsync(directory, "mod.json", limits, s => ModDocuments.ReadAsync<ModProject>(s, ct));
        if (relative != "mod.json" && !original.Files.Any(f => f.Path == relative)) throw new ArgumentException("Edit only a declared file or mod.json.");
        var scratch = PackageFiles.Scratch(); var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var manifestSource = relative == "mod.json" ? replacement : PackageFiles.PathFor(directory, "mod.json");
            await using (var input = File.OpenRead(manifestSource)) await using (var output = File.Create(Path.Combine(scratch, "mod.json")))
                await PackageFiles.CopyAsync(input, output, "mod.json", Math.Min(limits.MaxJsonBytes, limits.MaxFileBytes), ct);
            var candidate = await PackageFiles.ReadJsonAsync(scratch, "mod.json", limits, s => ModDocuments.ReadAsync<ModProject>(s, ct));
            PackageFiles.CheckPaths(candidate.Files.Select(f => f.Path).Concat(["mod.json", "checksums.json"]));
            long total = new FileInfo(Path.Combine(scratch, "mod.json")).Length;
            if (candidate.Files.Count + 2 > limits.MaxEntries) throw new InvalidDataException("Too many files.");
            foreach (var file in candidate.Files)
            {
                var source = file.Path == relative ? replacement : PackageFiles.PathFor(directory, file.Path);
                var path = Path.Combine(scratch, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var input = File.OpenRead(source); await using var output = File.Create(path);
                total += (await PackageFiles.CopyAsync(input, output, file.Path, Math.Min(limits.MaxFileBytes, limits.MaxExpandedBytes - total), ct)).SizeBytes;
            }
            ModValidator.RequireValid(await new ModValidator().ValidateAsync(scratch, dependencies, gameVersion, limits, ct));
            await using (var input = File.OpenRead(PackageFiles.PathFor(scratch, relative)))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await PackageFiles.CopyAsync(input, output, relative, limits.MaxFileBytes, ct);
            PackageFiles.NoLinks(target); ct.ThrowIfCancellationRequested(); File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); Directory.Delete(scratch, true); }
    }
}
