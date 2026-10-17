using System.Globalization;
using System.Text;
using Iryomera.Content.SDK;
using Iryomera.Localization;

namespace Iryomera.NameImporter;

/// <summary>
/// Exports approved records of permitted batches as one SDK content package with name pools and en/de pool labels.
/// Identical name strings and genders from several batches become one entry that keeps every attestation; statistics
/// stay per source. Differing romanizations are a conflict and block the export. Name formats are not exported.
/// </summary>
public sealed class NameExporter
{
    public async Task<NameReleaseReport> ExportAsync(NameWorkspace workspace, string output, string version, CancellationToken ct = default)
    {
        NameWorkspaceValidation.EnsureShape(workspace);
        if (!ContentValidator.IsVersion(version)) throw new ArgumentException("Package version must be canonical SemVer 2.");
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists; releases are never overwritten.");
        var selected = workspace.Batches
            .Where(b => NameWorkspaceValidation.Rights(workspace, b.Id) == RedistributionDecision.Permitted)
            .SelectMany(b => b.Records.Where(r => NameWorkspaceValidation.Status(workspace, b.Id, r.Id) == ReviewStatus.Approved).Select(r => (Batch: b, Record: r)))
            .ToList();
        if (selected.Count == 0) throw new InvalidOperationException("No approved records in batches with permitted redistribution.");
        var namespaces = selected.Select(x => x.Batch.Options.Namespace).Distinct(StringComparer.Ordinal).ToArray();
        if (namespaces.Length != 1) throw new InvalidOperationException("All exported batches must use the same namespace.");
        var ns = namespaces[0];
        var batches = selected.Select(x => x.Batch).DistinctBy(b => b.Id).OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
        var licenses = string.Join(" AND ", batches.Select(b => b.Options.License).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var approvals = batches.ToDictionary(b => b.Id, b => workspace.Reviews.Last(r => r.BatchId == b.Id && r.Status == ReviewStatus.Approved));
        var latest = approvals.Values.MaxBy(r => r.At)!;
        Provenance Approved(Provenance provenance, NameReviewDecision review) => provenance with { ReviewStatus = ReviewStatus.Approved, ReviewedBy = review.Reviewer, ReviewedAt = review.At };
        var sources = batches.ToDictionary(b => b.Id, b => new NamePoolSource
        {
            Key = SourceKey(b.Id),
            Provenance = Approved(new()
            {
                Source = b.Options.SourceUrl, ExternalId = b.Id, SourceVersion = b.Options.SourceVersion, License = b.Options.License, ImportedAt = b.Options.ImportedAt,
                Evidence = [new() { Citation = b.Options.Attribution, Uri = b.Options.SourceUrl }]
            }, approvals[b.Id])
        });
        var conflicts = new List<string>();
        var pools = new List<NamePool>();
        foreach (var group in selected.GroupBy(x => (x.Record.Country, x.Record.Region, x.Record.Part)).OrderBy(g => PoolLocal(g.Key.Country, g.Key.Region, g.Key.Part), StringComparer.Ordinal))
        {
            var local = PoolLocal(group.Key.Country, group.Key.Region, group.Key.Part);
            var entries = new List<NameEntry>();
            foreach (var name in group.GroupBy(x => (x.Record.Localized, x.Record.Gender)))
            {
                var romanizations = name.Where(x => x.Record.Romanized is not null).OrderBy(x => SourceKey(x.Batch.Id), StringComparer.Ordinal).ToArray();
                if (romanizations.Select(x => x.Record.Romanized).Distinct(StringComparer.Ordinal).Count() > 1)
                    conflicts.Add($"{local}: '{name.Key.Localized}' has differing romanizations.");
                var romanized = romanizations.FirstOrDefault();
                entries.Add(new()
                {
                    Key = NameIdentity.EntryKey(name.Key.Localized, name.Key.Gender), Localized = name.Key.Localized, Gender = name.Key.Gender,
                    Romanized = romanized.Record?.Romanized, RomanizationSourceKey = romanized.Record is null ? null : SourceKey(romanized.Batch.Id),
                    Attestations = name.Select(x => new NameAttestation
                    {
                        SourceKey = SourceKey(x.Batch.Id), ExternalId = x.Record.ExternalId ?? x.Record.Id, Rank = x.Record.Rank, Count = x.Record.Count,
                        Proportion = x.Record.Proportion, BirthYearFrom = x.Record.BirthYearFrom, BirthYearTo = x.Record.BirthYearTo
                    }).OrderBy(a => a.SourceKey, StringComparer.Ordinal).ThenBy(a => a.BirthYearFrom).ThenBy(a => a.ExternalId, StringComparer.Ordinal).ToList()
                });
            }
            pools.Add(new()
            {
                Id = ns + ":" + local, Version = version, NameKey = PoolKey(ns, local), Country = group.Key.Country, Region = group.Key.Region, Part = group.Key.Part,
                Provenance = Approved(new() { Source = "Iryomera NameImporter release", ExternalId = ns + ":" + local, SourceVersion = version, License = licenses, ImportedAt = batches.Max(b => b.Options.ImportedAt) }, latest),
                Sources = group.Select(x => x.Batch.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(id => sources[id]).ToList(),
                Entries = entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList()
            });
        }
        if (conflicts.Count > 0) throw new InvalidOperationException("Romanization conflicts; reject one of the records or fix the source: " + string.Join(" ", conflicts.Order(StringComparer.Ordinal)));
        var package = new ContentPackage
        {
            Manifest = new() { Id = ns + ":names", Namespace = ns, Version = version, License = licenses },
            NamePools = pools
        };
        var catalogs = new[] { "en", "de" }.Select(language => new LocalizationDefinition
        {
            Namespace = ns, Language = language,
            Entries = pools.ToDictionary(p => p.NameKey, p => Label(p, language), StringComparer.Ordinal)
        }).ToArray();
        var issues = ContentValidator.Validate(package).Concat(LocalizationValidator.Validate(catalogs, LocalizationValidator.GetContentKeys(package), ["en", "de"])).ToArray();
        if (issues.Length > 0) throw new InvalidOperationException("Exported package is invalid: " + string.Join("; ", issues.Select(i => i.Code + " " + i.Path)));
        var report = new NameReleaseReport
        {
            PackageId = package.Manifest.Id, Version = version,
            Batches = batches.Select(b => new NameReleaseBatch(b.Id, b.Options.Source, b.Options.SourceVersion, b.Options.License, b.InputSha256, selected.Count(x => x.Batch.Id == b.Id))).ToList(),
            Pools = pools.Select(p => new NameReleasePool(p.Id, p.Country, p.Region, p.Part, p.Entries.Count)).ToList()
        };
        var staging = Path.Combine(Path.GetDirectoryName(output)!, ".iryomera-names-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path.Combine(staging, "localization"));
        try
        {
            await Write(Path.Combine(staging, "content.json"), package, ct);
            foreach (var catalog in catalogs) await Write(Path.Combine(staging, "localization", catalog.Language + ".json"), catalog, ct);
            await Write(Path.Combine(staging, "release-report.json"), report, ct);
            await File.WriteAllTextAsync(Path.Combine(staging, "NOTICE.md"), Notice(batches), new UTF8Encoding(false), ct);
            ct.ThrowIfCancellationRequested();
            Directory.Move(staging, output);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        return report;
    }

    internal static string SourceKey(string batchId) => "b_" + batchId["batch_".Length..][..12];
    private static string PoolLocal(string country, string? region, NamePart part) =>
        (region is null ? country.ToLowerInvariant() : region.ToLowerInvariant().Replace('-', '_')) + "." + (part == NamePart.Given ? "given" : "family");
    private static string PoolKey(string ns, string local) => ns + ".pool." + local.Replace('.', '_') + ".name";
    private static string Label(NamePool pool, string language)
    {
        var scope = pool.Region ?? pool.Country;
        return (language, pool.Part) switch
        {
            ("de", NamePart.Given) => $"Vornamen ({scope})", ("de", _) => $"Nachnamen ({scope})",
            (_, NamePart.Given) => $"Given names ({scope})", _ => $"Family names ({scope})"
        };
    }
    private static async Task Write<T>(string path, T value, CancellationToken ct)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        await ContentJson.WriteAsync(output, value, ct);
    }
    private static string Notice(IEnumerable<NameBatch> batches)
    {
        var text = new StringBuilder("# Name data notices\n\nName data keeps its original licenses; the Iryomera MIT license covers only the tool. Names describe no real person.\n");
        foreach (var b in batches)
            text.Append(CultureInfo.InvariantCulture, $"\n## {b.Options.Source} {b.Options.SourceVersion}\n\n- Source: {b.Options.SourceUrl}\n- License: {b.Options.License}\n- Attribution: {b.Options.Attribution}\n- Input SHA-256: {b.InputSha256}\n- Batch: {b.Id}\n\n```text\n{b.Options.LicenseText.TrimEnd()}\n```\n");
        return text.ToString();
    }
}
