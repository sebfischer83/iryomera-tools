using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>All balancing values here are arbitrary simulation test numbers, not medical data.</summary>
public sealed class GameplayOverrideTests
{
    private const string Target = "synthetic:amber_syndrome";
    private static ContentPackage Mod(string ns, params GameplayOverride[] overrides) => new()
    {
        Manifest = new() { Id = ns + ":mod", Namespace = ns, Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = "synthetic:foundation", VersionRange = "[1.0.0,2.0.0)" }] },
        GameplayOverrides = [.. overrides]
    };
    private static GameplayOverride Override(string ns, decimal? cost = null, decimal? rarity = null, Dictionary<string, decimal>? resources = null, string target = Target) =>
        new() { Id = ns + ":balance_patch", Version = "1.0.0", TargetDefinitionId = target, ExaminationCost = cost, Rarity = rarity, RequiredResources = resources };

    [Fact]
    public async Task OverridesValidateRoundTripAndLegacyPackagesOmitThem()
    {
        var base_ = Fixtures.Package(); var mod = Mod("mod_a", Override("mod_a", cost: 35, resources: new() { ["demo_supply"] = 3 }));
        Assert.Empty(ContentValidator.Validate(mod, [base_]));
        Assert.True(Fixtures.Evaluate("gameplayOverride", JsonSerializer.SerializeToNode(mod.GameplayOverrides![0], ContentJson.Options)).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, mod); bytes.Position = 0;
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes, installedPackages: [base_]);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        Assert.Equal(35m, result.Value!.GameplayOverrides!.Single().ExaminationCost);
        Assert.DoesNotContain("gameplayOverrides", JsonSerializer.Serialize(base_, ContentJson.Options));
    }

    [Theory]
    [InlineData("own", "gameplay-override-own")]
    [InlineData("duplicate", "duplicate-gameplay-override")]
    [InlineData("empty", "gameplay-override-empty")]
    [InlineData("range", "balance-range")]
    [InlineData("negative-resource", "balance-range")]
    [InlineData("missing", "missing-reference")]
    [InlineData("undeclared", "undeclared-dependency")]
    [InlineData("not-simulation", "gameplay-override")]
    public void InvalidOverridesAreRejected(string change, string code)
    {
        var base_ = Fixtures.Package(); var mod = Mod("mod_a", Override("mod_a", cost: 35));
        var first = mod.GameplayOverrides![0];
        mod = change switch
        {
            "own" => mod with { GameplayOverrides = [first with { TargetDefinitionId = "mod_a:own_thing" }] },
            "duplicate" => mod with { GameplayOverrides = [first, first with { Id = "mod_a:second_patch" }] },
            "empty" => mod with { GameplayOverrides = [first with { ExaminationCost = null }] },
            "range" => mod with { GameplayOverrides = [first with { Rarity = 1.5m }] },
            "negative-resource" => mod with { GameplayOverrides = [first with { RequiredResources = new() { ["demo_supply"] = -1 } }] },
            "missing" => mod with { GameplayOverrides = [first with { TargetDefinitionId = "synthetic:missing_thing" }] },
            "undeclared" => mod with { Manifest = mod.Manifest with { Dependencies = [] } },
            _ => mod with { GameplayOverrides = [first with { SimulationOnly = false }] }
        };
        Assert.Contains(ContentValidator.Validate(mod, [base_]), i => i.Code == code);
    }

    [Fact]
    public void ResolutionIsFieldLevelInLoadOrderWithConflictWarnings()
    {
        var base_ = Fixtures.Package();
        var a = Mod("mod_a", Override("mod_a", cost: 35, resources: new() { ["demo_supply"] = 3, ["extra_supply"] = 1 }));
        var b = Mod("mod_b", Override("mod_b", cost: 40, rarity: 0.5m));
        var resolved = GameplayResolver.Resolve([base_, a, b]);
        var amber = resolved.Balances.Single(x => x.DefinitionId == Target);
        Assert.Equal((40m, 0.5m, 0.4m, 50m), (amber.ExaminationCost!.Value, amber.Rarity!.Value, amber.Severity!.Value, amber.TreatmentCost!.Value));
        Assert.Equal(new Dictionary<string, decimal> { ["demo_supply"] = 3, ["extra_supply"] = 1 }, amber.RequiredResources);
        var conflict = Assert.Single(resolved.Conflicts);
        Assert.Equal((Target, "examinationCost", "mod_b:mod"), (conflict.TargetDefinitionId, conflict.Field, conflict.WinnerPackageId));
        Assert.Equal(["mod_a:mod", "mod_b:mod"], conflict.PackageIds);
        Assert.Equal(GameplayOverrideConflict.Code, "gameplay-override-conflict");

        var reversed = GameplayResolver.Resolve([base_, b, a]);
        Assert.Equal(35m, reversed.Balances.Single(x => x.DefinitionId == Target).ExaminationCost);
        Assert.Equal("mod_a:mod", reversed.Conflicts.Single().WinnerPackageId);
        Assert.Empty(GameplayResolver.Resolve([base_, a]).Conflicts);
        Assert.Throws<ArgumentException>(() => GameplayResolver.Resolve([base_, a, a]));
    }

    [Fact]
    public void ForeignGameplayRecordsActLikeOverridesSoExistingModsStayVisible()
    {
        var base_ = Fixtures.Package();
        var legacy = Mod("mod_old") with { GameplayOverrides = null, Gameplay = [new() { DefinitionId = Target, ExaminationCost = 25 }] };
        var a = Mod("mod_a", Override("mod_a", cost: 35));
        Assert.Empty(ContentValidator.Validate(legacy, [base_]));
        var resolved = GameplayResolver.Resolve([base_, legacy, a]);
        Assert.Equal(35m, resolved.Balances.Single(x => x.DefinitionId == Target).ExaminationCost);
        Assert.Equal(["mod_old:mod", "mod_a:mod"], resolved.Conflicts.Single().PackageIds);
        Assert.Equal(20m, GameplayResolver.Resolve([base_]).Balances.Single(x => x.DefinitionId == Target).ExaminationCost);
    }
}
