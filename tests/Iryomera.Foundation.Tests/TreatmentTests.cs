using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>Treatments, pair balancing and complications (game ADR 0021); all values are synthetic test numbers.</summary>
public sealed class TreatmentTests
{
    private static ContentRelation Relation(string id, RelationKind kind, string from, string to, int? priority = null) => new()
    { Id = id, Version = "1.0.0", Kind = kind, FromId = from, ToId = to, Provenance = Fixtures.Package().Relations[0].Provenance, Priority = priority };

    private static (ContentPackage Package, string Disease, string Treatment, string Complication) Setup(ComplicationEffect effect = ComplicationEffect.ExtraStay)
    {
        var package = Fixtures.Package();
        var disease = package.Definitions.OfType<DiseaseDefinition>().First().Id;
        var treatment = package.Definitions.OfType<TreatmentDefinition>().First();
        var complication = new ComplicationDefinition
        { Id = "synthetic:wound", NameKey = treatment.NameKey, Version = "1.0.0", Provenance = treatment.Provenance, Effect = effect, Timing = ComplicationTiming.DuringStay };
        package.Definitions.Add(complication);
        package.Relations.RemoveAll(r => r.Kind == RelationKind.DiseaseTreatment);
        package.Relations.Add(Relation("synthetic:pair", RelationKind.DiseaseTreatment, disease, treatment.Id, 1));
        package.Relations.Add(Relation("synthetic:treatment_room", RelationKind.TreatmentRoom, treatment.Id, package.Definitions.OfType<RoomDefinition>().First().Id));
        package.Relations.Add(Relation("synthetic:treatment_role", RelationKind.TreatmentStaffRole, treatment.Id, package.Definitions.OfType<StaffRoleDefinition>().First().Id));
        package.Relations.Add(Relation("synthetic:risk", RelationKind.TreatmentComplication, treatment.Id, complication.Id));
        return (package, disease, treatment.Id, complication.Id);
    }

    [Fact]
    public async Task TreatmentsPairBalancingAndComplicationsRoundTripAndMatchSchemas()
    {
        var (package, disease, treatment, complication) = Setup();
        package.Gameplay.RemoveAll(b => b.DefinitionId == disease || b.DefinitionId == treatment);
        package.Gameplay.Add(new() { DefinitionId = disease, TreatmentCost = 100, StayDurationSeconds = 3600 });
        package.Gameplay.Add(new() { DefinitionId = treatment, TreatmentCost = 200, TreatmentDurationSeconds = 600 });
        package.Gameplay.Add(new() { DefinitionId = "synthetic:pair", TreatmentCost = 300, StayChance = 0.5m, StayDurationMaxSeconds = 7200 });
        package.Gameplay.Add(new() { DefinitionId = "synthetic:risk", ComplicationChance = 0.1m });
        package.Gameplay.Add(new() { DefinitionId = complication, ExtraStayDurationSeconds = 21600 });
        Assert.Empty(ContentValidator.Validate(package));
        Assert.True(Fixtures.Evaluate("complicationDefinition", JsonSerializer.SerializeToNode<ContentDefinition>(package.Definitions.Single(d => d.Id == complication), ContentJson.Options)).IsValid);
        foreach (var relation in package.Relations.Where(r => r.Kind is RelationKind.TreatmentRoom or RelationKind.TreatmentStaffRole or RelationKind.TreatmentComplication or RelationKind.DiseaseTreatment))
            Assert.True(Fixtures.Evaluate("contentRelation", JsonSerializer.SerializeToNode(relation, ContentJson.Options)).IsValid, relation.Id);
        Assert.True(Fixtures.Evaluate("gameplayBalance", JsonSerializer.SerializeToNode(package.Gameplay.Single(b => b.DefinitionId == "synthetic:pair"), ContentJson.Options)).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(read.IsValid, string.Join(';', read.Issues));
        ContentPackage[] set = [read.Value!];
        var option = Treatments.Options(set, disease).Single();
        Assert.Equal((treatment, "synthetic:pair", 1), (option.TreatmentId, option.RelationId, option.Priority!.Value));
        Assert.True(Treatments.Playable(set, treatment));
        var effective = Treatments.Effective(GameplayResolver.Resolve(set), disease, treatment, option.RelationId);
        Assert.Equal((300m, 600m, 0.5m, 3600m, 7200m), (effective.TreatmentCost!.Value, effective.TreatmentDurationSeconds!.Value, effective.StayChance!.Value, effective.StayDurationSeconds!.Value, effective.StayDurationMaxSeconds!.Value));
        Assert.Equal((TreatmentValueSource.Pair, TreatmentValueSource.Treatment, TreatmentValueSource.Disease),
            (effective.Sources["treatmentCost"], effective.Sources["treatmentDurationSeconds"], effective.Sources["stayDurationSeconds"]));
        Assert.Equal(("synthetic:wound", "synthetic:risk"), Treatments.Complications(set, treatment).Single());
        var legacy = JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options);
        Assert.DoesNotContain("stayChance", legacy); Assert.DoesNotContain("complication", legacy);
    }

    [Fact]
    public void ModsOverridePairValuesAndConflictsAreReported()
    {
        var (base_, disease, treatment, _) = Setup();
        base_.Gameplay.Add(new() { DefinitionId = "synthetic:pair", TreatmentCost = 300 });
        ContentPackage Mod(string ns, decimal cost) => new()
        {
            Manifest = new() { Id = ns + ":mod", Namespace = ns, Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = base_.Manifest.Id, VersionRange = "[1.0.0,2.0.0)" }] },
            GameplayOverrides = [new() { Id = ns + ":pair_cost", Version = "1.0.0", TargetDefinitionId = "synthetic:pair", TreatmentCost = cost }]
        };
        var (a, b) = (Mod("mod_a", 250), Mod("mod_b", 350));
        Assert.Empty(ContentValidator.Validate(a, [base_]));
        var resolved = GameplayResolver.Resolve([base_, a, b]);
        Assert.Equal(350m, Treatments.Effective(resolved, disease, treatment, "synthetic:pair").TreatmentCost);
        Assert.Contains(resolved.Conflicts, c => c.TargetDefinitionId == "synthetic:pair" && c.Field == "treatmentCost" && c.WinnerPackageId == "mod_b:mod");
        Assert.False(Treatments.Playable([Fixtures.Package()], Fixtures.Package().Definitions.OfType<TreatmentDefinition>().First().Id)); // older content stays valid but not playable
    }

    [Theory]
    [InlineData("target-kind", "gameplay-target")]
    [InlineData("two-roles", "treatment-staff-role")]
    [InlineData("effect-relation", "complication-effect")]
    [InlineData("missing-follow-up", "complication-effect")]
    [InlineData("cycle", "treatment-cycle")]
    [InlineData("stay-range", "balance-range")]
    [InlineData("chance", "balance-range")]
    [InlineData("priority-kind", "relation-priority")]
    [InlineData("complication-endpoint", "relation-type")]
    public void InvalidTreatmentContentIsRejected(string change, string code)
    {
        var (package, disease, treatment, complication) = Setup(change is "missing-follow-up" or "cycle" ? ComplicationEffect.FollowUpTreatment : ComplicationEffect.ExtraStay);
        var role = package.Definitions.OfType<StaffRoleDefinition>().First();
        switch (change)
        {
            case "target-kind": package.Gameplay.Add(new() { DefinitionId = "synthetic:treatment_room", TreatmentCost = 1 }); break;
            case "two-roles":
                package.Definitions.Add(role with { Id = "synthetic:second_role" });
                package.Relations.Add(Relation("synthetic:role_2", RelationKind.TreatmentStaffRole, treatment, "synthetic:second_role")); break;
            case "effect-relation": package.Relations.Add(Relation("synthetic:follow", RelationKind.ComplicationTreatment, complication, treatment)); break;
            case "missing-follow-up": break;
            case "cycle": package.Relations.Add(Relation("synthetic:follow", RelationKind.ComplicationTreatment, complication, treatment)); break;
            case "stay-range": package.Gameplay.Add(new() { DefinitionId = "synthetic:pair", StayDurationSeconds = 7200, StayDurationMaxSeconds = 3600 }); break;
            case "chance": package.Gameplay.Add(new() { DefinitionId = "synthetic:risk", ComplicationChance = 1.5m }); break;
            case "priority-kind": package.Relations.Add(Relation("synthetic:bad", RelationKind.TreatmentRoom, treatment, package.Definitions.OfType<RoomDefinition>().First().Id, 1)); break;
            default: package.Relations.Add(Relation("synthetic:bad", RelationKind.TreatmentComplication, treatment, disease)); break;
        }
        Assert.Contains(ContentValidator.Validate(package), i => i.Code == code);
    }
}
