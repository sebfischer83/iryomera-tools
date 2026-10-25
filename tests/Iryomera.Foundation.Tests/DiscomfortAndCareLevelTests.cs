using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>Discomfort of examinations and treatments and care levels of rooms (ADR 0023); synthetic test values.</summary>
public sealed class DiscomfortAndCareLevelTests
{
    private static ContentPackage Mod(params GameplayOverride[] changes) => new()
    {
        Manifest = new() { Id = "mod:mod", Namespace = "mod", Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = "synthetic:foundation", VersionRange = "[1.0.0,2.0.0)" }] },
        GameplayOverrides = [.. changes]
    };

    private static (ContentPackage Package, string Examination, string Room) WithValues(decimal? discomfort, decimal? careLevel)
    {
        var package = Fixtures.Package();
        var examination = package.Definitions.OfType<ExaminationDefinition>().First().Id;
        var room = package.Definitions.OfType<RoomDefinition>().First().Id;
        package.Gameplay.RemoveAll(b => b.DefinitionId == examination || b.DefinitionId == room);
        package.Gameplay.Add(new() { DefinitionId = examination, Discomfort = discomfort });
        package.Gameplay.Add(new() { DefinitionId = room, CareLevel = careLevel });
        return (package, examination, room);
    }

    [Fact]
    public async Task DiscomfortAndCareLevelRoundTripResolveAndMatchSchemas()
    {
        var (package, examination, room) = WithValues(3, 2);
        Assert.Empty(ContentValidator.Validate(package));
        foreach (var balance in package.Gameplay.Where(b => b.Discomfort is not null || b.CareLevel is not null))
            Assert.True(Fixtures.Evaluate("gameplayBalance", JsonSerializer.SerializeToNode(balance, ContentJson.Options)).IsValid, balance.DefinitionId);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(read.IsValid, string.Join(';', read.Issues));

        var gameplay = GameplayResolver.Resolve([read.Value!]);

        Assert.Equal(3m, gameplay.Balances.Single(b => b.DefinitionId == examination).Discomfort);
        Assert.Equal(2m, gameplay.Balances.Single(b => b.DefinitionId == room).CareLevel);
        var legacy = JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options);
        Assert.DoesNotContain("discomfort", legacy); Assert.DoesNotContain("careLevel", legacy);
    }

    [Fact]
    public void A_mod_overrides_discomfort_and_care_level()
    {
        var (package, examination, room) = WithValues(3, 2);
        ContentPackage[] set = [package, Mod(
            new GameplayOverride { Id = "mod:gentle_exam", Version = "1.0.0", TargetDefinitionId = examination, Discomfort = 6 },
            new GameplayOverride { Id = "mod:intensive_room", Version = "1.0.0", TargetDefinitionId = room, CareLevel = 3 })];

        var gameplay = GameplayResolver.Resolve(set);

        Assert.Equal(6m, gameplay.Balances.Single(b => b.DefinitionId == examination).Discomfort);
        Assert.Equal(3m, gameplay.Balances.Single(b => b.DefinitionId == room).CareLevel);
        Assert.Empty(ContentValidator.Validate(set[1], [package]));
    }

    [Theory]
    [InlineData(-1.0, null)]
    [InlineData(11.0, null)]
    [InlineData(2.5, null)]
    [InlineData(null, 0.0)]
    [InlineData(null, 4.0)]
    [InlineData(null, 1.5)]
    public void Values_outside_their_range_are_balance_range_errors(double? discomfort, double? careLevel)
    {
        var (package, _, _) = WithValues((decimal?)discomfort, (decimal?)careLevel);

        Assert.Contains(ContentValidator.Validate(package), i => i.Code == "balance-range");
        var target = Fixtures.Package().Definitions.OfType<ExaminationDefinition>().First().Id;
        var mod = Mod(new GameplayOverride { Id = "mod:change", Version = "1.0.0", TargetDefinitionId = target, Discomfort = (decimal?)discomfort, CareLevel = (decimal?)careLevel });
        Assert.Contains(ContentValidator.Validate(mod, [Fixtures.Package()]), i => i.Code == "balance-range" && i.Path == "mod:change");
    }
}
