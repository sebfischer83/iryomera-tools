namespace Iryomera.Content.SDK;

/// <summary>Contract terms of one insurer from the resolved gameplay, with the defaults for absent values.</summary>
public sealed record InsurerTerms(string InsurerId, decimal MinimumReputation, decimal PriceFactor, decimal PatientShare);

/// <summary>
/// Fictional game insurers (ADR 0022). Call on a validated package set and its resolved gameplay; offering, accepting and
/// cancelling contracts and choosing a patient's insurer are game logic.
/// </summary>
public static class Insurers
{
    /// <summary>Every insurer, ordered by ID (ordinal). Absent values mean reputation 0, factor 1 and share 0.</summary>
    public static IReadOnlyList<InsurerTerms> All(IEnumerable<ContentPackage> packages, ResolvedGameplay gameplay)
    {
        var balances = gameplay.Balances.ToDictionary(b => b.DefinitionId, StringComparer.Ordinal);
        return packages.SelectMany(p => p.Definitions).OfType<InsurerDefinition>().Select(i => i.Id).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(id =>
            {
                var balance = balances.GetValueOrDefault(id);
                return new InsurerTerms(id, balance?.MinimumReputation ?? 0, balance?.PriceFactor ?? 1, balance?.PatientShare ?? 0);
            })
            .ToArray();
    }

    /// <summary>The share of arrivals without an insurer (self-payers, factor 1): what the insurers leave of 1, never below 0.</summary>
    public static decimal SelfPayShare(IEnumerable<InsurerTerms> insurers) => Math.Max(0, 1 - insurers.Sum(i => i.PatientShare));
}

internal static class InsurerValidator
{
    /// <summary>Comfort, reputation and price factor ranges, and the summed shares of the package with its dependencies.</summary>
    internal static IEnumerable<ValidationIssue> Validate(ContentPackage package, IReadOnlyList<ContentPackage> closure)
    {
        var values = package.Gameplay.Select(b => (Path: b.DefinitionId, b.Comfort, b.MinimumReputation, b.PriceFactor))
            .Concat((package.GameplayOverrides ?? []).Select(c => (Path: c.Id, c.Comfort, c.MinimumReputation, c.PriceFactor)));
        foreach (var (path, comfort, reputation, factor) in values)
        {
            if (comfort is { } c && (c < 0 || c > 10 || c != decimal.Truncate(c)))
                yield return new("balance-range", path, "Comfort is a whole number in [0,10].");
            if (reputation is < 0 or > 100) yield return new("balance-range", path, "The minimum reputation is within [0,100].");
            if (factor is <= 0) yield return new("balance-range", path, "The price factor is above 0.");
        }
        // The package loads after its dependencies, so its own records and overrides win (ADR 0015).
        var order = closure.Where(p => p.Manifest.Id != package.Manifest.Id).Append(package).ToArray();
        if (order.Select(p => p.Manifest.Id).Distinct(StringComparer.Ordinal).Count() != order.Length) yield break;
        var total = Insurers.All(order, GameplayResolver.Resolve(order)).Sum(i => i.PatientShare);
        if (total > 1) yield return new("insurer-share", package.Manifest.Id, $"The patient shares of all insurers add up to {total}, more than 1.");
    }
}
