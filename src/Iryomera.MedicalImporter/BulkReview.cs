using Iryomera.Content.SDK;

namespace Iryomera.MedicalImporter;

/// <summary>Explicit, atomic bulk decisions. Preview is read-only; exceptions never silently become approvals.</summary>
public static class BulkReview
{
    public static BulkReviewAudit Preview(ImportWorkspace workspace, string batchId, ReviewStatus status, string reviewer,
        DateTimeOffset at, string note, IReadOnlyDictionary<string, string>? exclusions = null)
    {
        WorkspaceValidation.EnsureShape(workspace);
        var batch = workspace.Batches.SingleOrDefault(b => b.Id == batchId) ?? throw new ArgumentException("Unknown batch ID.");
        if (!Enum.IsDefined(status) || string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(note) || at == default ||
            at < batch.Options.ImportedAt || workspace.Reviews.Any(r => r.BatchId == batchId && r.At > at))
            throw new ArgumentException("Bulk review requires audit metadata and chronological timestamps.");
        var ids = batch.Terms.Select(t => t.Id).Concat(batch.Assertions.Select(a => a.Id)).ToHashSet(StringComparer.Ordinal);
        if (exclusions is not null && exclusions.Any(e => !ids.Contains(e.Key) || string.IsNullOrWhiteSpace(e.Value)))
            throw new ArgumentException("Every explicit exclusion needs an existing record ID and a reason.");
        var diagnostics = WorkspaceValidation.Validate(workspace);
        var globalErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && !ids.Contains(d.RecordId)).ToArray();
        if (globalErrors.Any(d => d.Code != "unresolved-reference")) throw new InvalidDataException("Resolve structural workspace errors before bulk review.");
        var blocked = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).GroupBy(d => d.RecordId)
            .ToDictionary(g => g.Key, g => string.Join(",", g.Select(d => d.Code).Distinct().Order(StringComparer.Ordinal)), StringComparer.Ordinal);
        var rights = workspace.Batches.ToDictionary(b => b.Id, b => RecordRights.BlockReasons(workspace, b), StringComparer.Ordinal);
        var terms = batch.Terms.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var audit = new BulkReviewAudit { BatchId = batchId, Status = status, Reviewer = reviewer, At = at, Note = note };
        foreach (var id in ids.Order(StringComparer.Ordinal))
        {
            var reason = exclusions?.GetValueOrDefault(id);
            if (status == ReviewStatus.Approved)
                reason ??= EligibilityReason(terms.GetValueOrDefault(id), id, blocked, rights[batchId]);
            if (reason is null) audit.SelectedIds.Add(id);
            else audit.Exceptions.Add(new(id, reason));
        }
        if (status == ReviewStatus.Approved)
        {
            var index = WorkspaceValidation.TermIndex(workspace);
            var selected = audit.SelectedIds.ToHashSet(StringComparer.Ordinal);
            var reviewIndex = WorkspaceValidation.ReviewIndex(workspace);
            var available = workspace.Batches.SelectMany(b => b.Terms.Select(t => (Batch: b, Term: t)))
                .Where(x => x.Batch.Id == batchId ? selected.Contains(x.Term.Id) :
                    !x.Term.Obsolete && reviewIndex.GetValueOrDefault((x.Batch.Id, x.Term.Id))?.Status == ReviewStatus.Approved &&
                    !rights[x.Batch.Id].ContainsKey(x.Term.Id))
                .Select(x => x.Term.Id).ToHashSet(StringComparer.Ordinal);
            var selection = audit.SelectedIds.ToHashSet(StringComparer.Ordinal);
            foreach (var assertion in batch.Assertions.Where(a => selection.Contains(a.Id)))
                if (WorkspaceValidation.RequiredReferences(assertion).Any(r => !index.TryGetValue(r, out var target) || !available.Contains(target.Term.Id)))
                {
                    audit.SelectedIds.Remove(assertion.Id);
                    audit.Exceptions.Add(new(assertion.Id, "endpoint-not-selected-or-approved"));
                }
            audit.Exceptions.Sort((a, b) => StringComparer.Ordinal.Compare(a.RecordId, b.RecordId));
        }
        return audit;
    }

    private static string? EligibilityReason(ImportedTerm? term, string id, Dictionary<string, string> errors, Dictionary<string, string> rights)
    {
        if (term?.Obsolete == true) return "obsolete-term";
        if (term?.SourceFields.TryGetValue("STATUS", out var status) == true && status.GetString() != "ACTIVE") return "loinc-not-active";
        if (errors.TryGetValue(id, out var error)) return "validation: " + error;
        return rights.GetValueOrDefault(id);
    }

    public static BulkReviewAudit Apply(ImportWorkspace workspace, string batchId, ReviewStatus status, string reviewer,
        DateTimeOffset at, string note, IReadOnlyDictionary<string, string>? exclusions = null)
    {
        var audit = Preview(workspace, batchId, status, reviewer, at, note, exclusions);
        // Build everything before appending; explicit exclusions also revoke any earlier approval.
        var decisions = audit.SelectedIds.Select(id => new ReviewDecision
        { BatchId = batchId, RecordId = id, Status = status, Reviewer = reviewer, At = at, Note = note }).ToArray();
        var exclusionsDecisions = status == ReviewStatus.Approved ? audit.Exceptions.Select(e => new ReviewDecision
        { BatchId = batchId, RecordId = e.RecordId, Status = ReviewStatus.Rejected, Reviewer = reviewer, At = at, Note = note + ": " + e.Reason }).ToArray() : [];
        workspace.Reviews.AddRange(decisions);
        workspace.Reviews.AddRange(exclusionsDecisions);
        workspace.BulkReviews.Add(audit);
        return audit;
    }
}
