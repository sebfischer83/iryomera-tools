using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Iryomera.Content.SDK;
using Iryomera.Localization;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class DocumentValidationTests
{
    private static MemoryStream Input(JsonNode? node) => new(Encoding.UTF8.GetBytes(node?.ToJsonString() ?? "null"));

    [Theory]
    [InlineData("package")]
    [InlineData("nestedManifest")]
    [InlineData("mod")]
    [InlineData("localization")]
    [InlineData("annotation")]
    public async Task SchemaVersionMustAppearOnTheWireDespiteConvenientCSharpDefaults(string kind)
    {
        var file = kind is "localization" ? "en" : kind is "mod" ? "mod-manifest" : kind is "annotation" ? "annotations" : "content";
        var node = JsonNode.Parse(await File.ReadAllTextAsync(Fixtures.Example(file)))!;
        var target = kind == "nestedManifest" ? node["manifest"]! : kind == "annotation" ? node["medicalAnnotations"]![0]! : node;
        target.AsObject().Remove("schemaVersion");
        if (kind == "mod")
        {
            Assert.Throws<JsonException>(() => node.Deserialize<ModManifest>(ContentJson.Options));
            await using var stream = Input(node); var result = await ContentDocuments.ReadValidatedAsync<ModManifest>(stream);
            Assert.False(result.IsValid); Assert.Contains(result.Issues, x => x.Code == "schema-required");
        }
        else if (kind == "localization")
        {
            Assert.Throws<JsonException>(() => node.Deserialize<LocalizationDefinition>(ContentJson.Options));
            await using var stream = Input(node); var result = await LocalizationDocuments.ReadValidatedAsync(stream);
            Assert.False(result.IsValid); Assert.Contains(result.Issues, x => x.Code == "schema-required");
        }
        else
        {
            Assert.Throws<JsonException>(() => node.Deserialize<ContentPackage>(ContentJson.Options));
            await using var stream = Input(node); var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(stream);
            Assert.False(result.IsValid); Assert.Null(result.Value); Assert.Contains(result.Issues, x => x.Code == "schema-required");
        }
    }

    [Fact]
    public async Task UnifiedEntryChecksOriginalSchemaBeforeApplyingDefaultsAndThenChecksGraphSemantics()
    {
        var node = JsonNode.Parse(await File.ReadAllTextAsync(Fixtures.Example("content")))!;
        node["relations"]![0]!["toId"] = "synthetic:missing";
        await using (var stream = Input(node))
        {
            var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(stream);
            Assert.False(result.IsValid); Assert.NotNull(result.Value); Assert.Contains(result.Issues, x => x.Code == "missing-reference");
        }
        node["manifest"]!.AsObject().Remove("id");
        await using (var stream = Input(node))
        {
            var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(stream);
            Assert.Null(result.Value); Assert.Contains(result.Issues, x => x.Code == "schema-required");
        }
    }

    [Fact]
    public async Task SharedLocalizationEntryUsesCrossCatalogPlaceholderValidation()
    {
        var node = JsonNode.Parse(await File.ReadAllTextAsync(Fixtures.Example("de")))!;
        node["entries"]!["synthetic.ui.welcome"] = "Hallo {other}";
        await using var stream = Input(node);
        var result = await LocalizationDocuments.ReadValidatedAsync(stream, [Fixtures.Catalog("en")]);
        Assert.False(result.IsValid); Assert.Contains(result.Issues, x => x.Code == "placeholder-mismatch");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":\"1.0.0\",\"schemaVersion\":\"1.0.0\"}")]
    [InlineData("{\"schemaVersion\":null}")]
    [InlineData("{broken")]
    public async Task MalformedJsonAndWrongShapesReturnUsefulDiagnostics(string json)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(stream);
        Assert.False(result.IsValid); Assert.Null(result.Value); Assert.NotEmpty(result.Issues);
        Assert.All(result.Issues, issue => { Assert.NotEmpty(issue.Path); Assert.NotEmpty(issue.Message); });
    }

    [Fact]
    public async Task ValidExamplesLoadThroughSharedValidationAndKeepExtensions()
    {
        foreach (var file in new[] { "content", "annotations" })
        {
            var node = JsonNode.Parse(await File.ReadAllTextAsync(Fixtures.Example(file)))!; node["future_field"] = "preserved";
            await using var stream = Input(node); var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(stream);
            Assert.True(result.IsValid, string.Join("; ", result.Issues)); Assert.Equal("preserved", result.Value!.Extensions!["future_field"].GetString());
        }
    }

    [Fact]
    public async Task SharedEntryHonorsCancellation()
    {
        using var source = new CancellationTokenSource(); source.Cancel(); await using var stream = Input(new JsonObject());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContentDocuments.ReadValidatedAsync<ContentPackage>(stream, cancellationToken: source.Token));
    }

    [Fact]
    public void CSharpDefaultsRemainCompatibleWhileWireFieldsAreMandatory()
    {
        Assert.Equal(ContentFormat.SchemaVersion, Fixtures.Package().SchemaVersion);
        Assert.Equal(ContentFormat.SchemaVersion, new LocalizationDefinition { Namespace = "demo", Language = "en" }.SchemaVersion);
    }
    [Fact]
    public async Task ConcreteDefinitionsKeepTheirDiscriminatorOutsideExtensionData()
    {
        var node = JsonSerializer.SerializeToNode<ContentDefinition>(Fixtures.Package().Definitions[0], ContentJson.Options)!;
        await using var input = Input(node);
        var result = await ContentDocuments.ReadValidatedAsync<DiseaseDefinition>(input);
        Assert.True(result.IsValid); Assert.False(result.Value!.Extensions?.ContainsKey("kind") ?? false);
        await using var output = new MemoryStream(); await ContentJson.WriteAsync(output, result.Value); output.Position = 0;
        Assert.Equal(result.Value.Id, (await ContentJson.ReadAsync<DiseaseDefinition>(output)).Id);
    }

    [Fact]
    public async Task RawContentJsonReaderAlsoRejectsMissingVersion()
    {
        var node = JsonNode.Parse(await File.ReadAllTextAsync(Fixtures.Example("content")))!;
        node.AsObject().Remove("schemaVersion"); await using var input = Input(node);
        await Assert.ThrowsAsync<JsonException>(() => ContentJson.ReadAsync<ContentPackage>(input));
    }

}
