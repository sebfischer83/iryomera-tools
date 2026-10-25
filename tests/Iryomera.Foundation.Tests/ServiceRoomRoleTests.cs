using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>Staff and service room roles (ADR 0024): staff room, ward office, separate toilets and cleaning room.</summary>
public sealed class ServiceRoomRoleTests
{
    [Theory]
    [InlineData(RoomRole.StaffRoom, "staffRoom")]
    [InlineData(RoomRole.WardOffice, "wardOffice")]
    [InlineData(RoomRole.PatientToilet, "patientToilet")]
    [InlineData(RoomRole.StaffToilet, "staffToilet")]
    [InlineData(RoomRole.CleaningRoom, "cleaningRoom")]
    public async Task ServiceRoomRolesRoundTripAndMatchTheSchema(RoomRole role, string json)
    {
        var package = Fixtures.Package();
        var room = package.Definitions.OfType<RoomDefinition>().First();
        package.Definitions[package.Definitions.IndexOf(room)] = room with { Role = role };
        Assert.Empty(ContentValidator.Validate(package));
        var node = JsonSerializer.SerializeToNode<ContentDefinition>(package.Definitions.Single(d => d.Id == room.Id), ContentJson.Options)!;
        Assert.Equal(json, node["role"]!.GetValue<string>());
        Assert.True(Fixtures.Evaluate("roomDefinition", node).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(read.IsValid, string.Join(';', read.Issues));
        Assert.Equal(role, read.Value!.Definitions.OfType<RoomDefinition>().Single(r => r.Id == room.Id).Role);
    }

    [Fact]
    public void An_unknown_room_role_is_rejected_by_the_schema()
    {
        var package = Fixtures.Package();
        var node = JsonSerializer.SerializeToNode<ContentDefinition>(package.Definitions.OfType<RoomDefinition>().First(), ContentJson.Options)!;
        node["role"] = "kitchen";
        Assert.False(Fixtures.Evaluate("roomDefinition", node).IsValid);
    }
}
