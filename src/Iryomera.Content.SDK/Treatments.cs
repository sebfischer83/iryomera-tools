namespace Iryomera.Content.SDK;

/// <summary>A treatment offered for a disease, from a diseaseTreatment relation; priority 1 is the first choice.</summary>
public sealed record TreatmentOption(string TreatmentId, string RelationId, int? Priority);

/// <summary>Where an effective treatment value comes from (ADR 0021): the disease→treatment pair, the treatment or the disease.</summary>
public enum TreatmentValueSource { Pair, Treatment, Disease }

/// <summary>Effective simulation values of one treatment for one disease, with the source of each value that is set.</summary>
public sealed record EffectiveTreatment(
    decimal? TreatmentDurationSeconds, decimal? TreatmentCost, decimal? StayChance, decimal? StayDurationSeconds, decimal? StayDurationMaxSeconds,
    IReadOnlyDictionary<string, TreatmentValueSource> Sources);

/// <summary>
/// Treatments, their rooms, staff and complications (ADR 0021). Call on a validated package set and its resolved gameplay;
/// choosing a treatment, rolling a stay or a complication and applying risk modifiers is game logic.
/// </summary>
public static class Treatments
{
    /// <summary>Treatments linked to the disease, by priority (absent last), then relation ID (ordinal).</summary>
    public static IReadOnlyList<TreatmentOption> Options(IEnumerable<ContentPackage> packages, string diseaseId) =>
        packages.SelectMany(p => p.Relations)
            .Where(r => r.Kind == RelationKind.DiseaseTreatment && r.FromId == diseaseId)
            .Select(r => new TreatmentOption(r.ToId, r.Id, r.Priority))
            .OrderBy(o => o.Priority ?? int.MaxValue).ThenBy(o => o.RelationId, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// True when the treatment has at least one room type and exactly one staff role. Other treatments are valid content but not
    /// playable; the game then keeps using the disease's values and the department's treatment room.
    /// </summary>
    public static bool Playable(IEnumerable<ContentPackage> packages, string treatmentId)
    {
        var relations = packages.SelectMany(p => p.Relations).Where(r => r.FromId == treatmentId).ToArray();
        return relations.Any(r => r.Kind == RelationKind.TreatmentRoom) &&
            relations.Where(r => r.Kind == RelationKind.TreatmentStaffRole).Select(r => r.ToId).Distinct(StringComparer.Ordinal).Count() == 1;
    }

    /// <summary>
    /// Field by field, a value on the diseaseTreatment relation beats one on the treatment, which beats one on the disease.
    /// The relation is the diseaseTreatment record linking both; pass null when there is none.
    /// </summary>
    public static EffectiveTreatment Effective(ResolvedGameplay gameplay, string diseaseId, string treatmentId, string? relationId)
    {
        GameplayBalance? For(string? id) => id is null ? null : gameplay.Balances.FirstOrDefault(b => b.DefinitionId == id);
        var layers = new[] { (For(relationId), TreatmentValueSource.Pair), (For(treatmentId), TreatmentValueSource.Treatment), (For(diseaseId), TreatmentValueSource.Disease) };
        var sources = new SortedDictionary<string, TreatmentValueSource>(StringComparer.Ordinal);
        decimal? Pick(string field, Func<GameplayBalance, decimal?> value)
        {
            foreach (var (balance, source) in layers)
                if (balance is not null && value(balance) is { } found) { sources[field] = source; return found; }
            return null;
        }
        return new(Pick("treatmentDurationSeconds", b => b.TreatmentDurationSeconds), Pick("treatmentCost", b => b.TreatmentCost), Pick("stayChance", b => b.StayChance),
            Pick("stayDurationSeconds", b => b.StayDurationSeconds), Pick("stayDurationMaxSeconds", b => b.StayDurationMaxSeconds), sources);
    }

    /// <summary>Complications a treatment can cause, ordered by complication ID (ordinal), with the linking relation IDs.</summary>
    public static IReadOnlyList<(string ComplicationId, string RelationId)> Complications(IEnumerable<ContentPackage> packages, string treatmentId) =>
        packages.SelectMany(p => p.Relations)
            .Where(r => r.Kind == RelationKind.TreatmentComplication && r.FromId == treatmentId)
            .Select(r => (r.ToId, r.Id)).OrderBy(x => x.ToId, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
}

internal static class TreatmentValidator
{
    internal static IEnumerable<ValidationIssue> Validate(ContentPackage package, IReadOnlyList<ContentPackage> visible)
    {
        var relations = visible.SelectMany(p => p.Relations).ToArray();
        var complications = visible.SelectMany(p => p.Definitions).OfType<ComplicationDefinition>().ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var treatment in package.Definitions.OfType<TreatmentDefinition>())
            if (relations.Where(r => r.Kind == RelationKind.TreatmentStaffRole && r.FromId == treatment.Id).Select(r => r.ToId).Distinct(StringComparer.Ordinal).Count() > 1)
                yield return new("treatment-staff-role", treatment.Id, "A treatment has at most one staff role.");
        foreach (var complication in package.Definitions.OfType<ComplicationDefinition>())
        {
            if (!Enum.IsDefined(complication.Effect) || !Enum.IsDefined(complication.Timing))
                yield return new("complication-effect", complication.Id, "Unknown complication effect or timing.");
            var needed = complication.Effect switch
            {
                ComplicationEffect.RequiredExamination => RelationKind.ComplicationExamination,
                ComplicationEffect.FollowUpTreatment => RelationKind.ComplicationTreatment,
                _ => (RelationKind?)null
            };
            if (needed is { } kind && !relations.Any(r => r.Kind == kind && r.FromId == complication.Id))
                yield return new("complication-effect", complication.Id, $"Effect {complication.Effect} needs at least one {kind} relation.");
        }
        foreach (var relation in package.Relations.Where(r => r.Kind is RelationKind.ComplicationExamination or RelationKind.ComplicationTreatment))
            if (complications.TryGetValue(relation.FromId, out var complication) &&
                complication.Effect != (relation.Kind == RelationKind.ComplicationExamination ? ComplicationEffect.RequiredExamination : ComplicationEffect.FollowUpTreatment))
                yield return new("complication-effect", relation.Id, "The relation does not match the complication's effect.");
        // Follow-up chains treatment -> complication -> follow-up treatment must end.
        var next = relations.Where(r => r.Kind is RelationKind.TreatmentComplication or RelationKind.ComplicationTreatment)
            .GroupBy(r => r.FromId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(r => r.ToId).ToArray(), StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal); var active = new HashSet<string>(StringComparer.Ordinal);
        bool Cycle(string id)
        {
            if (active.Contains(id)) return true;
            if (!visited.Add(id)) return false;
            active.Add(id);
            foreach (var target in next.GetValueOrDefault(id, [])) if (Cycle(target)) return true;
            active.Remove(id); return false;
        }
        foreach (var treatment in package.Definitions.OfType<TreatmentDefinition>().Select(t => t.Id).Order(StringComparer.Ordinal))
            if (Cycle(treatment)) { yield return new("treatment-cycle", treatment, "Follow-up treatments through complications must not lead back in a loop."); break; }
        var stays = package.Gameplay.Select(b => (Path: b.DefinitionId, Min: b.StayDurationSeconds, Max: b.StayDurationMaxSeconds))
            .Concat((package.GameplayOverrides ?? []).Select(c => (Path: c.Id, Min: c.StayDurationSeconds, Max: c.StayDurationMaxSeconds)));
        foreach (var (path, min, max) in stays)
            if (min > max) yield return new("balance-range", path, "The shortest stay must not exceed the longest stay.");
    }
}
