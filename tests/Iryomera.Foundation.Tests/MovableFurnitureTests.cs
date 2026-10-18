using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class MovableFurnitureTests
{
    private static InteractionPoint Point(string key, string? kind, decimal heading = 0) => new() { Key = key, Kind = kind, X = 0.5m, Y = 0, Z = 0.5m, HeadingDegrees = heading };

    private static (ContentPackage Package, FurnitureDefinition Furniture) Bed(FurnitureMobility? mobility, FurnitureTransport? transport, params InteractionPoint[] points)
    {
        var package = Fixtures.Package(); var old = package.Definitions.OfType<FurnitureDefinition>().First();
        var bed = old with { Mobility = mobility, Transport = transport, Placement = new() { Footprint = new() { Width = 1, Depth = 2 }, InteractionPoints = [.. points] } };
        package.Definitions[package.Definitions.IndexOf(old)] = bed; return (package, bed);
    }

    [Fact]
    public async Task TransportFurnitureWithSeveralPointsRoundTripsAndMatchesSchemas()
    {
        var (package, bed) = Bed(FurnitureMobility.Pushable, new() { PatientCapacity = 1, Posture = TransportPosture.Lying, RequiredQualificationId = "synthetic:demo_qualification" },
            Point("lying", InteractionPointKinds.Lying), Point("handle_left", InteractionPointKinds.Handle, 90), Point("handle_right", InteractionPointKinds.Handle, 270),
            Point("operator", InteractionPointKinds.Operator), Point("entry", InteractionPointKinds.Entry, 180));
        Assert.Empty(ContentValidator.Validate(package));
        Assert.True(Fixtures.Evaluate("furnitureDefinition", JsonSerializer.SerializeToNode<ContentDefinition>(bed, ContentJson.Options)).IsValid);
        Assert.True(Fixtures.Evaluate("furnitureTransport", JsonSerializer.SerializeToNode(bed.Transport, ContentJson.Options)).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        var reloaded = result.Value!.Definitions.OfType<FurnitureDefinition>().Single(d => d.Id == bed.Id);
        Assert.Equal((FurnitureMobility.Pushable, TransportPosture.Lying), (reloaded.Mobility!.Value, reloaded.Transport!.Posture));
        Assert.Equal(2, reloaded.Placement!.InteractionPoints.Count(p => p.Kind == InteractionPointKinds.Handle));
        var legacy = JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options);
        Assert.DoesNotContain("\"mobility\"", legacy); Assert.DoesNotContain("\"transport\"", legacy); Assert.DoesNotContain("\"kind\": \"seat\"", legacy);
    }

    [Fact]
    public void BenchesWithSeveralSeatsSatisfyHigherCapacity()
    {
        var (package, _) = Bed(FurnitureMobility.Movable, new() { PatientCapacity = 2, Posture = TransportPosture.Seated }, Point("seat_1", "seat"), Point("seat_2", "seat"));
        Assert.Empty(ContentValidator.Validate(package));
        Assert.Contains(ContentValidator.Validate(Bed(FurnitureMobility.Movable, new() { PatientCapacity = 3, Posture = TransportPosture.Seated }, Point("seat_1", "seat"), Point("seat_2", "seat")).Package),
            i => i.Code == "furniture-transport");
    }

    [Theory]
    [InlineData("pushable-without-handle", "furniture-transport")]
    [InlineData("transport-fixed", "furniture-transport")]
    [InlineData("transport-without-mobility", "furniture-transport")]
    [InlineData("missing-lying", "furniture-transport")]
    [InlineData("capacity", "furniture-transport")]
    [InlineData("qualification", "interaction-qualification")]
    [InlineData("kind", "interaction-point")]
    public void InconsistentMovableFurnitureIsRejected(string change, string code)
    {
        var handle = Point("handle", "handle"); var lying = Point("lying", "lying");
        var lyingTransport = new FurnitureTransport { PatientCapacity = 1, Posture = TransportPosture.Lying };
        var (package, _) = change switch
        {
            "pushable-without-handle" => Bed(FurnitureMobility.Pushable, null, lying),
            "transport-fixed" => Bed(FurnitureMobility.Fixed, lyingTransport, lying),
            "transport-without-mobility" => Bed(null, lyingTransport, lying),
            "missing-lying" => Bed(FurnitureMobility.Pushable, lyingTransport, handle, Point("seat", "seat")),
            "capacity" => Bed(FurnitureMobility.Pushable, lyingTransport with { PatientCapacity = 0 }, handle, lying),
            "qualification" => Bed(FurnitureMobility.Pushable, lyingTransport with { RequiredQualificationId = "synthetic:amber_syndrome" }, handle, lying),
            _ => Bed(null, null, Point("seat", "Seat Kind"))
        };
        Assert.Contains(ContentValidator.Validate(package), i => i.Code == code);
    }

    [Fact]
    public void FixedOperatorPointsNeedNoTransport()
    {
        var (package, _) = Bed(null, null, Point("operator", InteractionPointKinds.Operator), Point("custom", "mod_specific_kind"));
        Assert.Empty(ContentValidator.Validate(package));
    }
}
