using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.Localization;
using NuGet.Versioning;

namespace Iryomera.ModKit;

/// <summary>Validates local content and data assets; never loads models, code or external URLs.</summary>
public sealed class ModValidator
{
    public async Task<ModValidationResult> ValidateAsync(string directory, IEnumerable<ContentPackage>? dependencies = null,
        string? gameVersion = null, PackageLimits? limits = null, CancellationToken ct = default)
    {
        limits ??= new(); limits.EnsureValid();
        var project = await PackageFiles.ReadJsonAsync(directory, "mod.json", limits, input => ModDocuments.ReadAsync<ModProject>(input, ct));
        var manifest = project.Manifest; var installed = (dependencies ?? []).ToArray();
        var issues = ManifestValidator.Validate(manifest, installed).ToList();
        void Error(string code, string path, string message) => issues.Add(new(code, path, message));
        if (gameVersion is not null && (!NuGetVersion.TryParse(gameVersion, out var game) || !VersionRange.TryParse(manifest.CompatibleGameVersions, out var range) || !range.Satisfies(game)))
            Error("game-version", manifest.Id, "Game version does not satisfy the declared compatibility range.");
        if (project.Files.Count + 2 > limits.MaxEntries) throw new InvalidDataException("File count exceeds package limit.");
        PackageFiles.CheckPaths(project.Files.Select(f => f.Path).Concat(["mod.json", "checksums.json"]));
        var files = project.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var contentFiles = project.Files.Where(f => f.Kind is ModFileKind.Content or ModFileKind.Localization).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        if (!contentFiles.SetEquals(manifest.ContentFiles)) Error("content-inventory", "mod.json", "Manifest contentFiles must equal the content/localization inventory.");
        var content = new ContentPackage { Manifest = new()
        { Id = manifest.Id, Namespace = manifest.Namespace, Version = manifest.Version, License = manifest.License, Dependencies = manifest.Dependencies } };
        var catalogs = new List<LocalizationDefinition>(); long total = new FileInfo(PackageFiles.PathFor(directory, "mod.json")).Length;
        foreach (var file in project.Files)
        {
            ct.ThrowIfCancellationRequested();
            var path = PackageFiles.PathFor(directory, file.Path); var length = new FileInfo(path).Length;
            if (length > limits.MaxFileBytes || length > limits.MaxExpandedBytes - total) throw new InvalidDataException("Package exceeds file/expanded size limit.");
            total += length;
            if (string.IsNullOrWhiteSpace(file.License)) Error("file-license", file.Path, "Original file license is required.");
            switch (file.Kind)
            {
                case ModFileKind.Content:
                    RequireExtension(file.Path, ".json");
                    var part = await PackageFiles.ReadJsonAsync(directory, file.Path, limits, input => ContentDocuments.ReadValidatedAsync<ContentPackage>(input, _ => [], cancellationToken: ct));
                    if (!part.IsValid) { issues.AddRange(part.Issues.Select(i => i with { Path = file.Path + ":" + i.Path })); break; }
                    if (!SameManifest(part.Value!.Manifest, content.Manifest)) Error("package-manifest", file.Path, "Content identity, namespace, version, license and dependencies must match the mod manifest.");
                    content = content with { Extensions = MergeExtensions(content.Extensions, part.Value.Extensions), Manifest = content.Manifest with { Extensions = MergeExtensions(content.Manifest.Extensions, part.Value.Manifest.Extensions) } };
                    content.Definitions.AddRange(part.Value.Definitions); content.Relations.AddRange(part.Value.Relations);
                    content.MedicalAnnotations.AddRange(part.Value.MedicalAnnotations); content.ReferenceRanges.AddRange(part.Value.ReferenceRanges);
                    content.DiagnosticRecommendations.AddRange(part.Value.DiagnosticRecommendations); content.Gameplay.AddRange(part.Value.Gameplay);
                    if (part.Value.DiagnosticSimulation is { } sim)
                    {
                        content = content with { DiagnosticSimulation = content.DiagnosticSimulation ?? new() };
                        content = content with { DiagnosticSimulation = content.DiagnosticSimulation with { Extensions = MergeExtensions(content.DiagnosticSimulation.Extensions, sim.Extensions) } };
                        content.DiagnosticSimulation.ObservationDistributions.AddRange(sim.ObservationDistributions);
                        content.DiagnosticSimulation.Manifestations.AddRange(sim.Manifestations); content.DiagnosticSimulation.ExaminationPolicies.AddRange(sim.ExaminationPolicies);
                    }
                    break;
                case ModFileKind.Localization:
                    RequireExtension(file.Path, ".json");
                    var catalog = await PackageFiles.ReadJsonAsync(directory, file.Path, limits, input => ContentDocuments.ReadValidatedAsync<LocalizationDefinition>(input, cancellationToken: ct));
                    if (!catalog.IsValid) issues.AddRange(catalog.Issues); else catalogs.Add(catalog.Value!);
                    break;
                case ModFileKind.ModelGlb:
                case ModFileKind.Texture:
                case ModFileKind.Icon:
                    await AssetFiles.ValidateAsync(path, file.Kind, limits, ct); break;
                case ModFileKind.Notice: RequireExtension(file.Path, ".txt", ".md"); break;
                default: throw new InvalidDataException("Unknown data file kind.");
            }
        }
        issues.AddRange(ContentValidator.Validate(content, installed));
        // The ModKit profile treats namespaced resources as content references without changing legacy SDK symbolic-resource semantics.
        var packages = installed.Append(content).GroupBy(p => p.Manifest.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var visible = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visible.Add(id) && packages.TryGetValue(id, out var package)) foreach (var dependency in package.Manifest.Dependencies) Visit(dependency.PackageId);
        }
        Visit(content.Manifest.Id);
        var resources = packages.Values.SelectMany(p => p.Definitions.Select(d => (Definition: d, Package: p.Manifest.Id))).GroupBy(x => x.Definition.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Package, StringComparer.Ordinal);
        foreach (var balance in content.Gameplay)
            foreach (var resource in balance.RequiredResources.Keys.Where(k => k.Contains(':')))
            {
                if (!resources.TryGetValue(resource, out var owner)) Error("resource-reference", balance.DefinitionId, "Unknown namespaced resource: " + resource);
                else if (!visible.Contains(owner)) Error("undeclared-dependency", balance.DefinitionId, "Resource requires a declared package dependency: " + resource);
            }
        issues.AddRange(ContentLocalizationValidator.Validate(content, catalogs, [manifest.NameKey], manifest.SupportedLanguages.Concat(["en", "de"]).Distinct(StringComparer.Ordinal))
            .Where(i => i.Code != "unused-key"));
        if (catalogs.Any(c => c.Namespace != manifest.Namespace) || !catalogs.Select(c => c.Language).ToHashSet(StringComparer.Ordinal).SetEquals(manifest.SupportedLanguages))
            Error("catalog-inventory", "mod.json", "Catalog namespace/languages must exactly match the mod declaration.");
        foreach (var definition in content.Definitions)
            foreach (var asset in definition.Assets ?? [])
                if (!files.TryGetValue(asset.Path, out var file) || asset.Kind.ToString() != file.Kind.ToString() || asset.License != file.License || asset.Attribution != file.Attribution)
                    Error("asset-reference", definition.Id, "Asset path, kind, license and attribution must match a declared local file.");
        return new(project, content, catalogs, issues);

        Dictionary<string, JsonElement>? MergeExtensions(Dictionary<string, JsonElement>? previous, Dictionary<string, JsonElement>? next)
        {
            if (next is null) return previous;
            var merged = previous is null ? new Dictionary<string, JsonElement>(StringComparer.Ordinal) : new(previous, StringComparer.Ordinal);
            foreach (var (key, value) in next)
            {
                if (merged.TryGetValue(key, out var old) && !JsonElement.DeepEquals(old, value)) Error("extension-conflict", key, "Content files disagree on extension metadata.");
                else merged[key] = value;
            }
            return merged;
        }
    }

    internal static void RequireValid(ModValidationResult result)
    { if (!result.IsValid) throw new InvalidDataException(string.Join("\n", result.Issues.Select(i => i.Code + " " + i.Path + ": " + i.Message))); }
    internal static void RequireExtension(string path, params string[] extensions)
    { if (!extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Disallowed data file extension: " + path); }
    private static bool SameManifest(ContentPackageManifest a, ContentPackageManifest b) => a.Id == b.Id && a.Namespace == b.Namespace && a.Version == b.Version && a.License == b.License &&
        a.Dependencies.OrderBy(d => d.PackageId, StringComparer.Ordinal).Select(d => (d.PackageId, d.VersionRange)).SequenceEqual(b.Dependencies.OrderBy(d => d.PackageId, StringComparer.Ordinal).Select(d => (d.PackageId, d.VersionRange)));
}
