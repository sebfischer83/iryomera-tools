using System.Text.Json;
using System.Text.Json.Nodes;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class MedicalAnnotationTests
{
    [Fact]
    public async Task CompleteHpoAndMaxoSourceContextRoundTripsWithoutProjectingBinaryRelations()
    {
        var package = await Fixtures.Load<ContentPackage>("annotations");
        Assert.Empty(ContentValidator.Validate(package));
        Assert.Empty(package.Relations);
        var hpo = package.MedicalAnnotations.Single(a => a.Kind == MedicalAnnotationKind.DiseasePhenotype);
        Assert.True(hpo.IsNegated); Assert.Equal("0/5", hpo.Frequency!.SourceValue);
        Assert.Equal("NOT", Assert.Single(hpo.Qualifiers)); Assert.NotNull(hpo.Onset); Assert.Single(hpo.Modifiers);
        Assert.Equal(12, hpo.SourceFields.Count);
        var maxo = package.MedicalAnnotations.Single(a => a.Kind == MedicalAnnotationKind.MedicalAction);
        Assert.Equal("TREATS", maxo.Predicate); Assert.NotNull(maxo.DiseaseContext); Assert.NotNull(maxo.ExtensionContext);
        Assert.Null(maxo.ExtensionContext!.ContentId); Assert.Null(maxo.IsNegated); Assert.Equal(15, maxo.SourceFields.Count);
        await using var stream = new MemoryStream(); await ContentJson.WriteAsync(stream, package); stream.Position = 0;
        var copy = await ContentJson.ReadAsync<ContentPackage>(stream);
        Assert.Equal(JsonSerializer.Serialize(package, ContentJson.Options), JsonSerializer.Serialize(copy, ContentJson.Options));
        Assert.Empty(copy.Relations);
        Assert.Equal(12, copy.MedicalAnnotations[0].SourceFields.Count);
    }

    [Theory]
    [InlineData("7/13")]
    [InlineData("17%")]
    [InlineData("HP:SYNTHETIC_FREQUENCY")]
    [InlineData("unrecognized source expression")]
    public async Task ObservationalFrequencyRemainsTheExactSourceValue(string value)
    {
        var package = await Fixtures.Load<ContentPackage>("annotations");
        package.MedicalAnnotations[0] = package.MedicalAnnotations[0] with { Frequency = new() { SourceValue = value } };
        Assert.Empty(ContentValidator.Validate(package));
        var node = JsonSerializer.SerializeToNode(package, ContentJson.Options)!;
        Assert.True(Fixtures.Evaluate("contentPackage", node).IsValid);
        Assert.Equal(value, node["medicalAnnotations"]![0]!["frequency"]!["sourceValue"]!.GetValue<string>());
        Assert.Empty(package.Gameplay); Assert.Empty(package.Relations);
    }

    [Fact]
    public async Task NullFalseAndTrueNegationAreDistinctAndNeverInferredFromFrequency()
    {
        var package = await Fixtures.Load<ContentPackage>("annotations");
        foreach (var negated in new bool?[] { null, false, true })
        {
            var annotation = package.MedicalAnnotations[0] with { IsNegated = negated };
            var copy = JsonSerializer.Deserialize<MedicalAnnotation>(JsonSerializer.Serialize(annotation, ContentJson.Options), ContentJson.Options)!;
            Assert.Equal(negated, copy.IsNegated); Assert.Equal("0/5", copy.Frequency!.SourceValue);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("type")]
    [InlineData("unresolved")]
    [InlineData("duplicate")]
    public async Task InvalidAnnotationReferencesAndIdsAreDiagnosed(string scenario)
    {
        var package = await Fixtures.Load<ContentPackage>("annotations"); var a = package.MedicalAnnotations[0];
        package.MedicalAnnotations[0] = scenario switch
        {
            "missing" => a with { Object = a.Object with { ContentId = "annotation_demo:missing" } },
            "type" => a with { Subject = a.Subject with { ContentId = "annotation_demo:action" } },
            "unresolved" => a with { Subject = a.Subject with { ContentId = null } },
            _ => a with { Id = package.Definitions[0].Id }
        };
        var code = scenario switch { "missing" => "missing-reference", "type" => "annotation-type", "unresolved" => "unresolved-annotation-reference", _ => "duplicate-id" };
        Assert.Contains(ContentValidator.Validate(package), issue => issue.Code == code);
    }

    [Fact]
    public async Task ContextReferencesRespectExplicitPackageDependencies()
    {
        var package = await Fixtures.Load<ContentPackage>("annotations"); var other = Fixtures.Package();
        var annotation = package.MedicalAnnotations[1];
        package.MedicalAnnotations[1] = annotation with { DiseaseContext = new() { ExternalId = "SYNTHETIC:other", ContentId = other.Definitions[0].Id } };
        Assert.Contains(ContentValidator.Validate(package, [other]), x => x.Code == "undeclared-dependency");
        package.Manifest.Dependencies.Add(new() { PackageId = other.Manifest.Id, VersionRange = "[1.0.0]" });
        Assert.Empty(ContentValidator.Validate(package, [other]));
    }

    [Fact]
    public async Task SchemasCoverStandaloneAnnotationsAndPreserveUnknownNestedSourceMetadata()
    {
        var package = await Fixtures.Load<ContentPackage>("annotations");
        foreach (var annotation in package.MedicalAnnotations)
            Assert.True(Fixtures.Evaluate("medicalAnnotation", JsonSerializer.SerializeToNode(annotation, ContentJson.Options)).IsValid);
        var node = JsonSerializer.SerializeToNode(package.MedicalAnnotations[0], ContentJson.Options)!;
        node["sourceFields"]!["future_source_field"] = new JsonObject { ["nested"] = new JsonArray(1, 2, 3) };
        node["frequency"]!["future_frequency_metadata"] = "preserve";
        var result = node.Deserialize<MedicalAnnotation>(ContentJson.Options)!;
        Assert.Equal(3, result.SourceFields["future_source_field"].GetProperty("nested").GetArrayLength());
        Assert.Equal("preserve", result.Frequency!.Extensions!["future_frequency_metadata"].GetString());
        Assert.Equal(ReviewStatus.Unreviewed, result.Provenance.ReviewStatus);
    }

    [Fact]
    public async Task ExistingVersionOneExamplesRemainValidWithoutAnAnnotationCollection()
    {
        var package = await Fixtures.Load<ContentPackage>("content");
        Assert.Empty(package.MedicalAnnotations); Assert.Empty(ContentValidator.Validate(package));
        Assert.True(Fixtures.Evaluate("contentPackage", JsonNode.Parse(await File.ReadAllTextAsync(Fixtures.Example("content")))).IsValid);
    }
    [Fact]
    public async Task AnnotationExampleHasCompleteGermanAndEnglishCatalogs()
    {
        var package = await Fixtures.Load<ContentPackage>("annotations");
        var english = await Fixtures.Load<LocalizationDefinition>("annotations.en");
        var german = await Fixtures.Load<LocalizationDefinition>("annotations.de");
        Assert.Empty(Iryomera.Localization.LocalizationValidator.Validate([english, german], Iryomera.Localization.LocalizationValidator.GetContentKeys(package)));
    }

}
