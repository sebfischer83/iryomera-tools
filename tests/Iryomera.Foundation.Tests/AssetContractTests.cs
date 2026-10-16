using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.Localization;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class AssetContractTests
{
    private static (ContentPackage Package, FurnitureDefinition Furniture) Example()
    {
        var package = Fixtures.Package(); var old = package.Definitions.OfType<FurnitureDefinition>().First();
        var furniture = old with
        {
            Assets = [new() { Path = "assets/example.glb", Kind = AssetKind.ModelGlb, License = "MIT" }],
            Placement = new() { Footprint = new() { Width = 2, Depth = 1 }, InteractionPoints = [new() { Key = "use", X = 1, Y = 0, Z = -0.5m, HeadingDegrees = 180 }] }
        };
        package.Definitions[package.Definitions.IndexOf(old)] = furniture; return (package, furniture);
    }
    [Fact]
    public async Task OptionalAssetAndPlacementContractsRoundTripAndMatchSchemas()
    {
        var (package, furniture) = Example(); Assert.Empty(ContentValidator.Validate(package));
        Assert.True(Fixtures.Evaluate("furnitureDefinition", JsonSerializer.SerializeToNode<ContentDefinition>(furniture, ContentJson.Options)).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        Assert.True((await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes)).IsValid);
        foreach (var (name, data) in new (string, object)[] { ("assetReference", furniture.Assets![0]), ("furniturePlacement", furniture.Placement!), ("furnitureFootprint", furniture.Placement!.Footprint), ("interactionPoint", furniture.Placement.InteractionPoints[0]), ("footprintCell", new FootprintCell { X = 0, Z = 0 }) })
            Assert.True(Fixtures.Evaluate(name, JsonSerializer.SerializeToNode(data, data.GetType(), ContentJson.Options)).IsValid);
    }
    [Theory]
    [InlineData("size", "footprint")]
    [InlineData("cell", "footprint-cell")]
    [InlineData("duplicate", "interaction-point")]
    [InlineData("heading", "interaction-point")]
    [InlineData("qualification", "interaction-qualification")]
    [InlineData("missing", "missing-reference")]
    [InlineData("asset", "asset")]
    public void InvalidLayoutsAssetsAndQualificationReferencesAreRejected(string change, string code)
    {
        var (package, furniture) = Example(); var placement = furniture.Placement!; var point = placement.InteractionPoints[0];
        switch (change)
        {
            case "size": placement = placement with { Footprint = placement.Footprint with { Width = 0 } }; break;
            case "cell": placement.Footprint.OccupiedCells.Add(new() { X = 2, Z = 0 }); break;
            case "duplicate": placement.InteractionPoints.Add(point); break;
            case "heading": placement.InteractionPoints[0] = point with { HeadingDegrees = 360 }; break;
            case "qualification": placement.InteractionPoints[0] = point with { RequiredQualificationId = package.Definitions.OfType<DiseaseDefinition>().First().Id }; break;
            case "missing": placement.InteractionPoints[0] = point with { RequiredQualificationId = "synthetic:missing_qualification" }; break;
            case "asset": furniture.Assets![0] = furniture.Assets[0] with { Path = "../outside.glb" }; break;
        }
        package.Definitions[package.Definitions.IndexOf(furniture)] = furniture with { Placement = placement };
        Assert.Contains(ContentValidator.Validate(package), i => i.Code == code);
    }
    [Fact]
    public void LegacyDefinitionsKeepTheirWireShapeWhenOptionalFieldsAreAbsent()
    {
        var package = Fixtures.Package(); Assert.Empty(ContentValidator.Validate(package));
        var json = JsonSerializer.Serialize(package, ContentJson.Options);
        Assert.DoesNotContain("\"assets\"", json); Assert.DoesNotContain("\"placement\"", json);
    }
    [Fact]
    public void LanguageSpecificAliasMetadataCannotBypassMandatoryNameTranslations()
    {
        var package = Fixtures.Package(); var definition = package.Definitions[0]; var en = Fixtures.Catalog("en"); var de = Fixtures.Catalog("de");
        package.Definitions[0] = definition with { Extensions = new() { ["localizedAliasKeys"] = JsonSerializer.SerializeToElement(new { en = new[] { definition.NameKey } }) } };
        de.Entries.Remove(definition.NameKey);
        var issues = ContentLocalizationValidator.Validate(package, [en, de]);
        Assert.Contains(issues, i => i.Code == "missing-translation"); Assert.Contains(issues, i => i.Code == "alias-map");
    }
    [Fact]
    public void LegacySdkResourceVocabulariesStayOpaqueForCompatibility()
    {
        var package = Fixtures.Package(); var balance = package.Gameplay[0]; balance.RequiredResources["synthetic:unknown_resource"] = 1;
        Assert.Empty(ContentValidator.Validate(package));
        balance.RequiredResources.Remove("synthetic:unknown_resource"); balance.RequiredResources["symbolic_supply"] = 1;
        Assert.Empty(ContentValidator.Validate(package));
    }
}
