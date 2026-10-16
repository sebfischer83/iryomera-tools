using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class ContentTests
{
    [Fact]
    public async Task SyntheticExampleIsCompleteConsistentAndRoundTrips()
    {
        var package = await Fixtures.Load<ContentPackage>("content");
        Assert.Equal(22, package.Definitions.Count);
        Assert.Empty(ContentValidator.Validate(package));
        Assert.Equal(12, package.Definitions.Select(x => x.GetType()).Distinct().Count());
        Assert.Equal(8, package.Relations.Select(x => x.Kind).Distinct().Count());
        Assert.All(package.Definitions, x => Assert.Equal(ReviewStatus.Unreviewed, x.Provenance.ReviewStatus));
        await using var stream = new MemoryStream();
        await ContentJson.WriteAsync(stream, package);
        stream.Position = 0;
        var reloaded = await ContentJson.ReadAsync<ContentPackage>(stream);
        Assert.Equal(JsonSerializer.Serialize(package, ContentJson.Options), JsonSerializer.Serialize(reloaded, ContentJson.Options));
        Assert.Empty(ContentValidator.Validate(reloaded));
    }

    [Fact]
    public async Task ConcreteDefinitionUsesTheSameDiscriminatedWireContract()
    {
        var disease = (DiseaseDefinition)Fixtures.Package().Definitions[0];
        await using var stream = new MemoryStream();
        await ContentJson.WriteAsync(stream, disease);
        stream.Position = 0;
        var reloaded = await ContentJson.ReadAsync<DiseaseDefinition>(stream);
        Assert.Equal(disease.Id, reloaded.Id);
        stream.Position = 0;
        await Assert.ThrowsAsync<JsonException>(() => ContentJson.ReadAsync<SymptomDefinition>(stream));
    }

    [Theory]
    [InlineData("{\"kind\":\"unknown\"}")]
    [InlineData("{\"kind\":1}")]
    public void InvalidDiscriminatorsFailExplicitly(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContentDefinition>(json, ContentJson.Options));

    [Theory]
    [InlineData("acute_appendicitis")]
    [InlineData("Base:appendicitis")]
    [InlineData("base:")]
    [InlineData(":disease")]
    [InlineData("base:a/b")]
    [InlineData("base:a:b")]
    [InlineData("base:ä")]
    [InlineData("base:a\n")]
    public void InvalidIdsAreRejected(string id)
    {
        var p = Fixtures.Package();
        p.Definitions[0] = p.Definitions[0] with { Id = id };
        Assert.Contains(ContentValidator.Validate(p), x => x.Code == "id");
    }

    [Theory]
    [InlineData("base:acute_appendicitis")]
    [InlineData("author.mod:room.large")]
    public void StableNamespacedIdsAreAccepted(string id) => Assert.True(ContentFormat.IsValidId(id));

    [Fact]
    public void DuplicateIdsAcrossDefinitionsAndRelationsAreRejected()
    {
        var p = Fixtures.Package();
        p.Relations[0] = p.Relations[0] with { Id = p.Definitions[0].Id };
        Assert.Contains(ContentValidator.Validate(p), x => x.Code == "duplicate-id");
        p = Fixtures.Package();
        p.Definitions.Add(p.Definitions[0]);
        Assert.Contains(ContentValidator.Validate(p), x => x.Code == "duplicate-id");
    }

    [Fact]
    public void MissingDefinitionsAreDetected()
    {
        var p = Fixtures.Package();
        p.Definitions.RemoveAll(x => x.Id == p.Relations[0].ToId);
        Assert.Contains(ContentValidator.Validate(p), x => x.Code == "missing-reference");
    }

    [Theory]
    [InlineData(RelationKind.DiseaseSymptom)]
    [InlineData(RelationKind.DiseaseFinding)]
    [InlineData(RelationKind.ExaminationFinding)]
    [InlineData(RelationKind.DiseaseExamination)]
    [InlineData(RelationKind.DiseaseTreatment)]
    [InlineData(RelationKind.ExaminationEquipment)]
    [InlineData(RelationKind.TreatmentQualification)]
    [InlineData(RelationKind.RoomFurniture)]
    public void EachRelationRejectsWrongEndpointTypes(RelationKind kind)
    {
        var p = Fixtures.Package();
        var index = p.Relations.FindIndex(x => x.Kind == kind);
        p.Relations[index] = p.Relations[index] with { FromId = "synthetic:demo_staff", ToId = "synthetic:demo_staff" };
        Assert.Contains(ContentValidator.Validate(p), x => x.Code == "relation-type");
    }

    [Fact]
    public void UnknownRelationKindsAndReviewValuesAreDetected()
    {
        var p = Fixtures.Package();
        p.Relations[0] = p.Relations[0] with { Kind = (RelationKind)999, Provenance = p.Relations[0].Provenance with { ReviewStatus = (ReviewStatus)999 } };
        var issues = ContentValidator.Validate(p);
        Assert.Contains(issues, x => x.Code == "relation-kind");
        Assert.Contains(issues, x => x.Code == "review-status");
    }

    [Fact]
    public void OwnershipVersionsAndApprovalAuditAreValidated()
    {
        var p = Fixtures.Package();
        p.Definitions[0] = p.Definitions[0] with
        {
            Id = "other:example", Version = "1",
            Provenance = p.Definitions[0].Provenance with { License = "", ReviewStatus = ReviewStatus.Approved }
        };
        var issues = ContentValidator.Validate(p);
        foreach (var code in new[] { "namespace-owner", "version", "provenance", "review-audit" }) Assert.Contains(issues, x => x.Code == code);
    }

    [Fact]
    public void GameplayIsSeparateAndInvalidRangesAreDetected()
    {
        var p = Fixtures.Package();
        p.Gameplay[0] = p.Gameplay[0] with { SimulatedSuccessProbability = 1.01m, TreatmentCost = -1 };
        Assert.Contains(ContentValidator.Validate(p), x => x.Code == "balance-range");
        p.Gameplay.Add(p.Gameplay[0]);
        Assert.Contains(ContentValidator.Validate(p), x => x.Code == "duplicate-balance");
        Assert.DoesNotContain("simulatedSuccessProbability", JsonSerializer.Serialize<ContentDefinition>(p.Definitions[0], ContentJson.Options));
    }

    [Fact]
    public void UnknownAdditiveFieldsSurviveNestedRoundTrip()
    {
        var node = JsonNode.Parse(File.ReadAllText(Fixtures.Example("content")))!;
        node["futurePackageField"] = 42;
        node["definitions"]![0]!["futureDefinitionField"] = "keep";
        node["definitions"]![0]!["provenance"]!["futureSourceField"] = true;
        var p = node.Deserialize<ContentPackage>(ContentJson.Options)!;
        var output = JsonSerializer.SerializeToNode(p, ContentJson.Options)!;
        Assert.Equal(42, output["futurePackageField"]!.GetValue<int>());
        Assert.Equal("keep", output["definitions"]![0]!["futureDefinitionField"]!.GetValue<string>());
        Assert.True(output["definitions"]![0]!["provenance"]!["futureSourceField"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"1.0.0\",\"manifest\":null}")]
    [InlineData("{\"manifest\":{\"id\":\"a:b\",\"namespace\":\"a\",\"version\":\"1.0.0\",\"license\":\"MIT\"},\"definitions\":[null]}")]
    public void JsonNullsProduceDiagnosticsInsteadOfCrashes(string json)
    {
        var p = JsonSerializer.Deserialize<ContentPackage>(json, ContentJson.Options)!;
        Assert.Contains(ContentValidator.Validate(p), x => x.Code == "required");
    }

    [Theory]
    [InlineData("{\"manifest\":{}}")]
    [InlineData("{\"manifest\":{},\"manifest\":{}}")]
    [InlineData("{\"schemaVersion\":1}")]
    public void MalformedContractsThrowJsonException(string json) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContentPackage>(json, ContentJson.Options));

    [Fact]
    public void UnsupportedSchemaIsRejected()
    {
        var p = Fixtures.Package() with { SchemaVersion = "2.0.0" };
        Assert.Contains(ContentValidator.Validate(p), x => x.Code == "schema-version");
    }

    [Fact]
    public async Task AsyncIoHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContentJson.ReadAsync<ContentPackage>(stream, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContentJson.WriteAsync(stream, Fixtures.Package(), cancellation.Token));
    }
}
