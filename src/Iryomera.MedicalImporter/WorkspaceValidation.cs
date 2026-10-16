using System.Globalization;
using System.Text.Json;
using Iryomera.Content.SDK;

namespace Iryomera.MedicalImporter;

public static class WorkspaceValidation
{
    public static void EnsureShape(ImportWorkspace workspace)
    {
        var issues = ContractShape.Validate(workspace);
        if (issues.Count > 0 || workspace.SchemaVersion != "1.0.0") throw new InvalidDataException("Malformed workspace or unsupported workspace schema version.");
    }

    public static IReadOnlyList<ImportDiagnostic> Validate(ImportWorkspace workspace)
    {
        EnsureShape(workspace);
        var diagnostics = workspace.Batches.SelectMany(x => x.Diagnostics).ToList();
        void Error(string code, string id, string message) => diagnostics.Add(new(DiagnosticSeverity.Error, code, id, message));
        var batchIds = new HashSet<string>(StringComparer.Ordinal);
        var globalIds = new HashSet<string>(StringComparer.Ordinal);
        var claims = new Dictionary<string, ImportedTerm>(StringComparer.Ordinal);
        var assertionClaims = new Dictionary<string, SourceAssertion>(StringComparer.Ordinal);
        foreach (var batch in workspace.Batches)
        {
            if (!batchIds.Add(batch.Id)) Error("duplicate-batch", batch.Id, "Duplicate import batch.");
            try { ImportEngine.ValidateOptions(batch.Options); }
            catch (ArgumentException e) { Error("options", batch.Id, e.Message); }
            if (batch.Id != ImportEngine.BatchId(batch.InputSha256, batch.Options, batch.AdapterVersion) || batch.AdapterVersion is not ("1.0.0" or ImportEngine.AdapterVersion) || batch.InputBytes <= 0 ||
                batch.InputSha256.Length != 64 || batch.InputSha256.Any(c => !char.IsAsciiHexDigitLower(c)))
                Error("batch-integrity", batch.Id, "Invalid batch metadata, digest or adapter version.");
            var localIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var term in batch.Terms)
            {
                if (!localIds.Add(term.Id)) Error("duplicate-id", term.Id, "Duplicate normalized record inside one batch.");
                if (!globalIds.Add(term.Id))
                {
                    if (claims.TryGetValue(term.Id, out var previous) && Equivalent(previous, term))
                        diagnostics.Add(new(DiagnosticSeverity.Warning, "duplicate-source-claim", term.Id, "Identical term claims share an explicit external ID; each provenance and review is retained."));
                    else Error("duplicate-id", term.Id, "Multiple differing imports claim one ID; use a separate workspace per release or resolve the conflict explicitly.");
                }
                claims.TryAdd(term.Id, term);
                if (!Enum.IsDefined(term.Kind) || string.IsNullOrWhiteSpace(term.Label) || term.Id != ImportIdentity.ContentId(batch.Options.Namespace, term.ExternalId))
                    Error("term", term.Id, "Invalid normalized term kind, label or ID.");
                try { if (CultureInfo.GetCultureInfo(term.Language).Name != term.Language || term.Language.Length == 0) Error("language", term.Id, "Invalid source language."); }
                catch (CultureNotFoundException) { Error("language", term.Id, "Invalid source language."); }
                CheckRawProvenance(term.Provenance, batch, term.Id);
            }
            foreach (var assertion in batch.Assertions)
            {
                if (!localIds.Add(assertion.Id)) Error("duplicate-id", assertion.Id, "Duplicate normalized record inside one batch.");
                if (!globalIds.Add(assertion.Id))
                {
                    if (assertionClaims.TryGetValue(assertion.Id, out var previous) && Equivalent(previous, assertion))
                        diagnostics.Add(new(DiagnosticSeverity.Warning, "duplicate-assertion-claim", assertion.Id, "Identical assertion in multiple batches; independent provenance, reviews and rights retained."));
                    else Error("duplicate-id", assertion.Id, "Conflicting claims for one assertion ID.");
                }
                assertionClaims.TryAdd(assertion.Id, assertion);
                if (!Enum.IsDefined(assertion.Kind) || assertion.Id != ImportIdentity.AssertionId(batch.Options.Namespace, batch.Options.Source, assertion.Kind,
                    assertion.SubjectExternalId, assertion.Predicate, assertion.ObjectExternalId, assertion.SourceFields)) Error("assertion", assertion.Id, "Invalid assertion kind or stable ID.");
                CheckRawProvenance(assertion.Provenance, batch, assertion.Id);
            }
        }
        var index = TermIndex(workspace, diagnostics);
        foreach (var assertion in workspace.Batches.SelectMany(x => x.Assertions))
        {
            foreach (var external in RequiredReferences(assertion))
                if (!index.ContainsKey(external)) Error("unresolved-reference", assertion.Id, $"No unique staged definition for {external}.");
            if (assertion.Kind == SourceAssertionKind.MaxoAnnotation)
            {
                CheckType(assertion.SubjectExternalId, ImportedTermKind.MedicalProcedure, assertion.Id);
                CheckType(assertion.ObjectExternalId, ImportedTermKind.Finding, assertion.Id);
                if (assertion.DiseaseExternalId is not null) CheckType(assertion.DiseaseExternalId, ImportedTermKind.Disease, assertion.Id);
            }
            if (assertion.Kind == SourceAssertionKind.HpoAssociation)
            {
                CheckType(assertion.SubjectExternalId, ImportedTermKind.Disease, assertion.Id);
                CheckType(assertion.ObjectExternalId, ImportedTermKind.Finding, assertion.Id);
            }
        }
        var batchLookup = workspace.Batches.DistinctBy(b => b.Id).ToDictionary(b => b.Id, StringComparer.Ordinal);
        var records = workspace.Batches.SelectMany(b => b.Terms.Select(t => (b.Id, t.Id)).Concat(b.Assertions.Select(a => (b.Id, a.Id)))).ToHashSet();
        var previousReviews = new Dictionary<(string, string), DateTimeOffset>();
        foreach (var review in workspace.Reviews)
        {
            if (!records.Contains((review.BatchId, review.RecordId)))
                Error("review-reference", review.RecordId, "Review refers to a missing record.");
            if (!Enum.IsDefined(review.Status) || string.IsNullOrWhiteSpace(review.Reviewer) || review.At == default || string.IsNullOrWhiteSpace(review.Note))
                Error("review-audit", review.RecordId, "Review status, reviewer, timestamp and note are required.");
            if ((batchLookup.TryGetValue(review.BatchId, out var owner) && review.At < owner.Options.ImportedAt) ||
                (previousReviews.TryGetValue((review.BatchId, review.RecordId), out var previousAt) && review.At < previousAt))
                Error("review-time", review.RecordId, "Review history timestamps are not chronological.");
            previousReviews[(review.BatchId, review.RecordId)] = review.At;
        }
        var rightsHistory = new ImportWorkspace { Batches = workspace.Batches };
        foreach (var rights in workspace.Rights)
        {
            try { RecordRights.Decide(rightsHistory, rights); }
            catch (ArgumentException e) { Error("rights-audit", rights.RecordId ?? rights.BatchId, e.Message); }
        }
        foreach (var audit in workspace.BulkReviews)
        {
            if (!batchIds.Contains(audit.BatchId) || !Enum.IsDefined(audit.Status) || string.IsNullOrWhiteSpace(audit.Reviewer) ||
                audit.At == default || string.IsNullOrWhiteSpace(audit.Note) ||
                audit.SelectedIds.Concat(audit.Exceptions.Select(e => e.RecordId)).Any(id => !records.Contains((audit.BatchId, id))) ||
                audit.SelectedIds.Concat(audit.Exceptions.Select(e => e.RecordId)).Distinct(StringComparer.Ordinal).Count() != audit.SelectedIds.Count + audit.Exceptions.Count ||
                audit.Exceptions.Any(e => string.IsNullOrWhiteSpace(e.Reason)))
                Error("bulk-review-audit", audit.BatchId, "Malformed bulk review audit or missing record references.");
        }
        return diagnostics.OrderBy(x => x.RecordId, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal).ToArray();

        void CheckType(string external, ImportedTermKind expected, string id)
        {
            if (index.TryGetValue(external, out var term) && term.Term.Kind != expected) Error("assertion-type", id, $"{external} must be {expected}.");
        }
        void CheckRawProvenance(Provenance p, ImportBatch batch, string id)
        {
            if (p.ReviewStatus != ReviewStatus.Unreviewed || p.Source != batch.Options.SourceUrl || p.SourceVersion != batch.Options.SourceVersion ||
                p.License != batch.Options.License || p.ImportedAt != batch.Options.ImportedAt || string.IsNullOrWhiteSpace(p.ExternalId))
                Error("provenance", id, "Raw imports must preserve source metadata and remain unreviewed; use review history for decisions.");
        }
    }

    internal static Dictionary<string, (ImportedTerm Term, ImportBatch Batch)> TermIndex(ImportWorkspace workspace, List<ImportDiagnostic>? diagnostics = null)
    {
        var index = new Dictionary<string, (ImportedTerm, ImportBatch)>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        // Primary IDs retain their identity. An active term's alias must not silently replace an obsolete record.
        foreach (var batch in workspace.Batches)
            foreach (var term in batch.Terms)
                if (!index.TryAdd(term.ExternalId, (term, batch)) && !(index[term.ExternalId].Item1.Id == term.Id && Equivalent(index[term.ExternalId].Item1, term)))
                {
                    ambiguous.Add(term.ExternalId);
                    diagnostics?.Add(new(DiagnosticSeverity.Error, "ambiguous-external-id", term.Id, $"Primary identifier {term.ExternalId} has conflicting owners."));
                }
        var primaryIds = index.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var batch in workspace.Batches)
            foreach (var term in batch.Terms)
                foreach (var external in term.AlternateIds)
                    if (!index.TryAdd(external, (term, batch)) && !(index[external].Item1.Id == term.Id && Equivalent(index[external].Item1, term)))
                    {
                        if (primaryIds.Contains(external) && index[external].Item1.Obsolete)
                        {
                            diagnostics?.Add(new(DiagnosticSeverity.Warning, "shadowed-alternate-id", term.Id, $"Alias {external} is also an obsolete primary ID; primary identity is retained without automatic replacement."));
                            continue;
                        }
                        ambiguous.Add(external);
                        diagnostics?.Add(new(DiagnosticSeverity.Error, "ambiguous-external-id", term.Id, $"External identifier {external} has multiple owners."));
                    }
        foreach (var external in ambiguous) index.Remove(external);
        return index;
    }

    private static bool Equivalent(ImportedTerm left, ImportedTerm right) => left.ExternalId == right.ExternalId && left.Kind == right.Kind &&
        left.Label == right.Label && left.Language == right.Language && left.Description == right.Description && left.Obsolete == right.Obsolete &&
        left.Synonyms.SequenceEqual(right.Synonyms, StringComparer.Ordinal);

    private static bool Equivalent(SourceAssertion left, SourceAssertion right) =>
        left.Kind == right.Kind && left.Predicate == right.Predicate && left.SubjectExternalId == right.SubjectExternalId &&
        left.ObjectExternalId == right.ObjectExternalId && left.DiseaseExternalId == right.DiseaseExternalId &&
        left.ExtensionExternalId == right.ExtensionExternalId &&
        JsonSerializer.Serialize(left.SourceFields, ContentJson.Options) == JsonSerializer.Serialize(right.SourceFields, ContentJson.Options);

    internal static IEnumerable<string> RequiredReferences(SourceAssertion assertion)
    {
        yield return assertion.SubjectExternalId;
        yield return assertion.ObjectExternalId;
        if (assertion.DiseaseExternalId is not null) yield return assertion.DiseaseExternalId;
    }

    public static ReviewStatus Status(ImportWorkspace workspace, string batchId, string recordId) =>
        workspace.Reviews.LastOrDefault(x => x.BatchId == batchId && x.RecordId == recordId)?.Status ?? ReviewStatus.Unreviewed;

    public static void Review(ImportWorkspace workspace, string batchId, string recordId, ReviewStatus status, string reviewer, DateTimeOffset at, string note)
    {
        EnsureShape(workspace);
        var batch = workspace.Batches.SingleOrDefault(x => x.Id == batchId) ?? throw new ArgumentException("Unknown batch ID.");
        var term = batch.Terms.Find(x => x.Id == recordId);
        if (term is null && !batch.Assertions.Any(x => x.Id == recordId)) throw new ArgumentException("Unknown record ID.");
        if (!Enum.IsDefined(status) || string.IsNullOrWhiteSpace(reviewer) || at == default || string.IsNullOrWhiteSpace(note)) throw new ArgumentException("Status, reviewer, timestamp and note are required.");
        if (at < batch.Options.ImportedAt || workspace.Reviews.Any(x => x.BatchId == batchId && x.RecordId == recordId && x.At > at))
            throw new ArgumentException("Review timestamps must not precede import or a previous decision.");
        if (status == ReviewStatus.Approved && term?.Obsolete == true) throw new InvalidOperationException("Obsolete terms cannot be approved for release.");
        workspace.Reviews.Add(new() { BatchId = batchId, RecordId = recordId, Status = status, Reviewer = reviewer, At = at, Note = note });
    }

    public static void ReviewBatch(ImportWorkspace workspace, string batchId, ReviewStatus status, string reviewer, DateTimeOffset at, string note)
    {
        EnsureShape(workspace);
        var batch = workspace.Batches.SingleOrDefault(x => x.Id == batchId) ?? throw new ArgumentException("Unknown batch ID.");
        if (!Enum.IsDefined(status) || string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(note) || at == default ||
            at < batch.Options.ImportedAt || workspace.Reviews.Any(x => x.BatchId == batchId && x.At > at))
            throw new ArgumentException("Review requires status, identity, note and a chronological timestamp.");
        if (status == ReviewStatus.Approved && batch.Terms.Any(x => x.Obsolete)) throw new InvalidOperationException("Bulk approval includes obsolete terms; review eligible records individually.");
        var decisions = batch.Terms.Select(x => x.Id).Concat(batch.Assertions.Select(x => x.Id)).Select(id => new ReviewDecision
        { BatchId = batch.Id, RecordId = id, Status = status, Reviewer = reviewer, At = at, Note = note }).ToArray();
        workspace.Reviews.AddRange(decisions);
    }

    internal static Dictionary<(string BatchId, string RecordId), ReviewDecision> ReviewIndex(ImportWorkspace workspace) =>
        workspace.Reviews.GroupBy(x => (x.BatchId, x.RecordId)).ToDictionary(g => g.Key, g => g.Last());

    internal static Provenance ReviewedProvenance(ImportWorkspace workspace, ImportBatch batch, string id, Provenance original,
        IReadOnlyDictionary<(string BatchId, string RecordId), ReviewDecision>? index = null)
    {
        var decision = index is null ? workspace.Reviews.LastOrDefault(x => x.BatchId == batch.Id && x.RecordId == id) : index.GetValueOrDefault((batch.Id, id));
        return decision is null ? original : original with { ReviewStatus = decision.Status, ReviewedBy = decision.Reviewer, ReviewedAt = decision.At };
    }
}
