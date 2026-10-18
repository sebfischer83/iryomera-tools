using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>Department rooms, opening order and unlock balancing (ADR 0020); all values are synthetic test numbers.</summary>
public sealed class DepartmentTests
{
    private static ContentRelation Relation(string id, RelationKind kind, string from, string to, RelationRequirement? requirement = null, bool? treatment = null) => new()
    { Id = id, Version = "1.0.0", Kind = kind, FromId = from, ToId = to, Provenance = Fixtures.Package().Relations[0].Provenance, Requirement = requirement, TreatmentRoom = treatment };

    private static (ContentPackage Package, string Department, string Second, string Room) Setup()
    {
        var package = Fixtures.Package();
        var department = package.Definitions.OfType<MedicalDepartmentDefinition>().First();
        var second = department with { Id = "synthetic:second_department", NameKey = department.NameKey };
        package.Definitions.Add(second);
        return (package, department.Id, second.Id, package.Definitions.OfType<RoomDefinition>().First().Id);
    }

    [Fact]
    public async Task DepartmentRoomsPrerequisitesAndUnlockValuesRoundTripAndMatchSchemas()
    {
        var (package, department, second, room) = Setup();
        package.Relations.Add(Relation("synthetic:dept_room", RelationKind.DepartmentRoom, department, room, treatment: true));
        package.Relations.Add(Relation("synthetic:dept_order", RelationKind.DepartmentPrerequisite, second, department));
        package.Gameplay.Add(new() { DefinitionId = second, UnlockCost = 30000, UnlockDischargedPatients = 25 });
        Assert.Empty(ContentValidator.Validate(package));
        foreach (var relation in package.Relations.TakeLast(2))
            Assert.True(Fixtures.Evaluate("contentRelation", JsonSerializer.SerializeToNode(relation, ContentJson.Options)).IsValid, relation.Id);
        Assert.True(Fixtures.Evaluate("gameplayBalance", JsonSerializer.SerializeToNode(package.Gameplay[^1], ContentJson.Options)).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(read.IsValid, string.Join(';', read.Issues));
        var rooms = Departments.Rooms([read.Value!], department);
        Assert.Equal((room, RelationRequirement.Required, true), (rooms.Single().RoomId, rooms.Single().Requirement, rooms.Single().TreatmentRoom));
        Assert.Equal([department], Departments.Prerequisites([read.Value!], second));
        Assert.Empty(Departments.Prerequisites([read.Value!], department));
        var balance = GameplayResolver.Resolve([read.Value!]).Balances.Single(b => b.DefinitionId == second);
        Assert.Equal((30000m, 25m), (balance.UnlockCost!.Value, balance.UnlockDischargedPatients!.Value));
        var legacy = JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options);
        Assert.DoesNotContain("treatmentRoom", legacy); Assert.DoesNotContain("unlockCost", legacy); Assert.DoesNotContain("departmentRoom", legacy);
    }

    [Fact]
    public void RoomsAggregateAcrossPackagesAndModsOverrideUnlockValues()
    {
        var (base_, department, _, room) = Setup();
        base_.Relations.Add(Relation("synthetic:dept_room", RelationKind.DepartmentRoom, department, room, RelationRequirement.Optional));
        base_.Gameplay.Add(new() { DefinitionId = department, UnlockCost = 1000 });
        var mod = new ContentPackage
        {
            Manifest = new() { Id = "mod_a:mod", Namespace = "mod_a", Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = base_.Manifest.Id, VersionRange = "[1.0.0,2.0.0)" }] },
            Relations = [Relation("mod_a:dept_room", RelationKind.DepartmentRoom, department, room, treatment: true)],
            GameplayOverrides = [new() { Id = "mod_a:cheaper", Version = "1.0.0", TargetDefinitionId = department, UnlockCost = 500, UnlockDischargedPatients = 3 }]
        };
        Assert.Empty(ContentValidator.Validate(mod, [base_]));
        var aggregated = Departments.Rooms([base_, mod], department).Single();
        Assert.Equal((RelationRequirement.Required, true, 2), (aggregated.Requirement, aggregated.TreatmentRoom, aggregated.RelationIds.Count));
        var resolved = GameplayResolver.Resolve([base_, mod]).Balances.Single(b => b.DefinitionId == department);
        Assert.Equal((500m, 3m), (resolved.UnlockCost!.Value, resolved.UnlockDischargedPatients!.Value));
    }

    [Theory]
    [InlineData("treatment-kind", "relation-treatment-room")]
    [InlineData("treatment-optional", "relation-treatment-room")]
    [InlineData("two-treatment-rooms", "relation-treatment-room")]
    [InlineData("cycle", "department-cycle")]
    [InlineData("room-endpoint", "relation-type")]
    [InlineData("fractional-discharges", "balance-range")]
    [InlineData("negative-cost", "balance-range")]
    public void InvalidDepartmentContentIsRejected(string change, string code)
    {
        var (package, department, second, room) = Setup();
        var otherRoom = package.Definitions.OfType<RoomDefinition>().First() with { Id = "synthetic:other_room" };
        package.Definitions.Add(otherRoom);
        switch (change)
        {
            case "treatment-kind": package.Relations.Add(Relation("synthetic:bad", RelationKind.RoomFurniture, room, package.Definitions.OfType<FurnitureDefinition>().First().Id, treatment: true)); break;
            case "treatment-optional": package.Relations.Add(Relation("synthetic:bad", RelationKind.DepartmentRoom, department, room, RelationRequirement.Optional, true)); break;
            case "two-treatment-rooms":
                package.Relations.Add(Relation("synthetic:a", RelationKind.DepartmentRoom, department, room, treatment: true));
                package.Relations.Add(Relation("synthetic:b", RelationKind.DepartmentRoom, department, otherRoom.Id, treatment: true)); break;
            case "cycle":
                package.Relations.Add(Relation("synthetic:a", RelationKind.DepartmentPrerequisite, department, second));
                package.Relations.Add(Relation("synthetic:b", RelationKind.DepartmentPrerequisite, second, department)); break;
            case "room-endpoint": package.Relations.Add(Relation("synthetic:bad", RelationKind.DepartmentRoom, room, department)); break;
            case "fractional-discharges": package.Gameplay.Add(new() { DefinitionId = department, UnlockDischargedPatients = 2.5m }); break;
            default: package.Gameplay.Add(new() { DefinitionId = department, UnlockCost = -1 }); break;
        }
        Assert.Contains(ContentValidator.Validate(package), i => i.Code == code);
    }
}
