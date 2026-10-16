using System.IO.Compression;
using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.ContentStudio.Services;
using Json.Schema;
using Xunit;

namespace Iryomera.ContentStudio.Tests;

public sealed class DiagnosticStudioTests
{
    [Fact]
    public async Task DiagnosticDraftsPreserveContractsAndExportOnlyApprovedMedicalMetadata()
    {
        await using var data = await StudioTestData.CreateAsync();
        var root = FindRoot(); await using var content = File.OpenRead(Path.Combine(root, "examples/synthetic/diagnostics.json"));
        var id = await data.Projects.ImportPackageAsync("Fictional diagnostic prototype", content, "Synthetic");
        foreach (var language in new[] { "en", "de" })
        {
            var state = await data.Projects.GetAsync(id); await using var catalog = File.OpenRead(Path.Combine(root, "examples/synthetic/diagnostics." + language + ".json"));
            await data.Projects.ImportCatalogAsync(id, state.Project.Revision, catalog, "Synthetic", "Own fixture language");
        }
        await data.Approve(id);
        var exported = await StudioTestData.PackageFromZip(await data.Exports.ExportAsync(id));
        Assert.Single(exported.ReferenceRanges); Assert.Single(exported.DiagnosticRecommendations); Assert.Equal(15, exported.DiagnosticSimulation!.ObservationDistributions.Count);
        var before = await data.Projects.GetAsync(id); var observation = before.Package.Definitions.OfType<ObservationDefinition>().Single(o => o.Result!.ValueKind == ObservationValueKind.Numeric);
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveDiagnosticJsonAsync(id, before.Project.Revision, "result", StudioContent.Json(observation.Result! with { UnitCode = "different" }), "Synthetic", "Invalid unit test", observation.Id));
        Assert.Equal(before.Project.Revision, (await data.Projects.GetAsync(id)).Project.Revision);
        await using var changedCatalog = StudioTestData.Json(before.Catalogs.Single(c => c.Language == "de"));
        await data.Projects.ImportCatalogAsync(id, before.Project.Revision, changedCatalog, "Synthetic", "Recheck clinical translations");
        var after = await data.Projects.GetAsync(id);
        Assert.All(after.Package.ReferenceRanges, r => Assert.Equal(ReviewStatus.Unreviewed, r.Provenance.ReviewStatus));
        Assert.All(after.Package.DiagnosticRecommendations, r => Assert.Equal(ReviewStatus.Unreviewed, r.Provenance.ReviewStatus));
    }
    [Fact]
    public async Task ActualReleaseReviewReportMatchesItsVersionedSchema()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); await data.Approve(id);
        using var zip = new ZipArchive(new MemoryStream(await data.Exports.ExportAsync(id))); await using var stream = zip.GetEntry("review.json")!.Open();
        using var document = await JsonDocument.ParseAsync(stream); var folder = Path.Combine(FindRoot(), "schemas/studio/v1");
        var options = new EvaluationOptions { RequireFormatValidation = true }; options.SchemaRegistry.Register(JsonSchema.FromFile(Path.Combine(folder, "contracts.schema.json")));
        Assert.True(JsonSchema.FromFile(Path.Combine(folder, "studioReleaseReport.schema.json")).Evaluate(document.RootElement, options).IsValid);
        Assert.Equal("1.0.0", document.RootElement.GetProperty("schemaVersion").GetString());
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Iryomera.Tools.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root missing for own fixtures.");
    }
}
