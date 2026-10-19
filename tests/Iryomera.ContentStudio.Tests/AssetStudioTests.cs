using System.Net;
using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.ContentStudio.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Iryomera.ContentStudio.Tests;

/// <summary>Catalog IDs, previews and placement values below are invented test data; no purchased asset is used.</summary>
public sealed class AssetStudioTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    [Fact]
    public async Task AssetsAndPlacementAreSavedAndResetOnlyTheDefinitionReview()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); await data.Approve(id);
        var state = await data.Projects.GetAsync(id);
        var placement = new FurniturePlacement
        {
            Footprint = new() { Width = 2, Depth = 1 }, Mount = FurnitureMount.Wall, MountHeightMeters = 1.2m,
            Surfaces = [new() { Key = "top", HeightMeters = 0.75m, X = 0, Z = 0, Width = 2, Depth = 1 }]
        };
        await data.Projects.SaveAssetsAsync(id, state.Project.Revision, "synthetic:demo_desk", [new() { CatalogId = "iryomera.assets:furniture.desk_example", Kind = AssetKind.ModelGlb }], placement, "Synthetic editor", "Own fixture asset");
        state = await data.Projects.GetAsync(id);
        var desk = state.Package.Definitions.OfType<FurnitureDefinition>().Single(d => d.Id == "synthetic:demo_desk");
        Assert.Equal("iryomera.assets:furniture.desk_example", desk.CatalogAssets!.Single().CatalogId);
        Assert.Equal((FurnitureMount.Wall, 1.2m, "top"), (desk.Placement!.Mount!.Value, desk.Placement.MountHeightMeters!.Value, desk.Placement.Surfaces!.Single().Key));
        Assert.Equal(ReviewStatus.Unreviewed, desk.Provenance.ReviewStatus);
        Assert.All(state.Package.Relations, r => Assert.Equal(ReviewStatus.Approved, r.Provenance.ReviewStatus));
        Assert.Contains(await data.Projects.AuditAsync(id), a => a.Action == "asset-edit");

        var wheelchair = new FurniturePlacement { Footprint = new() { Width = 1, Depth = 1 }, InteractionPoints = [new() { Key = "seat", Kind = "seat", X = 0.5m, Y = 0.45m, Z = 0.5m }, new() { Key = "handle", Kind = "handle", X = 0.5m, Y = 0, Z = 0 }] };
        await data.Projects.SaveAssetsAsync(id, state.Project.Revision, "synthetic:demo_desk", [], wheelchair, "Synthetic editor", "Movable",
            FurnitureMobility.Pushable, new() { PatientCapacity = 1, Posture = TransportPosture.Seated });
        state = await data.Projects.GetAsync(id);
        desk = state.Package.Definitions.OfType<FurnitureDefinition>().Single(d => d.Id == "synthetic:demo_desk");
        Assert.Equal((FurnitureMobility.Pushable, 1, 2), (desk.Mobility!.Value, desk.Transport!.PatientCapacity, desk.Placement!.InteractionPoints.Count));
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveAssetsAsync(id, state.Project.Revision, "synthetic:demo_desk", [], wheelchair with { InteractionPoints = [] },
            "Synthetic editor", "No handle", FurnitureMobility.Pushable));
        await Assert.ThrowsAsync<ArgumentException>(() => data.Projects.SaveAssetsAsync(id, state.Project.Revision, "synthetic:demo_scanner", [], null, "Synthetic editor", "Mobility on equipment", FurnitureMobility.Movable));

        await data.Projects.SaveAssetsAsync(id, state.Project.Revision, "synthetic:demo_desk", [], null, "Synthetic editor", "Remove assets");
        desk = (await data.Projects.GetAsync(id)).Package.Definitions.OfType<FurnitureDefinition>().Single(d => d.Id == "synthetic:demo_desk");
        Assert.Null(desk.CatalogAssets); Assert.Null(desk.Placement);
    }

    [Fact]
    public async Task InvalidAssetEditsAreRejectedWithoutChangingTheProject()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); var state = await data.Projects.GetAsync(id);
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveAssetsAsync(id, state.Project.Revision, "synthetic:demo_scanner",
            [new() { CatalogId = "scanner.glb", Kind = AssetKind.ModelGlb }], null, "Synthetic editor", "Invalid ID"));
        await Assert.ThrowsAsync<ArgumentException>(() => data.Projects.SaveAssetsAsync(id, state.Project.Revision, "synthetic:demo_scanner",
            [], new() { Footprint = new() { Width = 1, Depth = 1 } }, "Synthetic editor", "Placement on equipment"));
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveAssetsAsync(id, state.Project.Revision, "synthetic:demo_desk",
            [], new() { Footprint = new() { Width = 1, Depth = 1 }, Surfaces = [new() { Key = "top", HeightMeters = 1, X = 0, Z = 0, Width = 2, Depth = 1 }] }, "Synthetic editor", "Surface too large"));
        Assert.Equal(state.Project.Revision, (await data.Projects.GetAsync(id)).Project.Revision);
    }

    [Fact]
    public async Task RoomEquipmentRequirementIsSavedAndRestrictedToRoomEquipment()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); var state = await data.Projects.GetAsync(id);
        var provenance = state.Package.Relations[0].Provenance with { ExternalId = "room_equipment_test" };
        await data.Projects.SaveRelationAsync(id, state.Project.Revision, new() { Id = "synthetic:room_equipment_test", Version = "1.0.0", Kind = RelationKind.RoomEquipment,
            FromId = "synthetic:demo_room", ToId = "synthetic:demo_scanner", Provenance = provenance, Requirement = RelationRequirement.Required }, "Synthetic editor", "Required device", create: true);
        state = await data.Projects.GetAsync(id);
        Assert.Equal(RelationRequirement.Required, state.Package.Relations.Single(r => r.Id == "synthetic:room_equipment_test").Requirement);
        await data.Projects.SaveRelationAsync(id, state.Project.Revision, new() { Id = "synthetic:room_furniture_test", Version = "1.0.0", Kind = RelationKind.RoomFurniture,
            FromId = "synthetic:demo_room", ToId = "synthetic:demo_desk", Provenance = provenance with { ExternalId = "room_furniture_test" }, Requirement = RelationRequirement.Optional }, "Synthetic editor", "Optional desk", create: true);
        state = await data.Projects.GetAsync(id);
        var offered = RoomContents.Placeable([state.Package], "synthetic:demo_room");
        Assert.Contains(offered, i => i.DefinitionId == "synthetic:demo_scanner" && i.Requirement == RelationRequirement.Required);
        Assert.Contains(offered, i => i.DefinitionId == "synthetic:demo_desk");
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveRelationAsync(id, state.Project.Revision, new() { Id = "synthetic:treatment_equipment_test", Version = "1.0.0",
            Kind = RelationKind.TreatmentEquipment, FromId = "synthetic:amber_session", ToId = "synthetic:demo_scanner", Provenance = provenance with { ExternalId = "treatment_equipment_test" },
            Requirement = RelationRequirement.Required }, "Synthetic editor", "Requirement on the wrong kind", create: true));
    }

    [Fact]
    public async Task RoomRulesAndRoutingPrioritiesAreSaved()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); var state = await data.Projects.GetAsync(id);
        await data.Projects.SaveRoomRulesAsync(id, state.Project.Revision, "synthetic:demo_room", 6, true, "Synthetic editor", "Room rules", RoomRole.Ward, true);
        state = await data.Projects.GetAsync(id);
        var room = state.Package.Definitions.OfType<RoomDefinition>().Single(r => r.Id == "synthetic:demo_room");
        Assert.Equal((6, true, ReviewStatus.Unreviewed), (room.MinimumCells!.Value, room.RequiresDoor!.Value, room.Provenance.ReviewStatus));
        Assert.Equal((RoomRole.Ward, true), (room.Role!.Value, room.Shareable!.Value));
        await Assert.ThrowsAsync<ArgumentException>(() => data.Projects.SaveRoomRulesAsync(id, state.Project.Revision, "synthetic:demo_desk", 6, null, "Synthetic editor", "Not a room"));
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveRoomRulesAsync(id, state.Project.Revision, "synthetic:demo_room", 0, null, "Synthetic editor", "Too small"));
        var department = state.Package.Definitions.OfType<MedicalDepartmentDefinition>().First().Id;
        await data.Projects.SaveRelationAsync(id, state.Project.Revision, new() { Id = "synthetic:route_test", Version = "1.0.0", Kind = RelationKind.DiseaseDepartment, FromId = "synthetic:amber_syndrome",
            ToId = department, Provenance = state.Package.Relations[0].Provenance with { ExternalId = "route_test" }, Priority = 1 }, "Synthetic editor", "Routing", create: true);
        Assert.Equal(1, (await data.Projects.GetAsync(id)).Package.Relations.Single(r => r.Id == "synthetic:route_test").Priority);
    }

    [Fact]
    public async Task ComplicationsAndPairBalancingAreEditable()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); var state = await data.Projects.GetAsync(id);
        await data.Projects.SaveDefinitionAsync(id, state.Project.Revision, new() { Id = "synthetic:wound", Kind = "complication", NameEn = "Synthetic wound problem", NameDe = "Synthetisches Wundproblem",
            ExternalId = "wound", Actor = "Synthetic editor", Note = "New complication" }, create: true);
        state = await data.Projects.GetAsync(id);
        Assert.Equal((ComplicationEffect.ExtraStay, ComplicationTiming.DuringStay), (state.Package.Definitions.OfType<ComplicationDefinition>().Single().Effect, state.Package.Definitions.OfType<ComplicationDefinition>().Single().Timing));
        await data.Projects.SaveComplicationRulesAsync(id, state.Project.Revision, "synthetic:wound", ComplicationEffect.ExtraStay, ComplicationTiming.DuringTreatment, "Synthetic editor", "Timing");
        state = await data.Projects.GetAsync(id);
        Assert.Equal(ComplicationTiming.DuringTreatment, state.Package.Definitions.OfType<ComplicationDefinition>().Single().Timing);
        await Assert.ThrowsAsync<ArgumentException>(() => data.Projects.SaveComplicationRulesAsync(id, state.Project.Revision, "synthetic:demo_room", ComplicationEffect.ExtraStay, ComplicationTiming.DuringStay, "Synthetic editor", "Not a complication"));
        var pair = state.Package.Relations.First(r => r.Kind == RelationKind.DiseaseTreatment);
        await data.Projects.SaveGameplayAsync(id, state.Project.Revision, new() { DefinitionId = pair.Id, StayChance = 0.5m, StayDurationSeconds = 3600, StayDurationMaxSeconds = 7200 }, "Synthetic editor", "Pair values");
        state = await data.Projects.GetAsync(id);
        Assert.Equal(0.5m, state.Package.Gameplay.Single(b => b.DefinitionId == pair.Id).StayChance);
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveGameplayAsync(id, state.Project.Revision, new() { DefinitionId = pair.Id, StayChance = 2 }, "Synthetic editor", "Out of range"));
    }

    [Fact]
    public void CatalogIndexIsSchemaValidatedAndToleratesUnknownFields()
    {
        static (IReadOnlyList<CatalogAssetIndexEntry> Entries, string? Error) Parse(string json) { using var document = JsonDocument.Parse(json); return StudioCatalog.Parse(document.RootElement); }
        var valid = Parse("{\"schemaVersion\":\"1.0.0\",\"generator\":\"synthetic\",\"assets\":[{\"id\":\"iryomera.assets:wall.notice_board\",\"kind\":\"modelGlb\",\"category\":\"wall\",\"mount\":\"wall\",\"footprint\":{\"width\":1,\"depth\":1},\"extra\":true},{\"id\":\"iryomera.assets:furniture.desk\",\"kind\":\"modelGlb\",\"category\":\"furniture\"}]}");
        Assert.Null(valid.Error);
        Assert.Equal(["iryomera.assets:furniture.desk", "iryomera.assets:wall.notice_board"], valid.Entries.Select(e => e.Id));
        Assert.Equal((FurnitureMount.Wall, 1, 1), (valid.Entries[1].Mount!.Value, valid.Entries[1].FootprintWidth!.Value, valid.Entries[1].FootprintDepth!.Value));
        var movable = Parse("{\"schemaVersion\":\"1.0.0\",\"assets\":[{\"id\":\"iryomera.assets:furniture.wheelchair\",\"kind\":\"modelGlb\",\"category\":\"furniture\",\"mobility\":\"pushable\",\"interactionPointKinds\":[\"seat\",\"handle\"]}]}");
        Assert.Null(movable.Error);
        Assert.Equal(FurnitureMobility.Pushable, movable.Entries[0].Mobility);
        Assert.Equal(["seat", "handle"], movable.Entries[0].InteractionPointKinds!);
        Assert.NotNull(Parse("{\"schemaVersion\":\"1.0.0\",\"assets\":[{\"id\":\"iryomera.assets:a\",\"kind\":\"modelGlb\",\"category\":\"x\",\"interactionPointKinds\":[\"seat\",\"seat\"]}]}").Error);
        Assert.NotNull(Parse("{\"schemaVersion\":\"1.0.0\",\"assets\":[{\"id\":\"not an id\",\"kind\":\"modelGlb\",\"category\":\"wall\"}]}").Error);
        Assert.NotNull(Parse("{\"schemaVersion\":\"2.0.0\",\"assets\":[]}").Error);
        Assert.NotNull(Parse("{\"schemaVersion\":\"1.0.0\",\"assets\":[{\"id\":\"iryomera.assets:a\",\"kind\":\"modelGlb\",\"category\":\"x\"},{\"id\":\"iryomera.assets:a\",\"kind\":\"modelGlb\",\"category\":\"x\"}]}").Error);
    }

    [Fact]
    public async Task PreviewsAreReadOnlyFromValidIdsInsideTheConfiguredFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "iryomera-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "iryomera.assets"));
            await File.WriteAllBytesAsync(Path.Combine(folder, "iryomera.assets", "wall.notice_board.png"), Png);
            await File.WriteAllTextAsync(Path.Combine(folder, "iryomera.assets", "wall.fake.png"), "not a png");
            await File.WriteAllBytesAsync(Path.Combine(folder, "iryomera.assets", "wall.big.png"), [.. Png, .. new byte[StudioCatalog.MaxPreviewBytes]]);
            var catalog = new StudioCatalog(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:CatalogPreviewDirectory"] = folder }).Build());
            Assert.Equal(Png, await catalog.PreviewAsync("iryomera.assets:wall.notice_board"));
            foreach (var id in new[] { "iryomera.assets:wall.fake", "iryomera.assets:wall.big", "iryomera.assets:wall.missing", "../secret", "iryomera.assets:..\\x", "" })
                Assert.Null(await catalog.PreviewAsync(id));
            Assert.Null(await new StudioCatalog(new ConfigurationBuilder().Build()).PreviewAsync("iryomera.assets:wall.notice_board"));

            var data = Path.Combine(Path.GetTempPath(), "iryomera-studio-catalog-http-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using var factory = new CatalogWebFactory(data, folder); using var client = factory.CreateClient();
                var ok = await client.GetAsync("/catalog-preview?id=" + Uri.EscapeDataString("iryomera.assets:wall.notice_board"));
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode); Assert.Equal("image/png", ok.Content.Headers.ContentType!.MediaType);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/catalog-preview?id=" + Uri.EscapeDataString("iryomera.assets:wall.fake"))).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/catalog-preview?id=..%2F..%2Fsecret")).StatusCode);
            }
            finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(data)) Directory.Delete(data, true); }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private sealed class CatalogWebFactory(string data, string catalog) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:DataDirectory"] = data, ["Studio:CatalogPreviewDirectory"] = catalog }));
    }
}
