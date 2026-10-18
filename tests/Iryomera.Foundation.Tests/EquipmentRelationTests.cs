using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class EquipmentRelationTests
{
    private static ContentRelation Relation(string id, RelationKind kind, string from, string to, RelationRequirement? requirement = null) => new()
    { Id = id, Version = "1.0.0", Kind = kind, FromId = from, ToId = to, Provenance = Fixtures.Package().Relations[0].Provenance, Requirement = requirement };

    private static ContentPackage With(params ContentRelation[] relations)
    { var package = Fixtures.Package(); package.Relations.AddRange(relations); return package; }

    [Fact]
    public async Task TreatmentAndRoomEquipmentRelationsRoundTripAndMatchSchemas()
    {
        var package = With(Relation("synthetic:treatment_equipment_1", RelationKind.TreatmentEquipment, "synthetic:amber_session", "synthetic:demo_scanner"),
            Relation("synthetic:room_equipment_1", RelationKind.RoomEquipment, "synthetic:demo_room", "synthetic:demo_scanner", RelationRequirement.Required),
            Relation("synthetic:room_equipment_2", RelationKind.RoomEquipment, "synthetic:demo_room", "synthetic:demo_scanner", RelationRequirement.Optional));
        Assert.Empty(ContentValidator.Validate(package));
        foreach (var relation in package.Relations.TakeLast(3))
            Assert.True(Fixtures.Evaluate("contentRelation", JsonSerializer.SerializeToNode(relation, ContentJson.Options)).IsValid, relation.Id);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        Assert.Equal(RelationRequirement.Required, result.Value!.Relations.Single(r => r.Id == "synthetic:room_equipment_1").Requirement);
        Assert.DoesNotContain("\"requirement\"", JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options));
    }

    [Theory]
    [InlineData(RelationKind.TreatmentEquipment, "synthetic:demo_room", "synthetic:demo_scanner")]
    [InlineData(RelationKind.TreatmentEquipment, "synthetic:amber_session", "synthetic:demo_room")]
    [InlineData(RelationKind.RoomEquipment, "synthetic:amber_session", "synthetic:demo_scanner")]
    [InlineData(RelationKind.RoomEquipment, "synthetic:demo_room", "synthetic:amber_session")]
    public void NewRelationsRejectWrongEndpointTypes(RelationKind kind, string from, string to) =>
        Assert.Contains(ContentValidator.Validate(With(Relation("synthetic:wrong_1", kind, from, to))), i => i.Code == "relation-type");

    [Fact]
    public void RequirementIsOnlyAllowedOnRoomEquipment()
    {
        var other = Relation("synthetic:treatment_equipment_1", RelationKind.TreatmentEquipment, "synthetic:amber_session", "synthetic:demo_scanner", RelationRequirement.Required);
        Assert.Contains(ContentValidator.Validate(With(other)), i => i.Code == "relation-requirement");
        Assert.False(Fixtures.Evaluate("contentRelation", JsonSerializer.SerializeToNode(other, ContentJson.Options)).IsValid);
        var unknown = Relation("synthetic:room_equipment_1", RelationKind.RoomEquipment, "synthetic:demo_room", "synthetic:demo_scanner", (RelationRequirement)99);
        Assert.Contains(ContentValidator.Validate(With(unknown)), i => i.Code == "relation-requirement");
    }
}
