using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.Localization;

namespace Iryomera.ContentStudio.Services;

public sealed partial class ProjectService
{
    public static DefinitionEdit EditModel(ProjectSnapshot state, ContentDefinition definition) => new()
    {
        Id = definition.Id, Kind = StudioContent.Kind(definition), Version = definition.Version,
        NameEn = StudioContent.Text(state.Catalogs, definition.NameKey, "en"), NameDe = StudioContent.Text(state.Catalogs, definition.NameKey, "de"),
        DescriptionEn = definition.DescriptionKey is null ? "" : StudioContent.Text(state.Catalogs, definition.DescriptionKey, "en"),
        DescriptionDe = definition.DescriptionKey is null ? "" : StudioContent.Text(state.Catalogs, definition.DescriptionKey, "de"),
        SynonymsEn = string.Join('\n', definition.SynonymKeys.Select(k => StudioContent.Text(state.Catalogs, k, "en"))),
        SynonymsDe = string.Join('\n', definition.SynonymKeys.Select(k => StudioContent.Text(state.Catalogs, k, "de"))),
        ClassificationsJson = StudioContent.Json(definition.Classifications), Source = definition.Provenance.Source,
        ExternalId = definition.Provenance.ExternalId, SourceVersion = definition.Provenance.SourceVersion, License = definition.Provenance.License
    };

    public Task SaveDefinitionAsync(Guid project, long revision, DefinitionEdit edit, bool create = false, CancellationToken ct = default) =>
        MutateAsync(project, revision, create ? "definition-create" : "definition-edit", edit.Id, edit.Actor, edit.Note, state =>
        {
            if (!ContentFormat.IsValidId(edit.Id) || string.IsNullOrWhiteSpace(edit.NameEn)) throw new ArgumentException("A valid stable ID and English name are required.");
            var existing = state.Package.Definitions.Find(d => d.Id == edit.Id);
            if (create && StudioContent.Records(state.Package).Any(r => r.Id == edit.Id)) throw new ArgumentException("Duplicate content ID.");
            if (!create && existing is null) throw new ArgumentException("Definition does not exist; IDs cannot be renamed.");
            if (existing is not null && StudioContent.Kind(existing) != edit.Kind) throw new ArgumentException("Definition kind is immutable.");
            var provenance = StudioContent.Draft(existing?.Provenance ?? new Provenance
            { Source = edit.Source, ExternalId = edit.ExternalId, SourceVersion = edit.SourceVersion, License = edit.License, ImportedAt = DateTimeOffset.UtcNow });
            // A sourced record's origin is immutable; authoring history records corrections separately.
            if (existing is not null && (provenance.Source != edit.Source || provenance.ExternalId != edit.ExternalId || provenance.SourceVersion != edit.SourceVersion || provenance.License != edit.License))
                throw new ArgumentException("Preserve original source/license fields; create a new sourced record for a different origin.");
            var definition = existing ?? StudioContent.NewDefinition(edit.Kind, edit.Id, edit.Id.Replace(':', '.') + ".name", edit.Version, provenance);
            var synonymEn = Lines(edit.SynonymsEn); var synonymDe = Lines(edit.SynonymsDe);
            if (synonymDe.Length > 0 && synonymDe.Length != synonymEn.Length) throw new ArgumentException("Synonym translations must have matching line counts.");
            var key = definition.Id.Replace(':', '.');
            definition = definition with
            {
                Version = edit.Version, Provenance = provenance,
                DescriptionKey = string.IsNullOrWhiteSpace(edit.DescriptionEn) && string.IsNullOrWhiteSpace(edit.DescriptionDe) ? null : definition.DescriptionKey ?? key + ".description",
                Classifications = StudioContent.Parse<List<ExternalIdentifier>>(edit.ClassificationsJson),
                SynonymKeys = synonymEn.Select((_, i) => key + ".synonym.s" + i).ToList()
            };
            var obsoleteKeys = existing is null ? [] : existing.SynonymKeys.Except(definition.SynonymKeys).Concat(existing.DescriptionKey is not null && definition.DescriptionKey is null ? [existing.DescriptionKey] : []).ToArray();
            foreach (var catalog in state.Catalogs) foreach (var oldKey in obsoleteKeys) catalog.Entries.Remove(oldKey);
            SetText(state, definition.NameKey, "en", edit.NameEn); SetText(state, definition.NameKey, "de", edit.NameDe);
            if (definition.DescriptionKey is not null) { SetText(state, definition.DescriptionKey, "en", edit.DescriptionEn); SetText(state, definition.DescriptionKey, "de", edit.DescriptionDe); }
            for (var i = 0; i < synonymEn.Length; i++) { SetText(state, definition.SynonymKeys[i], "en", synonymEn[i]); SetText(state, definition.SynonymKeys[i], "de", synonymDe.ElementAtOrDefault(i) ?? ""); }
            if (existing is not null) state.Package.Definitions.Remove(existing);
            state.Package.Definitions.Add(definition); state.Package.Definitions.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            InvalidateDependents(state.Package, definition.Id);
        }, ct: ct);

    /// <summary>
    /// Replaces catalog asset references and, for furniture, the placement. Like other definition edits this resets the
    /// definition's review; relations are unaffected because assets and placement carry no medical meaning.
    /// </summary>
    public Task SaveAssetsAsync(Guid project, long revision, string definitionId, IReadOnlyList<CatalogAssetReference> catalogAssets, FurniturePlacement? placement,
        string actor, string note, CancellationToken ct = default) =>
        MutateAsync(project, revision, "asset-edit", definitionId, actor, note, state =>
        {
            var index = state.Package.Definitions.FindIndex(d => d.Id == definitionId);
            if (index < 0) throw new ArgumentException("Unknown definition.");
            var definition = state.Package.Definitions[index];
            if (placement is not null && definition is not FurnitureDefinition) throw new ArgumentException("Only furniture has a placement.");
            definition = definition with { CatalogAssets = catalogAssets.Count == 0 ? null : catalogAssets.ToList(), Provenance = StudioContent.Draft(definition.Provenance) };
            if (definition is FurnitureDefinition furniture) definition = furniture with { Placement = placement };
            state.Package.Definitions[index] = definition;
        }, ct: ct);

    public Task SaveRelationAsync(Guid project, long revision, ContentRelation relation, string actor, string note, bool create = false, CancellationToken ct = default) =>
        MutateAsync(project, revision, "relation-edit", relation.Id, actor, note, state =>
        {
            var existing = state.Package.Relations.Find(r => r.Id == relation.Id);
            if (create && StudioContent.Records(state.Package).Any(r => r.Id == relation.Id)) throw new ArgumentException("Duplicate content ID.");
            if (!create && existing is null) throw new ArgumentException("Unknown relation.");
            if (existing is not null && StudioContent.Json(existing.Provenance with { ReviewStatus = ReviewStatus.Unreviewed, ReviewedAt = null, ReviewedBy = null }) !=
                StudioContent.Json(relation.Provenance with { ReviewStatus = ReviewStatus.Unreviewed, ReviewedAt = null, ReviewedBy = null })) throw new ArgumentException("Preserve original relation provenance.");
            if (existing is not null) state.Package.Relations.Remove(existing);
            state.Package.Relations.Add(relation with { Provenance = StudioContent.Draft(relation.Provenance), Extensions = existing?.Extensions ?? relation.Extensions });
        }, ct: ct);

    public Task SaveAnnotationAsync(Guid project, long revision, MedicalAnnotation annotation, string actor, string note, CancellationToken ct = default) =>
        MutateAsync(project, revision, "annotation-edit", annotation.Id, actor, note, state =>
        {
            var original = state.Package.MedicalAnnotations.Single(a => a.Id == annotation.Id);
            if (StudioContent.Json(StudioContent.Draft(original.Provenance)) != StudioContent.Json(StudioContent.Draft(annotation.Provenance)) ||
                StudioContent.Json(original.SourceFields) != StudioContent.Json(annotation.SourceFields)) throw new ArgumentException("Preserve original annotation provenance and source fields.");
            state.Package.MedicalAnnotations.Remove(original);
            state.Package.MedicalAnnotations.Add(annotation with { Provenance = StudioContent.Draft(original.Provenance), Extensions = original.Extensions });
        }, ct: ct);

    public Task SaveGameplayAsync(Guid project, long revision, GameplayBalance balance, string actor, string note, CancellationToken ct = default) =>
        MutateAsync(project, revision, "simulation-edit", balance.DefinitionId, actor, note, state =>
        {
            var old = state.Package.Gameplay.Find(b => b.DefinitionId == balance.DefinitionId);
            if (old is not null) state.Package.Gameplay.Remove(old);
            state.Package.Gameplay.Add(balance with { Extensions = old?.Extensions ?? balance.Extensions });
        }, invalidateRights: false, ct: ct);

    public async Task ImportCatalogAsync(Guid project, long revision, Stream input, string actor, string note, CancellationToken ct = default)
    {
        var result = await ContentDocuments.ReadValidatedAsync<LocalizationDefinition>(input, cancellationToken: ct);
        if (!result.IsValid) throw new StudioValidationException(result.Issues);
        await MutateAsync(project, revision, "catalog-import", result.Value!.Language, actor, note, state =>
        {
            if (result.Value.Namespace != state.Package.Manifest.Namespace) throw new ArgumentException("Catalog namespace does not match the project.");
            if (result.Value.Language is not ("en" or "de")) throw new ArgumentException("This editor currently supports en/de catalogs.");
            state.Catalogs.RemoveAll(c => c.Language == result.Value.Language); state.Catalogs.Add(result.Value);
            for (var i = 0; i < state.Package.Definitions.Count; i++)
            {
                var d = state.Package.Definitions[i]; state.Package.Definitions[i] = d with { Provenance = StudioContent.Draft(d.Provenance) }; InvalidateDependents(state.Package, d.Id);
            }
            // Rationale/category wording is clinical content too, including records without a direct definition edit.
            for (var i = 0; i < state.Package.ReferenceRanges.Count; i++)
                state.Package.ReferenceRanges[i] = state.Package.ReferenceRanges[i] with { Provenance = StudioContent.Draft(state.Package.ReferenceRanges[i].Provenance) };
            for (var i = 0; i < state.Package.DiagnosticRecommendations.Count; i++)
                state.Package.DiagnosticRecommendations[i] = state.Package.DiagnosticRecommendations[i] with { Provenance = StudioContent.Draft(state.Package.DiagnosticRecommendations[i].Provenance) };
        }, ct: ct);
    }

    public Task ReviewAsync(Guid project, long revision, string id, ReviewStatus status, string actor, string note, CancellationToken ct = default) =>
        MutateAsync(project, revision, "medical-review", id, actor, note, state =>
        {
            if (!Enum.IsDefined(status)) throw new ArgumentException("Unknown review status.");
            if (status == ReviewStatus.Approved) Check(Validate(state).Where(i => i.Code != "unused-key").ToArray());
            Provenance Change(Provenance p) => p with { ReviewStatus = status, ReviewedBy = status is ReviewStatus.Approved or ReviewStatus.Rejected ? actor : null,
                ReviewedAt = status is ReviewStatus.Approved or ReviewStatus.Rejected ? DateTimeOffset.UtcNow : null };
            var definition = state.Package.Definitions.FindIndex(d => d.Id == id);
            var relation = state.Package.Relations.FindIndex(r => r.Id == id);
            var annotation = state.Package.MedicalAnnotations.FindIndex(a => a.Id == id);
            if (definition >= 0) state.Package.Definitions[definition] = state.Package.Definitions[definition] with { Provenance = Change(state.Package.Definitions[definition].Provenance) };
            else if (relation >= 0) state.Package.Relations[relation] = state.Package.Relations[relation] with { Provenance = Change(state.Package.Relations[relation].Provenance) };
            else if (annotation >= 0) state.Package.MedicalAnnotations[annotation] = state.Package.MedicalAnnotations[annotation] with { Provenance = Change(state.Package.MedicalAnnotations[annotation].Provenance) };
            else
            {
                var range = state.Package.ReferenceRanges.FindIndex(r => r.Id == id); var recommendation = state.Package.DiagnosticRecommendations.FindIndex(r => r.Id == id);
                if (range >= 0) state.Package.ReferenceRanges[range] = state.Package.ReferenceRanges[range] with { Provenance = Change(state.Package.ReferenceRanges[range].Provenance) };
                else if (recommendation >= 0) state.Package.DiagnosticRecommendations[recommendation] = state.Package.DiagnosticRecommendations[recommendation] with { Provenance = Change(state.Package.DiagnosticRecommendations[recommendation].Provenance) };
                else throw new ArgumentException("Unknown review record.");
            }
        }, invalidateRights: false, ct: ct);

    public Task ConfirmRightsAsync(Guid project, long revision, bool permitted, string actor, string note, CancellationToken ct = default) =>
        MutateAsync(project, revision, "project-rights", "project", actor, note, state =>
        { state.Project.RightsConfirmed = permitted; state.Project.RightsReviewer = actor; state.Project.RightsNote = note; }, invalidateRights: false, ct: ct);

    public Task ReviewSelectionAsync(Guid project, long revision, IReadOnlyCollection<string> ids, string actor, string note, CancellationToken ct = default) =>
        MutateAsync(project, revision, "medical-review-selection", string.Join(",", ids), actor, note, state =>
        {
            Check(Validate(state).Where(i => i.Code != "unused-key").ToArray());
            if (ids.Count == 0 || ids.Any(id => !StudioContent.Records(state.Package).Any(r => r.Id == id))) throw new ArgumentException("Select existing records for review.");
            var selected = ids.ToHashSet(StringComparer.Ordinal);
            Provenance Approve(Provenance p) => p with { ReviewStatus = ReviewStatus.Approved, ReviewedBy = actor, ReviewedAt = DateTimeOffset.UtcNow };
            for (var i = 0; i < state.Package.Definitions.Count; i++) if (selected.Contains(state.Package.Definitions[i].Id)) state.Package.Definitions[i] = state.Package.Definitions[i] with { Provenance = Approve(state.Package.Definitions[i].Provenance) };
            for (var i = 0; i < state.Package.Relations.Count; i++) if (selected.Contains(state.Package.Relations[i].Id)) state.Package.Relations[i] = state.Package.Relations[i] with { Provenance = Approve(state.Package.Relations[i].Provenance) };
            for (var i = 0; i < state.Package.MedicalAnnotations.Count; i++) if (selected.Contains(state.Package.MedicalAnnotations[i].Id)) state.Package.MedicalAnnotations[i] = state.Package.MedicalAnnotations[i] with { Provenance = Approve(state.Package.MedicalAnnotations[i].Provenance) };
            for (var i = 0; i < state.Package.ReferenceRanges.Count; i++) if (selected.Contains(state.Package.ReferenceRanges[i].Id)) state.Package.ReferenceRanges[i] = state.Package.ReferenceRanges[i] with { Provenance = Approve(state.Package.ReferenceRanges[i].Provenance) };
            for (var i = 0; i < state.Package.DiagnosticRecommendations.Count; i++) if (selected.Contains(state.Package.DiagnosticRecommendations[i].Id)) state.Package.DiagnosticRecommendations[i] = state.Package.DiagnosticRecommendations[i] with { Provenance = Approve(state.Package.DiagnosticRecommendations[i].Provenance) };
        }, invalidateRights: false, ct: ct);

    public async Task SaveDiagnosticJsonAsync(Guid project, long revision, string kind, string json, string actor, string note, string? observationId = null, CancellationToken ct = default)
    {
        await using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        object value = kind switch
        {
            "result" => Require((await ContentDocuments.ReadValidatedAsync<ObservationResultDefinition>(input, cancellationToken: ct))),
            "range" => Require((await ContentDocuments.ReadValidatedAsync<ObservationReferenceRange>(input, cancellationToken: ct))),
            "recommendation" => Require((await ContentDocuments.ReadValidatedAsync<DiagnosticRecommendation>(input, cancellationToken: ct))),
            "simulation" => Require((await ContentDocuments.ReadValidatedAsync<DiagnosticSimulationSettings>(input, cancellationToken: ct))),
            _ => throw new ArgumentException("Unknown diagnostic contract.")
        };
        await MutateAsync(project, revision, "diagnostic-" + kind, observationId ?? kind, actor, note, state =>
        {
            switch (value)
            {
                case ObservationResultDefinition result:
                    var index = state.Package.Definitions.FindIndex(d => d.Id == observationId && d is ObservationDefinition);
                    if (index < 0) throw new ArgumentException("Select an observation.");
                    var old = (ObservationDefinition)state.Package.Definitions[index]; state.Package.Definitions[index] = old with { Result = result, Provenance = StudioContent.Draft(old.Provenance) }; InvalidateDependents(state.Package, old.Id); break;
                case ObservationReferenceRange range:
                    var originalRange = state.Package.ReferenceRanges.Find(r => r.Id == range.Id);
                    if (originalRange is not null && StudioContent.Json(StudioContent.Draft(originalRange.Provenance)) != StudioContent.Json(StudioContent.Draft(range.Provenance))) throw new ArgumentException("Preserve range provenance.");
                    state.Package.ReferenceRanges.RemoveAll(r => r.Id == range.Id); state.Package.ReferenceRanges.Add(range with { Provenance = StudioContent.Draft(range.Provenance) }); break;
                case DiagnosticRecommendation recommendation:
                    var originalRecommendation = state.Package.DiagnosticRecommendations.Find(r => r.Id == recommendation.Id);
                    if (originalRecommendation is not null && StudioContent.Json(StudioContent.Draft(originalRecommendation.Provenance)) != StudioContent.Json(StudioContent.Draft(recommendation.Provenance))) throw new ArgumentException("Preserve recommendation provenance.");
                    state.Package.DiagnosticRecommendations.RemoveAll(r => r.Id == recommendation.Id); state.Package.DiagnosticRecommendations.Add(recommendation with { Provenance = StudioContent.Draft(recommendation.Provenance) }); break;
                case DiagnosticSimulationSettings simulation: state.Package = state.Package with { DiagnosticSimulation = simulation }; break;
            }
        }, invalidateRights: kind != "simulation", ct: ct);
        static T Require<T>(ContentReadResult<T> result) where T : class => result.IsValid ? result.Value! : throw new StudioValidationException(result.Issues);
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static void SetText(ProjectSnapshot state, string key, string language, string text)
    {
        var catalog = state.Catalogs.Find(c => c.Language == language);
        if (catalog is null) { catalog = new() { Namespace = state.Package.Manifest.Namespace, Language = language }; state.Catalogs.Add(catalog); }
        if (string.IsNullOrWhiteSpace(text)) catalog.Entries.Remove(key); else catalog.Entries[key] = text.Trim();
    }
    private static void InvalidateDependents(ContentPackage package, string id)
    {
        for (var i = 0; i < package.Relations.Count; i++) if (package.Relations[i].FromId == id || package.Relations[i].ToId == id)
            package.Relations[i] = package.Relations[i] with { Provenance = StudioContent.Draft(package.Relations[i].Provenance) };
        for (var i = 0; i < package.ReferenceRanges.Count; i++) if (package.ReferenceRanges[i].ObservationId == id) package.ReferenceRanges[i] = package.ReferenceRanges[i] with { Provenance = StudioContent.Draft(package.ReferenceRanges[i].Provenance) };
        for (var i = 0; i < package.DiagnosticRecommendations.Count; i++) if (package.DiagnosticRecommendations[i].DiseaseId == id || package.DiagnosticRecommendations[i].ExaminationId == id || package.DiagnosticRecommendations[i].ContextDefinitionIds.Contains(id)) package.DiagnosticRecommendations[i] = package.DiagnosticRecommendations[i] with { Provenance = StudioContent.Draft(package.DiagnosticRecommendations[i].Provenance) };
        for (var i = 0; i < package.MedicalAnnotations.Count; i++)
        {
            var a = package.MedicalAnnotations[i];
            if (new[] { a.Subject.ContentId, a.Object.ContentId, a.DiseaseContext?.ContentId, a.ExtensionContext?.ContentId }.Contains(id, StringComparer.Ordinal))
                package.MedicalAnnotations[i] = a with { Provenance = StudioContent.Draft(a.Provenance) };
        }
    }
}
