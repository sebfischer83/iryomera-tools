using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>Furniture comfort and insurers (ADR 0022); all values are synthetic test numbers, no insurance-law statement.</summary>
public sealed class ComfortAndInsurerTests
{
    private static InsurerDefinition Insurer(ContentPackage package, string id) =>
        new() { Id = id, NameKey = package.Definitions[0].NameKey, Version = "1.0.0", Provenance = package.Definitions[0].Provenance };

    private static ContentPackage WithInsurers(params (string Id, decimal? Reputation, decimal? Factor, decimal? Share)[] insurers)
    {
        var package = Fixtures.Package();
        foreach (var (id, reputation, factor, share) in insurers)
        {
            package.Definitions.Add(Insurer(package, id));
            package.Gameplay.Add(new() { DefinitionId = id, MinimumReputation = reputation, PriceFactor = factor, PatientShare = share });
        }
        return package;
    }

    private static ContentPackage Mod(GameplayOverride change) => new()
    {
        Manifest = new() { Id = "mod:mod", Namespace = "mod", Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = "synthetic:foundation", VersionRange = "[1.0.0,2.0.0)" }] },
        GameplayOverrides = [change]
    };

    [Fact]
    public async Task ComfortAndInsurersRoundTripAndMatchSchemas()
    {
        var package = WithInsurers(("synthetic:basic_fund", 0, 1, 0.6m), ("synthetic:private_cover", 70, 1.4m, 0.1m));
        var furniture = package.Definitions.OfType<FurnitureDefinition>().First().Id;
        package.Gameplay.RemoveAll(b => b.DefinitionId == furniture);
        package.Gameplay.Add(new() { DefinitionId = furniture, Comfort = 4 });
        Assert.Empty(ContentValidator.Validate(package));
        Assert.True(Fixtures.Evaluate("insurerDefinition", JsonSerializer.SerializeToNode<ContentDefinition>(package.Definitions.OfType<InsurerDefinition>().First(), ContentJson.Options)).IsValid);
        foreach (var balance in package.Gameplay.Where(b => b.Comfort is not null || b.PatientShare is not null))
            Assert.True(Fixtures.Evaluate("gameplayBalance", JsonSerializer.SerializeToNode(balance, ContentJson.Options)).IsValid, balance.DefinitionId);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(read.IsValid, string.Join(';', read.Issues));
        ContentPackage[] set = [read.Value!];
        var gameplay = GameplayResolver.Resolve(set);
        Assert.Equal(4m, gameplay.Balances.Single(b => b.DefinitionId == furniture).Comfort);
        Assert.Equal([("synthetic:basic_fund", 0m, 1m, 0.6m), ("synthetic:private_cover", 70m, 1.4m, 0.1m)],
            Insurers.All(set, gameplay).Select(i => (i.InsurerId, i.MinimumReputation, i.PriceFactor, i.PatientShare)));
        Assert.Equal(0.3m, Insurers.SelfPayShare(Insurers.All(set, gameplay)));
        var legacy = JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options);
        Assert.DoesNotContain("comfort", legacy); Assert.DoesNotContain("insurer", legacy); Assert.DoesNotContain("patientShare", legacy);
    }

    [Fact]
    public void AnInsurerWithoutBalancingHasNeutralTerms()
    {
        var package = Fixtures.Package();
        package.Definitions.Add(Insurer(package, "synthetic:quiet_fund"));
        ContentPackage[] set = [package];

        var terms = Insurers.All(set, GameplayResolver.Resolve(set)).Single();

        Assert.Equal((0m, 1m, 0m), (terms.MinimumReputation, terms.PriceFactor, terms.PatientShare));
        Assert.Equal(1m, Insurers.SelfPayShare([terms]));
    }

    [Theory]
    [InlineData(-1.0, null, null, null)]
    [InlineData(11.0, null, null, null)]
    [InlineData(2.5, null, null, null)]
    [InlineData(null, -1.0, null, null)]
    [InlineData(null, 101.0, null, null)]
    [InlineData(null, null, 0.0, null)]
    [InlineData(null, null, null, 1.5)]
    public void ValuesOutsideTheirRangeAreRejected(double? comfort, double? reputation, double? factor, double? share)
    {
        static decimal? D(double? value) => value is { } v ? (decimal)v : null;
        var package = Fixtures.Package();
        package.Definitions.Add(Insurer(package, "synthetic:fund"));
        package.Gameplay.Add(new() { DefinitionId = "synthetic:fund", Comfort = D(comfort), MinimumReputation = D(reputation), PriceFactor = D(factor), PatientShare = D(share) });

        Assert.Contains(ContentValidator.Validate(package), i => i.Code == "balance-range" && i.Path == "synthetic:fund");
        Assert.False(Fixtures.Evaluate("gameplayBalance", JsonSerializer.SerializeToNode(package.Gameplay[^1], ContentJson.Options)).IsValid);
    }

    [Fact]
    public void OverridesAreRangeCheckedLikeBalancing()
    {
        var dependency = Fixtures.Package();
        var mod = Mod(new() { Id = "mod:comfort", Version = "1.0.0", TargetDefinitionId = "synthetic:demo_desk", Comfort = 12 });

        Assert.Contains(ContentValidator.Validate(mod, [dependency]), i => i.Code == "balance-range" && i.Path == "mod:comfort");
    }

    [Fact]
    public void SharesAboveOneInAPackageAreRejected()
    {
        var package = WithInsurers(("synthetic:first", 0, 1, 0.7m), ("synthetic:second", 0, 1, 0.4m));

        Assert.Contains(ContentValidator.Validate(package), i => i.Code == "insurer-share");
    }

    [Fact]
    public void AModThatRaisesTheSharesAboveOneIsRejected()
    {
        var dependency = Fixtures.Package();
        var mod = Mod(new() { Id = "mod:share", Version = "1.0.0", TargetDefinitionId = "synthetic:fund", PatientShare = 0.9m });
        dependency.Definitions.Add(Insurer(dependency, "synthetic:fund"));
        dependency.Definitions.Add(Insurer(dependency, "synthetic:other"));
        dependency.Gameplay.Add(new() { DefinitionId = "synthetic:fund", PatientShare = 0.5m });
        dependency.Gameplay.Add(new() { DefinitionId = "synthetic:other", PatientShare = 0.4m });
        Assert.DoesNotContain(ContentValidator.Validate(dependency), i => i.Code == "insurer-share");

        Assert.Contains(ContentValidator.Validate(mod, [dependency]), i => i.Code == "insurer-share" && i.Path == mod.Manifest.Id);
    }
}
