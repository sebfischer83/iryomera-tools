using System.Text;
using System.Text.Json.Serialization;

namespace Iryomera.MedicalImporter;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MatchCandidate(string LeftId, string RightId, string Language, string NormalizedText);

/// <summary>Text matches are review candidates only; they never create aliases, merges or clinical assertions.</summary>
public static class TermMatching
{
    public static IReadOnlyList<MatchCandidate> Candidates(ImportWorkspace workspace, int limit = 1000)
    {
        WorkspaceValidation.EnsureShape(workspace);
        if (limit is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(limit), "Use a candidate limit between 1 and 100000.");
        var groups = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var term in workspace.Batches.SelectMany(x => x.Terms))
            foreach (var text in new[] { term.Label }.Concat(term.Synonyms))
            {
                var normalized = text.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
                if (normalized.Length == 0) continue;
                var key = term.Language + "\0" + normalized;
                if (!groups.TryGetValue(key, out var ids)) groups[key] = ids = new(StringComparer.Ordinal);
                ids.Add(term.Id);
            }
        var candidates = new List<MatchCandidate>();
        foreach (var (key, ids) in groups)
        {
            var array = ids.ToArray(); var split = key.IndexOf('\0');
            for (var i = 0; i < array.Length; i++)
                for (var j = i + 1; j < array.Length; j++)
                {
                    candidates.Add(new(array[i], array[j], key[..split], key[(split + 1)..]));
                    if (candidates.Count == limit) return candidates;
                }
        }
        return candidates;
    }
}
