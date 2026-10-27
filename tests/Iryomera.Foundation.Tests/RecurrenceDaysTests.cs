using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>Recurring diseases (ADR 0026); synthetic test values.</summary>
public sealed class RecurrenceDaysTests
{
    private static ContentPackage Mod(params GameplayOverride[] changes) => new()
    {
        Manifest = new() { Id = "mod:mod", Namespace = "mod", Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = "synthetic:foundation", VersionRange = "[1.0.0,2.0.0)" }] },
        GameplayOverrides = [.. changes]
    };

    private static (ContentPackage Package, string Disease) WithRecurrence(decimal? days)
    {
        var package = Fixtures.Package();
        var disease = package.Definitions.OfType<DiseaseDefinition>().First().Id;
        var existing = package.Gameplay.FirstOrDefault(b => b.DefinitionId == disease);
        package.Gameplay.RemoveAll(b => b.DefinitionId == disease);
        package.Gameplay.Add((existing ?? new() { DefinitionId = disease }) with { RecurrenceDays = days });
        return (package, disease);
    }

    [Fact]
    public async Task Recurrence_round_trips_resolves_and_matches_the_schema()
    {
        var (package, disease) = WithRecurrence(7);
        Assert.Empty(ContentValidator.Validate(package));
        var balance = package.Gameplay.Single(b => b.DefinitionId == disease);
        Assert.True(Fixtures.Evaluate("gameplayBalance", JsonSerializer.SerializeToNode(balance, ContentJson.Options)).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(read.IsValid, string.Join(';', read.Issues));

        var gameplay = GameplayResolver.Resolve([read.Value!]);

        Assert.Equal(7m, gameplay.Balances.Single(b => b.DefinitionId == disease).RecurrenceDays);
        Assert.DoesNotContain("recurrenceDays", JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options));
    }

    [Fact]
    public void A_mod_overrides_the_recurrence()
    {
        var (package, disease) = WithRecurrence(7);
        ContentPackage[] set = [package, Mod(new GameplayOverride { Id = "mod:often", Version = "1.0.0", TargetDefinitionId = disease, RecurrenceDays = 3 })];

        var gameplay = GameplayResolver.Resolve(set);

        Assert.Equal(3m, gameplay.Balances.Single(b => b.DefinitionId == disease).RecurrenceDays);
        Assert.Empty(ContentValidator.Validate(set[1], [package]));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(366.0)]
    [InlineData(2.5)]
    public void Values_outside_the_range_are_balance_range_errors(double days)
    {
        var (package, disease) = WithRecurrence((decimal)days);

        Assert.Contains(ContentValidator.Validate(package), i => i.Code == "balance-range");
        var mod = Mod(new GameplayOverride { Id = "mod:change", Version = "1.0.0", TargetDefinitionId = disease, RecurrenceDays = (decimal)days });
        Assert.Contains(ContentValidator.Validate(mod, [Fixtures.Package()]), i => i.Code == "balance-range" && i.Path == "mod:change");
    }
}
