using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class CatalogAssetTests
{
    private static (ContentPackage Package, FurnitureDefinition Furniture) Example()
    {
        var package = Fixtures.Package(); var old = package.Definitions.OfType<FurnitureDefinition>().First();
        var furniture = old with
        {
            CatalogAssets = [new() { CatalogId = "iryomera.assets:wall.notice_board", Kind = AssetKind.ModelGlb }, new() { CatalogId = "iryomera.assets:wall.notice_board", Kind = AssetKind.Icon }],
            Placement = new() { Footprint = new() { Width = 1, Depth = 1 }, Mount = FurnitureMount.Wall, MountHeightMeters = 1.5m }
        };
        package.Definitions[package.Definitions.IndexOf(old)] = furniture; return (package, furniture);
    }

    [Fact]
    public async Task CatalogReferencesAndMountsRoundTripAndMatchSchemas()
    {
        var (package, furniture) = Example(); Assert.Empty(ContentValidator.Validate(package));
        Assert.True(Fixtures.Evaluate("furnitureDefinition", JsonSerializer.SerializeToNode<ContentDefinition>(furniture, ContentJson.Options)).IsValid);
        Assert.True(Fixtures.Evaluate("catalogAssetReference", JsonSerializer.SerializeToNode(furniture.CatalogAssets![0], ContentJson.Options)).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        var reloaded = result.Value!.Definitions.OfType<FurnitureDefinition>().Single(d => d.Id == furniture.Id);
        Assert.Equal((FurnitureMount.Wall, 1.5m), (reloaded.Placement!.Mount!.Value, reloaded.Placement.MountHeightMeters!.Value));
        Assert.Equal("iryomera.assets:wall.notice_board", reloaded.CatalogAssets![0].CatalogId);
        Assert.Null(reloaded.Assets);
    }

    [Fact]
    public void LegacyPlacementsAndDefinitionsOmitTheNewFields()
    {
        var json = JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options);
        Assert.DoesNotContain("catalogAssets", json); Assert.DoesNotContain("\"mount", json);
        var floor = new FurniturePlacement { Footprint = new() { Width = 2, Depth = 1 } };
        Assert.Equal("{\"footprint\":{\"width\":2,\"depth\":1,\"cellSizeMeters\":1,\"occupiedCells\":[]},\"interactionPoints\":[]}",
            JsonSerializer.Serialize(floor, new JsonSerializerOptions(ContentJson.Options) { WriteIndented = false }));
    }

    [Theory]
    [InlineData("id", "catalog-asset")]
    [InlineData("duplicate", "duplicate-catalog-asset")]
    [InlineData("floor-height", "furniture-mount")]
    [InlineData("ceiling-height", "furniture-mount")]
    [InlineData("negative-height", "furniture-mount")]
    public void InvalidCatalogReferencesAndMountsAreRejected(string change, string code)
    {
        var (package, furniture) = Example(); var placement = furniture.Placement!;
        var changed = change switch
        {
            "id" => furniture with { CatalogAssets = [furniture.CatalogAssets![0] with { CatalogId = "notice_board.glb" }] },
            "duplicate" => furniture with { CatalogAssets = [furniture.CatalogAssets![0], furniture.CatalogAssets[0]] },
            "floor-height" => furniture with { Placement = placement with { Mount = null } },
            "ceiling-height" => furniture with { Placement = placement with { Mount = FurnitureMount.Ceiling } },
            "negative-height" => furniture with { Placement = placement with { MountHeightMeters = -0.1m } },
            _ => furniture
        };
        package.Definitions[package.Definitions.IndexOf(furniture)] = changed;
        Assert.Contains(ContentValidator.Validate(package), i => i.Code == code);
    }

    [Theory]
    [InlineData("height-without-wall")]
    [InlineData("negative-height")]
    [InlineData("unknown-mount")]
    [InlineData("catalog-path")]
    [InlineData("catalog-kind")]
    public void SchemasRejectMalformedMountsAndCatalogReferences(string change)
    {
        var (_, furniture) = Example();
        var node = JsonSerializer.SerializeToNode<ContentDefinition>(furniture, ContentJson.Options)!;
        var placement = node["placement"]!.AsObject(); var catalog = node["catalogAssets"]![0]!.AsObject();
        switch (change)
        {
            case "height-without-wall": placement["mount"] = "ceiling"; break;
            case "negative-height": placement["mountHeightMeters"] = -1; break;
            case "unknown-mount": placement["mount"] = "roof"; break;
            case "catalog-path": catalog["catalogId"] = "assets/model.glb"; break;
            case "catalog-kind": catalog["kind"] = "script"; break;
        }
        Assert.False(Fixtures.Evaluate("furnitureDefinition", node).IsValid);
    }

    [Fact]
    public void CeilingAndFloorMountsWithoutHeightAreValid()
    {
        var (package, furniture) = Example();
        package.Definitions[package.Definitions.IndexOf(furniture)] = furniture with { Placement = furniture.Placement! with { Mount = FurnitureMount.Ceiling, MountHeightMeters = null } };
        Assert.Empty(ContentValidator.Validate(package));
        var node = JsonSerializer.SerializeToNode<ContentDefinition>(package.Definitions.OfType<FurnitureDefinition>().First(), ContentJson.Options)!;
        Assert.True(Fixtures.Evaluate("furnitureDefinition", node).IsValid);
        Assert.Equal("ceiling", node["placement"]!["mount"]!.GetValue<string>());
    }
}
