using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>Room rules, staff roles, routing and capacity (ADR 0017); all values are synthetic test numbers.</summary>
public sealed class ClinicContentTests
{
    private static ContentRelation Relation(string id, RelationKind kind, string from, string to, int? priority = null) => new()
    { Id = id, Version = "1.0.0", Kind = kind, FromId = from, ToId = to, Provenance = Fixtures.Package().Relations[0].Provenance, Priority = priority };
    private static string Id<T>(ContentPackage p) where T : ContentDefinition => p.Definitions.OfType<T>().First().Id;

    [Fact]
    public async Task RoomRulesRelationsAndCapacityRoundTripAndMatchSchemas()
    {
        var package = Fixtures.Package();
        var room = package.Definitions.OfType<RoomDefinition>().First();
        package.Definitions[package.Definitions.IndexOf(room)] = room with { MinimumCells = 6, RequiresDoor = true };
        package.Relations.Add(Relation("synthetic:exam_role", RelationKind.ExaminationStaffRole, Id<ExaminationDefinition>(package), Id<StaffRoleDefinition>(package)));
        package.Relations.Add(Relation("synthetic:route_1", RelationKind.DiseaseDepartment, Id<DiseaseDefinition>(package), Id<MedicalDepartmentDefinition>(package), 1));
        package.Gameplay.Add(new() { DefinitionId = Id<MedicalDepartmentDefinition>(package), Capacity = 4 });
        Assert.Empty(ContentValidator.Validate(package));
        Assert.True(Fixtures.Evaluate("roomDefinition", JsonSerializer.SerializeToNode<ContentDefinition>(package.Definitions.OfType<RoomDefinition>().First(), ContentJson.Options)).IsValid);
        foreach (var relation in package.Relations.TakeLast(2))
            Assert.True(Fixtures.Evaluate("contentRelation", JsonSerializer.SerializeToNode(relation, ContentJson.Options)).IsValid, relation.Id);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        Assert.Equal((6, true), (result.Value!.Definitions.OfType<RoomDefinition>().First().MinimumCells!.Value, result.Value.Definitions.OfType<RoomDefinition>().First().RequiresDoor!.Value));
        Assert.Equal(1, result.Value.Relations.Single(r => r.Id == "synthetic:route_1").Priority);
        Assert.Equal(4m, GameplayResolver.Resolve([result.Value]).Balances.Single(b => b.DefinitionId == Id<MedicalDepartmentDefinition>(package)).Capacity);
        var legacy = JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options);
        Assert.DoesNotContain("minimumCells", legacy); Assert.DoesNotContain("\"priority\"", legacy); Assert.DoesNotContain("\"capacity\"", legacy);
    }

    [Theory]
    [InlineData("room-size")]
    [InlineData("priority-kind")]
    [InlineData("priority-zero")]
    [InlineData("capacity-negative")]
    [InlineData("staff-role-endpoint")]
    [InlineData("department-endpoint")]
    public void InvalidClinicContentIsRejected(string change)
    {
        var package = Fixtures.Package();
        var disease = Id<DiseaseDefinition>(package); var department = Id<MedicalDepartmentDefinition>(package); var exam = Id<ExaminationDefinition>(package); var role = Id<StaffRoleDefinition>(package);
        var code = change switch
        {
            "room-size" => Room(package),
            "priority-kind" => Add(package, Relation("synthetic:bad", RelationKind.ExaminationStaffRole, exam, role, 1), "relation-priority"),
            "priority-zero" => Add(package, Relation("synthetic:bad", RelationKind.DiseaseDepartment, disease, department, 0), "relation-priority"),
            "capacity-negative" => Balance(package, department),
            "staff-role-endpoint" => Add(package, Relation("synthetic:bad", RelationKind.ExaminationStaffRole, exam, department), "relation-type"),
            _ => Add(package, Relation("synthetic:bad", RelationKind.DiseaseDepartment, disease, role), "relation-type")
        };
        Assert.Contains(ContentValidator.Validate(package), i => i.Code == code);

        static string Room(ContentPackage p)
        { var room = p.Definitions.OfType<RoomDefinition>().First(); p.Definitions[p.Definitions.IndexOf(room)] = room with { MinimumCells = 0 }; return "room-size"; }
        static string Add(ContentPackage p, ContentRelation relation, string expected) { p.Relations.Add(relation); return expected; }
        static string Balance(ContentPackage p, string target) { p.Gameplay.Add(new() { DefinitionId = target, Capacity = -1 }); return "balance-range"; }
    }

    [Fact]
    public async Task PricesAndWagesRoundTripOverrideAndReportConflicts()
    {
        var base_ = Fixtures.Package(); var furniture = Id<FurnitureDefinition>(base_); var role = Id<StaffRoleDefinition>(base_);
        base_.Gameplay.Add(new() { DefinitionId = furniture, PurchaseCost = 1200 });
        base_.Gameplay.Add(new() { DefinitionId = role, WagePerHour = 30 });
        Assert.Empty(ContentValidator.Validate(base_));
        Assert.True(Fixtures.Evaluate("gameplayBalance", JsonSerializer.SerializeToNode(base_.Gameplay[^1], ContentJson.Options)).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, base_); bytes.Position = 0;
        var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(read.IsValid, string.Join(';', read.Issues));
        ContentPackage Mod(string ns, decimal purchase) => new()
        {
            Manifest = new() { Id = ns + ":mod", Namespace = ns, Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = base_.Manifest.Id, VersionRange = "[1.0.0,2.0.0)" }] },
            GameplayOverrides = [new() { Id = ns + ":prices", Version = "1.0.0", TargetDefinitionId = furniture, PurchaseCost = purchase, WagePerHour = null }]
        };
        var (a, b) = (Mod("mod_a", 900), Mod("mod_b", 1500));
        b.GameplayOverrides!.Add(new() { Id = "mod_b:wages", Version = "1.0.0", TargetDefinitionId = role, WagePerHour = 45 });
        Assert.Empty(ContentValidator.Validate(a, [read.Value!]));
        var resolved = GameplayResolver.Resolve([read.Value!, a, b]);
        Assert.Equal((1500m, 45m), (resolved.Balances.Single(x => x.DefinitionId == furniture).PurchaseCost!.Value, resolved.Balances.Single(x => x.DefinitionId == role).WagePerHour!.Value));
        var conflict = Assert.Single(resolved.Conflicts);
        Assert.Equal((furniture, "purchaseCost", "mod_b:mod"), (conflict.TargetDefinitionId, conflict.Field, conflict.WinnerPackageId));
        Assert.True(Fixtures.Evaluate("gameplayOverride", JsonSerializer.SerializeToNode(b.GameplayOverrides[1], ContentJson.Options)).IsValid);
        var legacy = JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options);
        Assert.DoesNotContain("purchaseCost", legacy); Assert.DoesNotContain("wagePerHour", legacy);
    }

    [Theory]
    [InlineData("purchase")]
    [InlineData("wage")]
    [InlineData("override")]
    public void NegativePricesAndWagesAreRejected(string change)
    {
        var base_ = Fixtures.Package(); var target = Id<FurnitureDefinition>(base_);
        if (change == "override")
        {
            var mod = new ContentPackage
            {
                Manifest = new() { Id = "mod_a:mod", Namespace = "mod_a", Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = base_.Manifest.Id, VersionRange = "[1.0.0,2.0.0)" }] },
                GameplayOverrides = [new() { Id = "mod_a:cheap", Version = "1.0.0", TargetDefinitionId = target, PurchaseCost = -1 }]
            };
            Assert.Contains(ContentValidator.Validate(mod, [base_]), i => i.Code == "balance-range");
            return;
        }
        base_.Gameplay.Add(change == "purchase" ? new() { DefinitionId = target, PurchaseCost = -5 } : new() { DefinitionId = Id<StaffRoleDefinition>(base_), WagePerHour = -1 });
        Assert.Contains(ContentValidator.Validate(base_), i => i.Code == "balance-range");
    }

    [Fact]
    public void CapacityCanBeOverriddenByAMod()
    {
        var base_ = Fixtures.Package(); var department = Id<MedicalDepartmentDefinition>(base_);
        base_.Gameplay.Add(new() { DefinitionId = department, Capacity = 4 });
        var mod = new ContentPackage
        {
            Manifest = new() { Id = "mod_a:mod", Namespace = "mod_a", Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = base_.Manifest.Id, VersionRange = "[1.0.0,2.0.0)" }] },
            GameplayOverrides = [new() { Id = "mod_a:bigger_department", Version = "1.0.0", TargetDefinitionId = department, Capacity = 6 }]
        };
        Assert.Empty(ContentValidator.Validate(mod, [base_]));
        Assert.Equal(6m, GameplayResolver.Resolve([base_, mod]).Balances.Single(b => b.DefinitionId == department).Capacity);
    }
}
