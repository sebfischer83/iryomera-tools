using System.Text.Json.Serialization;

namespace Iryomera.Content.SDK;

/// <summary>
/// Simulation only (ADR 0015): replaces the set balancing fields of a definition in a declared dependency, never medical
/// content. Unset fields keep the earlier value; requiredResources merge per key and cannot remove keys.
/// </summary>
public sealed record GameplayOverride : ExtensibleRecord
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public required string Id { get; init; }
    public required string Version { get; init; }
    [JsonRequired]
    public bool SimulationOnly { get; init; } = true;
    public required string TargetDefinitionId { get; init; }
    public decimal? Rarity { get; init; }
    public decimal? Severity { get; init; }
    public decimal? TreatmentDurationSeconds { get; init; }
    public decimal? ExaminationDurationSeconds { get; init; }
    public decimal? ExaminationCost { get; init; }
    public decimal? TreatmentCost { get; init; }
    public decimal? DiagnosticDifficulty { get; init; }
    public decimal? SimulatedSuccessProbability { get; init; }
    public decimal? Capacity { get; init; }
    public decimal? PurchaseCost { get; init; }
    public decimal? WagePerHour { get; init; }
    public decimal? UnlockCost { get; init; }
    public decimal? UnlockDischargedPatients { get; init; }
    public Dictionary<string, decimal>? RequiredResources { get; init; }

    internal IEnumerable<(string Field, decimal Value)> Values()
    {
        if (Rarity is { } a) yield return ("rarity", a);
        if (Severity is { } b) yield return ("severity", b);
        if (TreatmentDurationSeconds is { } c) yield return ("treatmentDurationSeconds", c);
        if (ExaminationDurationSeconds is { } d) yield return ("examinationDurationSeconds", d);
        if (ExaminationCost is { } e) yield return ("examinationCost", e);
        if (TreatmentCost is { } f) yield return ("treatmentCost", f);
        if (DiagnosticDifficulty is { } g) yield return ("diagnosticDifficulty", g);
        if (SimulatedSuccessProbability is { } h) yield return ("simulatedSuccessProbability", h);
        if (Capacity is { } i) yield return ("capacity", i);
        if (PurchaseCost is { } j) yield return ("purchaseCost", j);
        if (WagePerHour is { } k) yield return ("wagePerHour", k);
        if (UnlockCost is { } l) yield return ("unlockCost", l);
        if (UnlockDischargedPatients is { } m) yield return ("unlockDischargedPatients", m);
        foreach (var (key, value) in RequiredResources ?? []) yield return ("requiredResources." + key, value);
    }
}

/// <summary>Two or more packages set the same field of the same definition; the last package in load order wins. A warning, not an error.</summary>
public sealed record GameplayOverrideConflict(string TargetDefinitionId, string Field, IReadOnlyList<string> PackageIds, string WinnerPackageId)
{
    public const string Code = "gameplay-override-conflict";
}

public sealed record ResolvedGameplay(IReadOnlyList<GameplayBalance> Balances, IReadOnlyList<GameplayOverrideConflict> Conflicts);

/// <summary>
/// Deterministic resolution shared by game and tools. The caller supplies the load order (the game's mod settings); nothing
/// is sorted by file system, time or culture. Each definition starts from the gameplay record in its own package; gameplay
/// records for other packages' definitions and gameplay overrides then apply field by field in load order.
/// </summary>
public static class GameplayResolver
{
    public static ResolvedGameplay Resolve(IReadOnlyList<ContentPackage> loadOrder)
    {
        if (loadOrder.Select(p => p.Manifest.Id).Distinct(StringComparer.Ordinal).Count() != loadOrder.Count)
            throw new ArgumentException("Each package may appear only once in the load order.", nameof(loadOrder));
        static bool Own(string definitionId, ContentPackage package) => definitionId.StartsWith(package.Manifest.Namespace + ":", StringComparison.Ordinal);

        var values = new Dictionary<string, SortedDictionary<string, decimal>>(StringComparer.Ordinal);
        var setters = new Dictionary<(string Target, string Field), List<string>>();
        SortedDictionary<string, decimal> For(string target) => values.TryGetValue(target, out var v) ? v : values[target] = new(StringComparer.Ordinal);
        foreach (var package in loadOrder)
            foreach (var balance in package.Gameplay.Where(b => Own(b.DefinitionId, package)))
                foreach (var (field, value) in AsOverride(balance).Values()) For(balance.DefinitionId)[field] = value;
        foreach (var package in loadOrder)
        {
            var changes = package.Gameplay.Where(b => !Own(b.DefinitionId, package)).Select(AsOverride).Concat(package.GameplayOverrides ?? []);
            foreach (var change in changes)
                foreach (var (field, value) in change.Values())
                {
                    For(change.TargetDefinitionId)[field] = value;
                    var key = (change.TargetDefinitionId, field);
                    if (!setters.TryGetValue(key, out var list)) setters[key] = list = [];
                    if (!list.Contains(package.Manifest.Id, StringComparer.Ordinal)) list.Add(package.Manifest.Id);
                }
        }
        var balances = values.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => ToBalance(v.Key, v.Value)).ToArray();
        var conflicts = setters.Where(s => s.Value.Count > 1).OrderBy(s => s.Key.Target, StringComparer.Ordinal).ThenBy(s => s.Key.Field, StringComparer.Ordinal)
            .Select(s => new GameplayOverrideConflict(s.Key.Target, s.Key.Field, s.Value.ToArray(), s.Value[^1])).ToArray();
        return new(balances, conflicts);
    }

    private static GameplayOverride AsOverride(GameplayBalance b) => new()
    {
        Id = "legacy:" + b.DefinitionId, Version = "1.0.0", TargetDefinitionId = b.DefinitionId, Rarity = b.Rarity, Severity = b.Severity,
        TreatmentDurationSeconds = b.TreatmentDurationSeconds, ExaminationDurationSeconds = b.ExaminationDurationSeconds, ExaminationCost = b.ExaminationCost,
        TreatmentCost = b.TreatmentCost, DiagnosticDifficulty = b.DiagnosticDifficulty, SimulatedSuccessProbability = b.SimulatedSuccessProbability, Capacity = b.Capacity,
        PurchaseCost = b.PurchaseCost, WagePerHour = b.WagePerHour,
        UnlockCost = b.UnlockCost, UnlockDischargedPatients = b.UnlockDischargedPatients, RequiredResources = b.RequiredResources
    };

    private static GameplayBalance ToBalance(string target, SortedDictionary<string, decimal> v)
    {
        decimal? Get(string field) => v.TryGetValue(field, out var value) ? value : null;
        return new()
        {
            DefinitionId = target, Rarity = Get("rarity"), Severity = Get("severity"), TreatmentDurationSeconds = Get("treatmentDurationSeconds"),
            ExaminationDurationSeconds = Get("examinationDurationSeconds"), ExaminationCost = Get("examinationCost"), TreatmentCost = Get("treatmentCost"),
            DiagnosticDifficulty = Get("diagnosticDifficulty"), SimulatedSuccessProbability = Get("simulatedSuccessProbability"), Capacity = Get("capacity"),
            PurchaseCost = Get("purchaseCost"), WagePerHour = Get("wagePerHour"),
            UnlockCost = Get("unlockCost"), UnlockDischargedPatients = Get("unlockDischargedPatients"),
            RequiredResources = v.Where(p => p.Key.StartsWith("requiredResources.", StringComparison.Ordinal))
                .ToDictionary(p => p.Key["requiredResources.".Length..], p => p.Value, StringComparer.Ordinal)
        };
    }
}
