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
    public async Task ItemHeightsRoundTripAndMatchSchemas()
    {
        // Arrange: a tall floor cabinet.
        var (package, furniture) = Example();
        var cabinet = furniture with { Placement = furniture.Placement! with { Mount = null, MountHeightMeters = null, HeightMeters = 1.9m } };
        package.Definitions[package.Definitions.IndexOf(furniture)] = cabinet;

        // Act
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);

        // Assert
        Assert.Empty(ContentValidator.Validate(package));
        Assert.True(Fixtures.Evaluate("furnitureDefinition", JsonSerializer.SerializeToNode<ContentDefinition>(cabinet, ContentJson.Options)).IsValid);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        Assert.Equal(1.9m, result.Value!.Definitions.OfType<FurnitureDefinition>().Single(d => d.Id == cabinet.Id).Placement!.HeightMeters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveItemHeightsAreRejected(double height)
    {
        var (package, furniture) = Example();
        var changed = furniture with { Placement = furniture.Placement! with { HeightMeters = (decimal)height } };
        package.Definitions[package.Definitions.IndexOf(furniture)] = changed;

        Assert.Contains(ContentValidator.Validate(package), i => i.Code == "furniture-height");
        Assert.False(Fixtures.Evaluate("furnitureDefinition", JsonSerializer.SerializeToNode<ContentDefinition>(changed, ContentJson.Options)).IsValid);
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

    private static (ContentPackage Package, FurnitureDefinition Furniture) Table(params FurnitureSurface[] surfaces)
    {
        var package = Fixtures.Package(); var old = package.Definitions.OfType<FurnitureDefinition>().First();
        var table = old with { Placement = new() { Footprint = new() { Width = 2, Depth = 1, CellSizeMeters = 0.5m }, Surfaces = [.. surfaces] } };
        package.Definitions[package.Definitions.IndexOf(old)] = table; return (package, table);
    }

    [Fact]
    public async Task SurfacesAndSurfaceMountsRoundTripAndMatchSchemas()
    {
        var (package, table) = Table(new() { Key = "top", HeightMeters = 0.75m, X = 0, Z = 0, Width = 1, Depth = 0.5m }, new() { Key = "shelf", HeightMeters = 0.3m, X = 0.1m, Z = 0.1m, Width = 0.8m, Depth = 0.3m });
        Assert.Empty(ContentValidator.Validate(package));
        Assert.True(Fixtures.Evaluate("furnitureDefinition", JsonSerializer.SerializeToNode<ContentDefinition>(table, ContentJson.Options)).IsValid);
        Assert.True(Fixtures.Evaluate("furnitureSurface", JsonSerializer.SerializeToNode(table.Placement!.Surfaces![0], ContentJson.Options)).IsValid);
        var small = new FurniturePlacement { Footprint = new() { Width = 1, Depth = 1, CellSizeMeters = 0.25m }, Mount = FurnitureMount.Surface };
        Assert.True(Fixtures.Evaluate("furniturePlacement", JsonSerializer.SerializeToNode(small, ContentJson.Options)).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        Assert.Equal(["top", "shelf"], result.Value!.Definitions.OfType<FurnitureDefinition>().Single(d => d.Id == table.Id).Placement!.Surfaces!.Select(s => s.Key));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("height")]
    [InlineData("width")]
    [InlineData("outside-x")]
    [InlineData("outside-z")]
    [InlineData("negative")]
    [InlineData("key")]
    public void InvalidSurfacesAreRejected(string change)
    {
        var top = new FurnitureSurface { Key = "top", HeightMeters = 0.75m, X = 0, Z = 0, Width = 1, Depth = 0.5m };
        FurnitureSurface[] surfaces = change switch
        {
            "duplicate" => [top, top with { HeightMeters = 0.3m }],
            "height" => [top with { HeightMeters = 0 }],
            "width" => [top with { Width = 0 }],
            "outside-x" => [top with { X = 0.5m }],
            "outside-z" => [top with { Depth = 0.6m }],
            "negative" => [top with { X = -0.1m, Width = 0.5m }],
            _ => [top with { Key = "Top Shelf" }]
        };
        Assert.Contains(ContentValidator.Validate(Table(surfaces).Package), i => i.Code == "furniture-surface");
    }

    [Fact]
    public void SurfaceMountsDoNotTakeAMountHeight()
    {
        var (package, furniture) = Example();
        package.Definitions[package.Definitions.IndexOf(furniture)] = furniture with { Placement = furniture.Placement! with { Mount = FurnitureMount.Surface } };
        Assert.Contains(ContentValidator.Validate(package), i => i.Code == "furniture-mount");
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
