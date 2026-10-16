using System.Globalization;
using System.Text.Json;
using Iryomera.Content.SDK;

namespace Iryomera.MedicalImporter;

/// <summary>Approved-only release builder. Validation and rights checks happen before creating any output.</summary>
public sealed class ReleaseExporter
{
    public async Task ExportAsync(ImportWorkspace workspace, string outputPath, IReadOnlyList<string>? requiredLanguages = null, CancellationToken ct = default)
    {
        var diagnostics = WorkspaceValidation.Validate(workspace);
        // Unresolved unapproved source assertions remain in staging. Structural/conflict errors never disappear silently.
        var fatal = diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error && x.Code != "unresolved-reference").ToArray();
        if (fatal.Length > 0) throw new InvalidDataException(string.Join(Environment.NewLine, fatal.Select(x => x.Code + ": " + x.RecordId + " " + x.Message)));
        var reviewIndex = WorkspaceValidation.ReviewIndex(workspace);
        bool Approved(string batch, string id) => reviewIndex.TryGetValue((batch, id), out var review) && review.Status == ReviewStatus.Approved;
        var terms = workspace.Batches.SelectMany(b => b.Terms.Select(t => (Term: t, Batch: b)))
            .Where(x => Approved(x.Batch.Id, x.Term.Id)).OrderBy(x => x.Term.Id, StringComparer.Ordinal).ToArray();
        var assertions = workspace.Batches.SelectMany(b => b.Assertions.Select(a => (Assertion: a, Batch: b)))
            .Where(x => Approved(x.Batch.Id, x.Assertion.Id)).OrderBy(x => x.Assertion.Id, StringComparer.Ordinal).ToArray();
        if (terms.Length == 0) throw new InvalidOperationException("No approved definitions to export. Import is not approval.");
        if (terms.Any(x => x.Term.Obsolete)) throw new InvalidDataException("Approved selection contains obsolete terms.");
        var batches = terms.Select(x => x.Batch).Concat(assertions.Select(x => x.Batch)).DistinctBy(x => x.Id).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        if (batches.Select(x => x.Options.Namespace).Distinct(StringComparer.Ordinal).Count() != 1) throw new InvalidDataException("One release package must have exactly one namespace.");
        foreach (var batch in batches)
            if (workspace.Rights.LastOrDefault(x => x.BatchId == batch.Id)?.Decision != RedistributionDecision.Permitted)
                throw new InvalidOperationException($"Batch {batch.Id} needs an explicit permitted redistribution decision and original notices before export.");
        var ns = batches[0].Options.Namespace;
        var package = new ContentPackage
        {
            Manifest = new() { Id = ns + ":medical_import", Namespace = ns, Version = "0.1.0", License = "LicenseRef-ImportedSources-see-LICENSES" }
        };
        var catalogs = new SortedDictionary<string, LocalizationDefinition>(StringComparer.Ordinal);
        foreach (var group in terms.GroupBy(x => x.Term.Id, StringComparer.Ordinal))
        {
            var (term, batch) = group.First();
            var provenance = WorkspaceValidation.ReviewedProvenance(workspace, batch, term.Id, term.Provenance, reviewIndex);
            ContentDefinition definition = term.Kind switch
            {
                ImportedTermKind.Disease => new DiseaseDefinition { Id = term.Id, NameKey = ImportIdentity.Key(term.Id), Version = "1.0.0", Provenance = provenance },
                ImportedTermKind.Finding => new FindingDefinition { Id = term.Id, NameKey = ImportIdentity.Key(term.Id), Version = "1.0.0", Provenance = provenance },
                ImportedTermKind.Examination => new ExaminationDefinition { Id = term.Id, NameKey = ImportIdentity.Key(term.Id), Version = "1.0.0", Provenance = provenance },
                ImportedTermKind.MedicalProcedure => new MedicalProcedureDefinition { Id = term.Id, NameKey = ImportIdentity.Key(term.Id), Version = "1.0.0", Provenance = provenance },
                _ => throw new InvalidDataException("Unknown imported term kind.")
            };
            definition = definition with
            {
                DescriptionKey = string.IsNullOrWhiteSpace(term.Description) ? null : ImportIdentity.Key(term.Id) + ".description",
                SynonymKeys = term.Synonyms.Select((_, i) => ImportIdentity.Key(term.Id) + ".synonym.s" + i.ToString(CultureInfo.InvariantCulture)).ToList(),
                Classifications = group.Select(claim => new ExternalIdentifier { System = claim.Term.ExternalId.Split(':')[0], Value = claim.Term.ExternalId,
                    Provenance = WorkspaceValidation.ReviewedProvenance(workspace, claim.Batch, claim.Term.Id, claim.Term.Provenance, reviewIndex) }).ToList(),
                Extensions = new() { ["sourceRecord"] = ImportIdentity.Json(term.SourceFields), ["alternateExternalIds"] = ImportIdentity.Json(group.SelectMany(x => x.Term.AlternateIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                    ["sourceClaims"] = ImportIdentity.Json(group.Select(x => x.Term with { Provenance = WorkspaceValidation.ReviewedProvenance(workspace, x.Batch, x.Term.Id, x.Term.Provenance, reviewIndex) })) }
            };
            package.Definitions.Add(definition);
            if (!catalogs.TryGetValue(term.Language, out var catalog)) catalogs[term.Language] = catalog = new() { Namespace = ns, Language = term.Language };
            catalog.Entries.Add(definition.NameKey, EscapeTemplate(term.Label));
            if (definition.DescriptionKey is not null) catalog.Entries.Add(definition.DescriptionKey, EscapeTemplate(term.Description!));
            for (var i = 0; i < definition.SynonymKeys.Count; i++) catalog.Entries.Add(definition.SynonymKeys[i], EscapeTemplate(term.Synonyms[i]));
        }
        var index = WorkspaceValidation.TermIndex(workspace);
        var selectedReviewKeys = terms.Select(x => (x.Batch.Id, x.Term.Id)).Concat(assertions.Select(x => (x.Batch.Id, x.Assertion.Id))).ToHashSet();
        var selectedBatchIds = batches.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var selectedIds = terms.Select(x => x.Term.Id).ToHashSet(StringComparer.Ordinal);
        var sidecar = new SourceAssertionDocument();
        foreach (var (assertion, batch) in assertions)
        {
            var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var external in WorkspaceValidation.RequiredReferences(assertion))
            {
                if (!index.TryGetValue(external, out var target) || !selectedIds.Contains(target.Term.Id))
                    throw new InvalidDataException($"Approved assertion {assertion.Id} needs an approved, uniquely resolved endpoint: {external}.");
                resolved[external] = target.Term.Id;
            }
            var provenance = WorkspaceValidation.ReviewedProvenance(workspace, batch, assertion.Id, assertion.Provenance, reviewIndex);
            sidecar.Assertions.Add(new()
            {
                Source = assertion with { Provenance = provenance }, SubjectId = resolved[assertion.SubjectExternalId], ObjectId = resolved[assertion.ObjectExternalId],
                DiseaseId = assertion.DiseaseExternalId is null ? null : resolved[assertion.DiseaseExternalId]
            });
            if (IsPositivePhenotypeAssociation(assertion))
                package.Relations.Add(new()
                {
                    Id = assertion.Id, Version = "1.0.0", Kind = RelationKind.DiseaseFinding,
                    FromId = resolved[assertion.SubjectExternalId], ToId = resolved[assertion.ObjectExternalId], Provenance = provenance,
                    Extensions = new() { ["sourceRecord"] = ImportIdentity.Json(assertion.SourceFields) }
                });
        }
        var validation = ContentValidator.Validate(package);
        if (validation.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, validation.Select(x => x.Code + ": " + x.Message)));
        if (requiredLanguages is { Count: > 0 })
            foreach (var language in requiredLanguages)
            {
                if (!catalogs.TryGetValue(language, out var catalog) || package.Definitions.Any(d => !catalog.Entries.ContainsKey(d.NameKey) ||
                    (d.DescriptionKey is not null && !catalog.Entries.ContainsKey(d.DescriptionKey)) || d.SynonymKeys.Any(k => !catalog.Entries.ContainsKey(k))))
                    throw new InvalidDataException($"Missing requested release translations for {language}; source labels are never copied as invented translations.");
            }
        var destination = Path.GetFullPath(outputPath);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Export destination already exists; choose a new directory to preserve existing releases.");
        var parent = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".iryomera-export-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(temporary);
        try
        {
            await WriteAsync(Path.Combine(temporary, "content.json"), package, ct);
            await WriteAsync(Path.Combine(temporary, "source-assertions.json"), sidecar, ct);
            Directory.CreateDirectory(Path.Combine(temporary, "localization"));
            foreach (var (language, catalog) in catalogs) await WriteAsync(Path.Combine(temporary, "localization", language + ".json"), catalog, ct);
            Directory.CreateDirectory(Path.Combine(temporary, "LICENSES"));
            foreach (var batch in batches)
                await File.WriteAllTextAsync(Path.Combine(temporary, "LICENSES", batch.Id + ".txt"), batch.Options.Attribution + "\n" + batch.Options.SourceUrl + "\nVersion: " + batch.Options.SourceVersion + "\n\n" + batch.Options.LicenseText, ct);
            await WriteAsync(Path.Combine(temporary, "report.json"), new ReleaseReport
            {
                SelectedDefinitionIds = terms.Select(x => x.Term.Id).Distinct(StringComparer.Ordinal).ToList(), SelectedAssertionIds = assertions.Select(x => x.Assertion.Id).ToList(),
                OmittedUnapprovedRecords = workspace.Batches.Sum(x => x.Terms.Count + x.Assertions.Count) - terms.Length - assertions.Length,
                Imports = batches.Select(b => new ReleaseImportSummary
                {
                    BatchId = b.Id, InputSha256 = b.InputSha256, InputBytes = b.InputBytes, AdapterVersion = b.AdapterVersion,
                    Options = b.Options, ObservedSourceVersion = b.ObservedSourceVersion
                }).ToList(),
                Reviews = workspace.Reviews.Where(r => selectedReviewKeys.Contains((r.BatchId, r.RecordId))).ToList(),
                Rights = workspace.Rights.Where(r => selectedBatchIds.Contains(r.BatchId)).ToList(),
                DiagnosticCounts = new(diagnostics.GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal)
            }, ct);
            ct.ThrowIfCancellationRequested();
            Directory.Move(temporary, destination);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }

    private static string EscapeTemplate(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("{", "\\{", StringComparison.Ordinal).Replace("}", "\\}", StringComparison.Ordinal);

    private static bool IsPositivePhenotypeAssociation(SourceAssertion assertion)
    {
        string Text(string name) => assertion.SourceFields.TryGetValue(name, out var field) ? field.GetString() ?? "" : "";
        // Do not interpret frequency ontology terms, negations, onset or other contextual qualifiers.
        // Only an explicitly unqualified source assertion projects to the context-free SDK relation.
        return assertion.Kind == SourceAssertionKind.HpoAssociation && Text("aspect") == "P" &&
            new[] { "qualifier", "frequency", "onset", "sex", "modifier" }.All(field => Text(field).Length == 0);
    }

    private static async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        await ContentJson.WriteAsync(output, value, ct);
    }
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseReport
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public List<string> SelectedDefinitionIds { get; init; } = [];
    public List<string> SelectedAssertionIds { get; init; } = [];
    public int OmittedUnapprovedRecords { get; init; }
    public List<ReleaseImportSummary> Imports { get; init; } = [];
    public List<ReviewDecision> Reviews { get; init; } = [];
    public List<RightsDecision> Rights { get; init; } = [];
    public SortedDictionary<string, int> DiagnosticCounts { get; init; } = new(StringComparer.Ordinal);
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseImportSummary
{
    public required string BatchId { get; init; }
    public required string InputSha256 { get; init; }
    public required long InputBytes { get; init; }
    public required string AdapterVersion { get; init; }
    public required ImportOptions Options { get; init; }
    public string? ObservedSourceVersion { get; init; }
}
