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

    [Fact]
    public async Task RoomRolesOpeningHoursArrivalAndStayRoundTripAndOverride()
    {
        var (base_, department, _, roomId) = Setup();
        var room = base_.Definitions.OfType<RoomDefinition>().Single(r => r.Id == roomId);
        base_.Definitions[base_.Definitions.IndexOf(room)] = room with { Role = RoomRole.Ward, Shareable = true };
        var disease = base_.Definitions.OfType<DiseaseDefinition>().First().Id;
        base_.Gameplay.Add(new() { DefinitionId = department, OpenFromHour = 8, OpenToHour = 18 });
        base_.Gameplay.RemoveAll(x => x.DefinitionId == disease);
        base_.Gameplay.Add(new() { DefinitionId = disease, Arrival = PatientArrival.WalkIn, StayDurationSeconds = 43200 });
        Assert.Empty(ContentValidator.Validate(base_));
        Assert.True(Fixtures.Evaluate("roomDefinition", JsonSerializer.SerializeToNode<ContentDefinition>(base_.Definitions.Single(d => d.Id == roomId), ContentJson.Options)).IsValid);
        Assert.True(Fixtures.Evaluate("gameplayBalance", JsonSerializer.SerializeToNode(base_.Gameplay[^1], ContentJson.Options)).IsValid);
        Assert.Equal("walkIn", JsonSerializer.SerializeToNode(base_.Gameplay[^1], ContentJson.Options)!["arrival"]!.GetValue<string>());
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, base_); bytes.Position = 0;
        var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(read.IsValid, string.Join(';', read.Issues));
        Assert.Equal((RoomRole.Ward, true), (read.Value!.Definitions.OfType<RoomDefinition>().Single(r => r.Id == roomId).Role!.Value, read.Value.Definitions.OfType<RoomDefinition>().Single(r => r.Id == roomId).Shareable!.Value));
        ContentPackage Mod(string ns, PatientArrival arrival) => new()
        {
            Manifest = new() { Id = ns + ":mod", Namespace = ns, Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = base_.Manifest.Id, VersionRange = "[1.0.0,2.0.0)" }] },
            GameplayOverrides = [new() { Id = ns + ":night", Version = "1.0.0", TargetDefinitionId = disease, Arrival = arrival }, new() { Id = ns + ":hours", Version = "1.0.0", TargetDefinitionId = department, OpenFromHour = 0, OpenToHour = 24 }]
        };
        var (a, b) = (Mod("mod_a", PatientArrival.Emergency), Mod("mod_b", PatientArrival.WalkIn));
        Assert.Empty(ContentValidator.Validate(a, [read.Value]));
        var resolved = GameplayResolver.Resolve([read.Value, a, b]);
        var flow = resolved.Balances.Single(x => x.DefinitionId == disease);
        Assert.Equal((PatientArrival.WalkIn, 43200m), (flow.Arrival!.Value, flow.StayDurationSeconds!.Value));
        Assert.Equal((PatientArrival.Emergency), GameplayResolver.Resolve([read.Value, a]).Balances.Single(x => x.DefinitionId == disease).Arrival);
        Assert.Contains(resolved.Conflicts, c => c.Field == "arrival" && c.WinnerPackageId == "mod_b:mod");
        Assert.Equal((0m, 24m), (resolved.Balances.Single(x => x.DefinitionId == department).OpenFromHour!.Value, resolved.Balances.Single(x => x.DefinitionId == department).OpenToHour!.Value));
        var legacy = JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options);
        Assert.DoesNotContain("openFromHour", legacy); Assert.DoesNotContain("\"arrival\"", legacy); Assert.DoesNotContain("\"role\"", legacy); Assert.DoesNotContain("shareable", legacy);
    }

    [Theory]
    [InlineData(25.0, null)]
    [InlineData(18.0, 8.0)]
    [InlineData(8.5, 18.0)]
    [InlineData(null, -1.0)]
    public void InvalidOpeningHoursAreRejected(double? from, double? to)
    {
        var (package, department, _, _) = Setup();
        package.Gameplay.Add(new() { DefinitionId = department, OpenFromHour = (decimal?)from, OpenToHour = (decimal?)to });
        Assert.Contains(ContentValidator.Validate(package), i => i.Code == "balance-range");
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
