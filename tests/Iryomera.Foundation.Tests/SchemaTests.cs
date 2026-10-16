using System.Text.Json;
using System.Text.Json.Nodes;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class SchemaTests
{
    [Theory]
    [InlineData("contentPackage", "content")]
    [InlineData("localizationDefinition", "en")]
    [InlineData("localizationDefinition", "de")]
    [InlineData("modManifest", "mod-manifest")]
    public void ExamplesConformToPublishedSchemas(string contract, string example) =>
        Assert.True(Fixtures.Evaluate(contract, JsonNode.Parse(File.ReadAllText(Fixtures.Example(example)))).IsValid);

    [Fact]
    public async Task EveryDefinitionSerializesAgainstItsOwnSchema()
    {
        foreach (var definition in Fixtures.Package().Definitions)
        {
            await using var stream = new MemoryStream();
            await ContentJson.WriteAsync(stream, definition);
            stream.Position = 0;
            var node = await JsonNode.ParseAsync(stream);
            Assert.True(Fixtures.Evaluate(node!["kind"]!.GetValue<string>() + "Definition", node).IsValid);
            var reloaded = node.Deserialize<ContentDefinition>(ContentJson.Options)!;
            Assert.Equal(definition.GetType(), reloaded.GetType());
        }
    }

    [Fact]
    public void EveryNestedContractConforms()
    {
        var package = Fixtures.Package();
        Assert.True(Fixtures.Evaluate("contentPackageManifest", JsonSerializer.SerializeToNode(package.Manifest, ContentJson.Options)).IsValid);
        foreach (var relation in package.Relations)
            Assert.True(Fixtures.Evaluate("contentRelation", JsonSerializer.SerializeToNode(relation, ContentJson.Options)).IsValid);
        Assert.True(Fixtures.Evaluate("gameplayBalance", JsonSerializer.SerializeToNode(package.Gameplay[0], ContentJson.Options)).IsValid);
        Assert.True(Fixtures.Evaluate("provenance", JsonSerializer.SerializeToNode(package.Definitions[0].Provenance, ContentJson.Options)).IsValid);
        Assert.True(Fixtures.Evaluate("contentDependency", JsonNode.Parse("{\"packageId\":\"base:content\",\"versionRange\":\"[1.0.0,2.0.0)\"}")).IsValid);
        Assert.True(Fixtures.Evaluate("evidenceReference", JsonNode.Parse("{\"citation\":\"Synthetic citation\",\"uri\":\"https://example.org/test\"}")).IsValid);
        var classification = new ExternalIdentifier { System = "Synthetic", Value = "TEST-001", Provenance = package.Definitions[0].Provenance };
        Assert.True(Fixtures.Evaluate("externalIdentifier", JsonSerializer.SerializeToNode(classification, ContentJson.Options)).IsValid);
    }

    [Theory]
    [InlineData("id", "invalid")]
    [InlineData("id", "synthetic:test\n")]
    [InlineData("kind", "unknown")]
    [InlineData("version", "1")]
    [InlineData("nameKey", "bad key")]
    public void SchemasRejectMalformedDefinitions(string property, string value)
    {
        var node = JsonSerializer.SerializeToNode<ContentDefinition>(Fixtures.Package().Definitions[0], ContentJson.Options)!;
        node[property] = value;
        Assert.False(Fixtures.Evaluate("contentDefinition", node).IsValid);
    }

    [Fact]
    public void SchemasEnforceProvenanceAuditAndLocalizationShape()
    {
        var p = JsonSerializer.SerializeToNode(Fixtures.Package().Definitions[0].Provenance, ContentJson.Options)!;
        p["reviewStatus"] = "approved";
        Assert.False(Fixtures.Evaluate("provenance", p).IsValid);
        p["reviewStatus"] = "unreviewed";
        p["importedAt"] = "yesterday";
        Assert.False(Fixtures.Evaluate("provenance", p).IsValid);
        var catalog = JsonNode.Parse(File.ReadAllText(Fixtures.Example("de")))!;
        catalog["entries"]!["synthetic.ui.welcome"] = null;
        Assert.False(Fixtures.Evaluate("localizationDefinition", catalog).IsValid);
    }
}
