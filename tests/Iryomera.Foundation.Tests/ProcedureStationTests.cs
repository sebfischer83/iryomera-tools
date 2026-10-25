using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>Stations of examinations and treatments (ADR 0025); synthetic test values.</summary>
public sealed class ProcedureStationTests
{
    private const string Scan = "synthetic:amber_scan", Session = "synthetic:amber_session", Scanner = "synthetic:demo_scanner",
        Room = "synthetic:demo_room", Desk = "synthetic:demo_desk";

    private static StationPlace Place(string kind, string? equipment = null) => new() { PointKind = kind, EquipmentId = equipment };

    private static ProcedureStation Station(string key, decimal share, StationPlace patient, StationPlace? staff = null) =>
        new() { Key = key, Share = share, Patient = patient, Staff = staff };

    private static ProcedureStationSet Set(string definition, params ProcedureStation[] stations) => new() { DefinitionId = definition, Stations = [.. stations] };

    private static ContentPackage With(params ProcedureStationSet[] sets) => Fixtures.Package() with { ProcedureStations = [.. sets] };

    private static ProcedureStationSet TalkThenScan() => Set(Scan,
        Station("talk", 0.25m, Place("seat"), Place(ProcedureStations.Standing)),
        Station("scan", 0.75m, Place("lying", Scanner), Place("operator", Scanner)));

    private static ContentRelation Relation(string id, RelationKind kind, string from, string to) => new()
    { Id = id, Version = "1.0.0", Kind = kind, FromId = from, ToId = to, Provenance = Fixtures.Package().Relations[0].Provenance };

    [Fact]
    public async Task StationsRoundTripMatchSchemasAndStayOutOfLegacyPackages()
    {
        var package = With(TalkThenScan(), Set(Session, Station("treat", 1m, Place("lying"))));
        Assert.Empty(ContentValidator.Validate(package));
        foreach (var set in package.ProcedureStations!)
            Assert.True(Fixtures.Evaluate("procedureStationSet", JsonSerializer.SerializeToNode(set, ContentJson.Options)).IsValid, set.DefinitionId);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(read.IsValid, string.Join(';', read.Issues));

        var stations = ProcedureStations.For([read.Value!], Scan)!;

        Assert.Equal(["talk", "scan"], stations.Select(s => s.Key));
        Assert.Equal(Scanner, stations[1].Staff!.EquipmentId);
        Assert.Null(ProcedureStations.For([read.Value!], "synthetic:azure_scan"));
        Assert.DoesNotContain("procedureStations", JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options));
    }

    [Fact]
    public void A_mod_replaces_the_whole_station_set()
    {
        var mod = new ContentPackage
        {
            Manifest = new() { Id = "mod:mod", Namespace = "mod", Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = "synthetic:foundation", VersionRange = "[1.0.0,2.0.0)" }] },
            ProcedureStations = [Set(Scan, Station("only", 1m, Place("seat")))]
        };
        var package = With(TalkThenScan());

        Assert.Empty(ContentValidator.Validate(mod, [package]));
        Assert.Equal(["only"], ProcedureStations.For([package, mod], Scan)!.Select(s => s.Key));
        Assert.Equal(["talk", "scan"], ProcedureStations.For([mod, package], Scan)!.Select(s => s.Key));
    }

    [Fact]
    public void Stations_belong_to_examinations_and_treatments_once_per_package()
    {
        Assert.Contains(ContentValidator.Validate(With(Set(Room, Station("a", 1m, Place("seat"))))), i => i.Code == "procedure-station-target");
        Assert.Contains(ContentValidator.Validate(With(TalkThenScan(), TalkThenScan())), i => i.Code == "procedure-station-target");
        Assert.Contains(ContentValidator.Validate(With(Set("synthetic:unknown_scan", Station("a", 1m, Place("seat"))))), i => i.Code == "missing-reference");
    }

    [Theory]
    [InlineData(0.5, 0.4)]
    [InlineData(0.5, 0.6)]
    [InlineData(0, 1)]
    [InlineData(-0.5, 1.5)]
    public void Shares_are_positive_and_sum_to_one(double first, double second)
    {
        var set = Set(Scan, Station("a", (decimal)first, Place("seat")), Station("b", (decimal)second, Place("seat")));

        Assert.Contains(ContentValidator.Validate(With(set)), i => i.Code == "procedure-station-share");
        Assert.Contains(ContentValidator.Validate(With(Set(Scan))), i => i.Code == "procedure-station-share");
        Assert.Empty(ContentValidator.Validate(With(Set(Scan, Station("a", 0.3333m, Place("seat")), Station("b", 0.6667m, Place("seat"))))));
    }

    [Fact]
    public void Keys_are_stable_and_unique()
    {
        Assert.Contains(ContentValidator.Validate(With(Set(Scan, Station("a", .5m, Place("seat")), Station("a", .5m, Place("seat"))))), i => i.Code == "procedure-station-key");
        Assert.Contains(ContentValidator.Validate(With(Set(Scan, Station("Bad Key", 1m, Place("seat"))))), i => i.Code == "procedure-station-key");
    }

    [Fact]
    public void Places_name_a_point_kind_and_medical_equipment()
    {
        Assert.Contains(ContentValidator.Validate(With(Set(Scan, Station("a", 1m, Place(""))))), i => i.Code == "procedure-station-place");
        Assert.Contains(ContentValidator.Validate(With(Set(Scan, Station("a", 1m, Place("seat", Room))))), i => i.Code == "procedure-station-place");
        Assert.Contains(ContentValidator.Validate(With(Set(Scan, Station("a", 1m, Place("seat"), Place(ProcedureStations.Standing, Scanner))))),
            i => i.Code == "procedure-station-place" && i.Path.EndsWith(".staff", StringComparison.Ordinal));
    }

    [Fact]
    public void Unplaceable_lists_places_no_room_of_the_examination_offers()
    {
        var package = With(TalkThenScan());
        package.Relations.Add(Relation("synthetic:scan_room_1", RelationKind.ExaminationRoom, Scan, Room));
        Assert.Empty(ContentValidator.Validate(package));

        var missing = ProcedureStations.Unplaceable([package]);

        Assert.Equal([("talk", "patient"), ("scan", "patient"), ("scan", "staff")], missing.Select(m => (m.StationKey, m.Role)));

        var desk = (FurnitureDefinition)package.Definitions.Single(d => d.Id == Desk);
        package.Definitions[package.Definitions.IndexOf(desk)] = desk with
        {
            Placement = (desk.Placement ?? new() { Footprint = new() { Width = 1, Depth = 1 } }) with
            {
                InteractionPoints = [new() { Key = "seat", Kind = "seat", X = .5m, Y = .5m, Z = .5m }, new() { Key = "lying", Kind = "lying", X = .5m, Y = .8m, Z = .5m }]
            }
        };
        package.Relations.Add(Relation("synthetic:desk_scanner_1", RelationKind.FurnitureEquipment, Desk, Scanner));

        Assert.Equal([("scan", "staff")], ProcedureStations.Unplaceable([package]).Select(m => (m.StationKey, m.Role)));
    }
}
