using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class RoomContentsTests
{
    private static ContentRelation Relation(string id, RelationKind kind, string from, string to, RelationRequirement? requirement = null) => new()
    { Id = id, Version = "1.0.0", Kind = kind, FromId = from, ToId = to, Provenance = Fixtures.Package().Relations[0].Provenance, Requirement = requirement };

    [Fact]
    public void DefaultsKeepTheMeaningOfExistingRoomFurnitureRelations()
    {
        Assert.Equal(RelationRequirement.Required, RoomContents.EffectiveRequirement(Relation("synthetic:a", RelationKind.RoomFurniture, "synthetic:demo_room", "synthetic:demo_desk")));
        Assert.Equal(RelationRequirement.Optional, RoomContents.EffectiveRequirement(Relation("synthetic:b", RelationKind.RoomFurniture, "synthetic:demo_room", "synthetic:demo_desk", RelationRequirement.Optional)));
        Assert.Equal(RelationRequirement.Optional, RoomContents.EffectiveRequirement(Relation("synthetic:c", RelationKind.RoomEquipment, "synthetic:demo_room", "synthetic:demo_scanner")));
        Assert.Throws<ArgumentException>(() => RoomContents.EffectiveRequirement(Fixtures.Package().Relations[0]));
    }

    [Fact]
    public void PlaceableItemsAreAggregatedAcrossPackagesInAStableOrder()
    {
        var package = Fixtures.Package();
        package.Relations.RemoveAll(r => r.Kind is RelationKind.RoomFurniture or RelationKind.RoomEquipment);
        package.Relations.Add(Relation("synthetic:room_desk", RelationKind.RoomFurniture, "synthetic:demo_room", "synthetic:demo_desk", RelationRequirement.Optional));
        package.Relations.Add(Relation("synthetic:room_scanner", RelationKind.RoomEquipment, "synthetic:demo_room", "synthetic:demo_scanner"));
        Assert.Empty(ContentValidator.Validate(package));
        var mod = new ContentPackage
        {
            Manifest = new() { Id = "author.mod:content", Namespace = "author.mod", Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = package.Manifest.Id, VersionRange = "[1.0.0,2.0.0)" }] },
            Relations = [Relation("author.mod:room_scanner_required", RelationKind.RoomEquipment, "synthetic:demo_room", "synthetic:demo_scanner", RelationRequirement.Required)]
        };
        Assert.Empty(ContentValidator.Validate(mod, [package]));
        var items = RoomContents.Placeable([mod, package], "synthetic:demo_room");
        Assert.Equal(["synthetic:demo_desk", "synthetic:demo_scanner"], items.Select(i => i.DefinitionId));
        Assert.Equal(RelationRequirement.Optional, items[0].Requirement);
        Assert.Equal(RelationRequirement.Required, items[1].Requirement);
        Assert.Equal(["author.mod:room_scanner_required", "synthetic:room_scanner"], items[1].RelationIds);
        Assert.Equal(items.Select(i => i.DefinitionId), RoomContents.Placeable([package, mod], "synthetic:demo_room").Select(i => i.DefinitionId));
        Assert.Empty(RoomContents.Placeable([package], "synthetic:amber_session"));
    }

    [Fact]
    public void RequirementOnRoomFurnitureIsValidInSchemaAndValidator()
    {
        var relation = Relation("synthetic:room_desk", RelationKind.RoomFurniture, "synthetic:demo_room", "synthetic:demo_desk", RelationRequirement.Optional);
        Assert.True(Fixtures.Evaluate("contentRelation", JsonSerializer.SerializeToNode(relation, ContentJson.Options)).IsValid);
        var package = Fixtures.Package(); package.Relations.Add(relation);
        Assert.Empty(ContentValidator.Validate(package));
    }

    private static FurnitureSurface Surface(params string[] accepts) => new() { Key = "top", HeightMeters = 1, X = 0, Z = 0, Width = 1, Depth = 1, Accepts = accepts.Length == 0 ? null : [.. accepts] };
    private static FurniturePlacement Item(FurnitureMount? mount, params string[] categories) =>
        new() { Footprint = new() { Width = 1, Depth = 1, CellSizeMeters = 0.25m }, Mount = mount, SurfaceCategories = categories.Length == 0 ? null : [.. categories] };

    [Fact]
    public void SurfaceCategoriesDecideWhatMayStandOnASurface()
    {
        Assert.True(FurnitureSurfaces.Accepts(Surface(), Item(FurnitureMount.Surface)));
        Assert.True(FurnitureSurfaces.Accepts(Surface(), Item(FurnitureMount.Surface, "decoration")));
        Assert.True(FurnitureSurfaces.Accepts(Surface("decoration", "lab_device"), Item(FurnitureMount.Surface, "lab_device")));
        Assert.False(FurnitureSurfaces.Accepts(Surface("lab_device"), Item(FurnitureMount.Surface, "decoration")));
        Assert.False(FurnitureSurfaces.Accepts(Surface("lab_device"), Item(FurnitureMount.Surface)));
        Assert.False(FurnitureSurfaces.Accepts(Surface(), Item(null)));
    }

    [Theory]
    [InlineData("accepts-key")]
    [InlineData("accepts-duplicate")]
    [InlineData("item-key")]
    [InlineData("item-not-surface")]
    public void InvalidSurfaceCategoriesAreRejected(string change)
    {
        var package = Fixtures.Package(); var old = package.Definitions.OfType<FurnitureDefinition>().First();
        var placement = change switch
        {
            "accepts-key" => new FurniturePlacement { Footprint = new() { Width = 1, Depth = 1 }, Surfaces = [Surface("Lab Device")] },
            "accepts-duplicate" => new FurniturePlacement { Footprint = new() { Width = 1, Depth = 1 }, Surfaces = [Surface("decoration", "decoration")] },
            "item-key" => Item(FurnitureMount.Surface, "Deko!"),
            _ => Item(FurnitureMount.Floor, "decoration")
        };
        package.Definitions[package.Definitions.IndexOf(old)] = old with { Placement = placement };
        Assert.Contains(ContentValidator.Validate(package), i => i.Code == "furniture-surface-category");
        if (change == "item-not-surface")
            Assert.False(Fixtures.Evaluate("furniturePlacement", JsonSerializer.SerializeToNode(placement, ContentJson.Options)).IsValid);
    }
}
