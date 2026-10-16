using System.Text.Json;

namespace Iryomera.MedicalImporter;

/// <summary>Fail-closed additional-rights policy, evaluated from raw claims even for legacy workspaces.</summary>
public static class RecordRights
{
    public static string? AdditionalTerms(ImportBatch batch, string recordId)
    {
        return new Evaluation(batch).AdditionalTerms(recordId);
    }

    internal sealed class Evaluation(ImportBatch batch)
    {
        private readonly Dictionary<string, ImportedTerm> _terms = batch.Terms.DistinctBy(t => t.Id).ToDictionary(t => t.Id, StringComparer.Ordinal);
        private readonly Dictionary<string, SourceAssertion> _assertions = batch.Assertions.DistinctBy(a => a.Id).ToDictionary(a => a.Id, StringComparer.Ordinal);
        private readonly HashSet<string> _ieaDiseases = batch.Assertions.Where(a => a.SourceFields.TryGetValue("evidence", out var e) && e.ValueKind == JsonValueKind.String && e.GetString() == "IEA")
            .Select(a => a.SubjectExternalId).ToHashSet(StringComparer.Ordinal);

        internal string? AdditionalTerms(string recordId)
        {
            var term = _terms.GetValueOrDefault(recordId);
            var assertion = _assertions.GetValueOrDefault(recordId);
            if (term is null && assertion is null) throw new ArgumentException("Unknown record ID.");
            static bool Omim(string? id) => id?.StartsWith("OMIM:", StringComparison.OrdinalIgnoreCase) == true;
            static bool ContainsOmim(JsonElement value) => value.ValueKind switch
            {
                JsonValueKind.String => value.GetString()!.Contains("OMIM:", StringComparison.OrdinalIgnoreCase),
                JsonValueKind.Array => value.EnumerateArray().Any(ContainsOmim),
                JsonValueKind.Object => value.EnumerateObject().Any(p => ContainsOmim(p.Value)),
                _ => false
            };
            var fields = term?.SourceFields ?? assertion!.SourceFields;
            foreach (var key in new[] { "EXTERNAL_COPYRIGHT_NOTICE", "additional_terms", "usage_restrictions", "rights", "license", "license_url" })
                if (fields.TryGetValue(key, out var condition) && condition.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(condition.GetString()))
                    return "record-specific-source-terms: " + key;
            if (Omim(term?.ExternalId) || Omim(assertion?.SubjectExternalId) || Omim(assertion?.ObjectExternalId) ||
                Omim(assertion?.DiseaseExternalId) || Omim(assertion?.ExtensionExternalId) || fields.Values.Any(ContainsOmim))
                return "omim-related: verify additional third-party conditions for this exact record and intended use";
            if (batch.Options.Source == "hpo-annotations")
            {
                // HPOA electronic annotations can carry additional permissions even with non-OMIM disease IDs.
                if (assertion?.SourceFields.TryGetValue("evidence", out var evidence) == true && evidence.GetString() == "IEA")
                    return "hpoa-iea: additional source permissions require explicit record review";
                if (term is not null && _ieaDiseases.Contains(term.ExternalId))
                    return "hpoa-iea-disease: disease claim originates in electronically derived annotations";
            }
            return null;
        }
    }

    internal static Dictionary<string, string> BlockReasons(ImportWorkspace workspace, ImportBatch batch)
    {
        var evaluation = new Evaluation(batch);
        var rights = workspace.Rights.Where(r => r.BatchId == batch.Id && r.RecordId is not null).GroupBy(r => r.RecordId!)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in batch.Terms.Select(t => t.Id).Concat(batch.Assertions.Select(a => a.Id)).Distinct(StringComparer.Ordinal))
        {
            var record = rights.GetValueOrDefault(id);
            if (record is not null && record.Decision != RedistributionDecision.Permitted) reasons[id] = "record-rights-" + record.Decision.ToString().ToLowerInvariant();
            else if (evaluation.AdditionalTerms(id) is not null && (record?.Decision != RedistributionDecision.Permitted || string.IsNullOrWhiteSpace(record.PermissionEvidence)))
                reasons[id] = "additional-rights-unresolved";
        }
        return reasons;
    }

    public static string? BlockReason(ImportWorkspace workspace, ImportBatch batch, string recordId)
    {
        var record = workspace.Rights.LastOrDefault(r => r.BatchId == batch.Id && r.RecordId == recordId);
        if (record is not null && record.Decision != RedistributionDecision.Permitted) return "record-rights-" + record.Decision.ToString().ToLowerInvariant();
        if (AdditionalTerms(batch, recordId) is not null && (record?.Decision != RedistributionDecision.Permitted || string.IsNullOrWhiteSpace(record.PermissionEvidence)))
            return "additional-rights-unresolved";
        return null;
    }

    public static void Decide(ImportWorkspace workspace, RightsDecision decision)
    {
        var batch = workspace.Batches.SingleOrDefault(b => b.Id == decision.BatchId) ?? throw new ArgumentException("Unknown batch ID.");
        if (!Enum.IsDefined(decision.Decision) || string.IsNullOrWhiteSpace(decision.Reviewer) || string.IsNullOrWhiteSpace(decision.Note) ||
            (decision.PermissionEvidence is not null && string.IsNullOrWhiteSpace(decision.PermissionEvidence)) ||
            decision.At < batch.Options.ImportedAt || workspace.Rights.Any(r => r.BatchId == decision.BatchId && r.RecordId == decision.RecordId && r.At > decision.At))
            throw new ArgumentException("Rights decisions need audit metadata and chronological timestamps per scope.");
        if (decision.RecordId is not null && AdditionalTerms(batch, decision.RecordId) is not null &&
            decision.Decision == RedistributionDecision.Permitted && string.IsNullOrWhiteSpace(decision.PermissionEvidence))
            throw new ArgumentException("Additional terms require a record-specific permission evidence reference.");
        workspace.Rights.Add(decision);
    }
}
