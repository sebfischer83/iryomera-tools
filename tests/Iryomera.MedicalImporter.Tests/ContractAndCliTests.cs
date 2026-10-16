using System.Text.Json;
using System.Text.Json.Nodes;
using Iryomera.Content.SDK;
using Json.Schema;
using Xunit;

namespace Iryomera.MedicalImporter.Tests;

public sealed class ContractAndCliTests
{
    private static bool Conforms<T>(string name, T value)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "schemas");
        var options = new EvaluationOptions { RequireFormatValidation = true };
        options.SchemaRegistry.Register(JsonSchema.FromFile(Path.Combine(directory, "contracts.schema.json")));
        return JsonSchema.FromFile(Path.Combine(directory, name + ".schema.json")).Evaluate(JsonSerializer.SerializeToNode(value, ContentJson.Options), options).IsValid;
    }

    [Fact]
    public async Task PublicStagingAndReleaseContractsMatchVersionedSchemas()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace();
        Assert.True(Conforms("importWorkspace", workspace)); TestData.Approve(workspace); Assert.True(Conforms("importWorkspace", workspace));
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        Assert.True(Conforms("sourceAssertionDocument", await TestData.Read<SourceAssertionDocument>(data.PathFor("out/source-assertions.json"))));
        Assert.True(Conforms("releaseReport", await TestData.Read<ReleaseReport>(data.PathFor("out/report.json"))));
    }

    [Fact]
    public async Task UnknownStagingFieldsAndNullRequiredCollectionsAreRejected()
    {
        var workspace = await TestData.CompleteWorkspace();
        var node = JsonSerializer.SerializeToNode(workspace, ContentJson.Options)!;
        node["futureUnsupportedField"] = 1;
        Assert.Throws<JsonException>(() => node.Deserialize<ImportWorkspace>(ContentJson.Options));
        node.AsObject().Remove("futureUnsupportedField"); node["batches"] = null;
        Assert.Throws<InvalidDataException>(() => WorkspaceValidation.Validate(node.Deserialize<ImportWorkspace>(ContentJson.Options)!));
    }

    [Fact]
    public async Task CliPerformsImportValidationExplicitReviewRightsAndExport()
    {
        using var data = new TestData(); var workspacePath = data.PathFor("workspace with spaces.json");
        async Task<int> Run(params string[] args) => await Program.CreateCommand().Parse(args).InvokeAsync();
        Assert.Equal(0, await Run("sources", "list"));
        Assert.Equal(0, await Run("import", "hpo", "--input", TestData.Fixture("hpo.json"), "--source-version", "synthetic-1", "--source-url", "https://example.org/synthetic/hpo", "--license", "MIT", "--license-file", TestData.Fixture("LICENSE.txt"), "--attribution", "Own synthetic test fixtures", "--imported-at", "2026-01-01T00:00:00Z", "--workspace", workspacePath));
        Assert.Equal(0, await Run("validate", "--workspace", workspacePath));
        Assert.Equal(0, await Run("status", "--workspace", workspacePath));
        var batch = Assert.Single((await new WorkspaceStore(workspacePath).ReadAsync()).Batches);
        Assert.Equal(1, await Run("export", "--workspace", workspacePath, "--output", data.PathFor("out")));
        Assert.Equal(0, await Run("review", "--workspace", workspacePath, "--batch", batch.Id, "--all", "--status", "approved", "--reviewer", "Synthetic test reviewer", "--at", "2026-01-02T00:00:00Z", "--note", "Synthetic fixture approval only"));
        Assert.Equal(1, await Run("export", "--workspace", workspacePath, "--output", data.PathFor("out")));
        Assert.Equal(0, await Run("rights", "--workspace", workspacePath, "--batch", batch.Id, "--decision", "permitted", "--reviewer", "Synthetic rights reviewer", "--at", "2026-01-02T00:00:00Z", "--note", "Own MIT fixtures only"));
        Assert.Equal(0, await Run("export", "--workspace", workspacePath, "--output", data.PathFor("out"), "--require-languages", "en"));
        Assert.True(File.Exists(data.PathFor("out/content.json")));
    }

    [Fact]
    public async Task CliMissingOptionsAndUnsupportedSourcesReturnFailure()
    {
        Assert.NotEqual(0, await Program.CreateCommand().Parse(["import", "hpo"]).InvokeAsync());
        using var data = new TestData();
        Assert.Equal(1, await Program.CreateCommand().Parse(["validate", "--workspace", data.PathFor("missing.json")]).InvokeAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => new ImportEngine().ReadAsync(TestData.Fixture("hpo.json"), TestData.Options("unknown")));
    }

    [Fact]
    public async Task IdenticalCrossSourceDiseaseClaimsKeepBothApprovedProvenances()
    {
        using var data = new TestData(); var workspace = new ImportWorkspace();
        var one = await TestData.Import("hpo-annotations", "hpoa.tsv");
        var two = await new ImportEngine().ReadAsync(TestData.Fixture("hpoa.tsv"), TestData.Options("hpo-annotations") with { SourceUrl = "https://example.org/independent-synthetic-claim" });
        // Keep only the repeated disease claims; no fabricated second association.
        one.Assertions.Clear(); two.Assertions.Clear();
        workspace.Batches.Add(one); workspace.Batches.Add(two);
        var diagnostics = WorkspaceValidation.Validate(workspace);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(diagnostics, d => d.Code == "duplicate-source-claim");
        TestData.Approve(workspace); await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        var definition = Assert.Single((await TestData.Read<ContentPackage>(data.PathFor("out/content.json"))).Definitions);
        Assert.Equal(2, definition.Classifications.Count);
        Assert.Equal(2, definition.Extensions!["sourceClaims"].GetArrayLength());
    }
    [Fact]
    public async Task ActualExporterOutputConformsToPublicSdkAndLocalizationSchemas()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace(); TestData.Approve(workspace);
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        var directory = Path.Combine(AppContext.BaseDirectory, "sdk-schemas");
        var options = new EvaluationOptions { RequireFormatValidation = true };
        options.SchemaRegistry.Register(JsonSchema.FromFile(Path.Combine(directory, "contracts.schema.json")));
        var packageSchema = JsonSchema.FromFile(Path.Combine(directory, "contentPackage.schema.json"));
        Assert.True(packageSchema.Evaluate(JsonNode.Parse(await File.ReadAllTextAsync(data.PathFor("out/content.json"))), options).IsValid);
        var catalogSchema = JsonSchema.FromFile(Path.Combine(directory, "localizationDefinition.schema.json"));
        Assert.True(catalogSchema.Evaluate(JsonNode.Parse(await File.ReadAllTextAsync(data.PathFor("out/localization/en.json"))), options).IsValid);
    }

}
