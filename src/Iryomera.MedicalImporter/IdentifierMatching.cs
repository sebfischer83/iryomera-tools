using System.Text.Json.Serialization;

namespace Iryomera.MedicalImporter;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentifierMatchCandidate(string LeftId, string RightId, string System, string Value, string Kind, string Note);

/// <summary>Explicit identifier comparisons only. No aliases, equivalence assertions or automatic merging.</summary>
public static class IdentifierMatching
{
    public static IReadOnlyList<IdentifierMatchCandidate> Candidates(ImportWorkspace workspace, int limit = 1000)
    {
        WorkspaceValidation.EnsureShape(workspace);
        if (limit is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(limit));
        var targets = workspace.Batches.SelectMany(b => b.Terms).Where(t => !t.ExternalId.StartsWith("WIKIDATA:", StringComparison.Ordinal)).DistinctBy(t => t.Id).ToArray();
        var results = new List<IdentifierMatchCandidate>();
        foreach (var term in workspace.Batches.SelectMany(b => b.Terms).Where(t => t.ExternalIdentifiers is not null).OrderBy(t => t.Id, StringComparer.Ordinal))
            foreach (var identifier in term.ExternalIdentifiers!)
            {
                var exact = identifier.Value.StartsWith(identifier.System + ":", StringComparison.Ordinal) ? identifier.Value : identifier.System + ":" + identifier.Value;
                foreach (var target in targets.Where(t => t.ExternalId == exact || t.AlternateIds.Contains(exact, StringComparer.Ordinal) || (identifier.System == "ICD10" && t.ExternalId == "ICD10GM:" + identifier.Value)).OrderBy(t => t.Id, StringComparer.Ordinal))
                {
                    if (target.Id == term.Id) continue;
                    var family = identifier.System == "ICD10" && target.ExternalId.StartsWith("ICD10GM:", StringComparison.Ordinal);
                    results.Add(new(term.Id, target.Id, identifier.System, identifier.Value, family ? "classificationFamily" : "explicitIdentifier",
                        family ? "ICD-10 and ICD-10-GM editions are not equivalent; compare this explicit shared code manually." : "Explicit source identifier matches; clinical equivalence is unreviewed."));
                    if (results.Count == limit) return results;
                }
            }
        return results.Distinct().ToArray();
    }
}
