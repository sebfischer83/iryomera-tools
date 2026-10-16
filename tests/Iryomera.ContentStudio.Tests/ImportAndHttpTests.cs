using System.Net;
using Iryomera.Content.SDK;
using Iryomera.ContentStudio.Services;
using Iryomera.MedicalImporter;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Iryomera.ContentStudio.Tests;

public sealed class ImportAndHttpTests
{
    private static async Task<ImportWorkspace> FixtureWorkspace()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hpo.json");
        var options = new ImportOptions { Source = "hpo", SourceVersion = "synthetic-1", SourceUrl = "https://example.org/synthetic", License = "MIT", LicenseText = "Own synthetic test file", Attribution = "Own synthetic test only", ImportedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), Namespace = "synthetic_import" };
        return new() { Batches = [await new ImportEngine().ReadAsync(fixture, options)] };
    }
    [Fact]
    public async Task SourceSnapshotIsImmutableAndReviewsRightsPromotionAndComparisonActuallyWork()
    {
        await using var data = await StudioTestData.CreateAsync(); var workspace = await FixtureWorkspace(); await using var input = StudioTestData.Json(workspace);
        var id = await data.Imports.UploadAsync("synthetic.json", input, "Synthetic"); var state = await data.Imports.GetAsync(id); var original = state.Import.OriginalJson; var batch = workspace.Batches[0];
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Imports.PromoteAsync(id, "Not approved", "Synthetic"));
        await data.Imports.ReviewAsync(id, state.Import.Revision, batch.Id, null, ReviewStatus.Approved, "Synthetic", "Own fixture only", true);
        state = await data.Imports.GetAsync(id);
        await data.Imports.RightsAsync(id, state.Import.Revision, batch.Id, null, RedistributionDecision.Permitted, "Synthetic rights", "Own MIT fixture", null);
        var project = await data.Imports.PromoteAsync(id, "Promoted synthetic fixture", "Synthetic");
        Assert.Equal(original, (await data.Imports.GetAsync(id)).Import.OriginalJson);
        var target = await data.Projects.GetAsync(project); Assert.Equal(2, target.Package.Definitions.Count); Assert.False(target.Project.RightsConfirmed);
        Assert.All(target.Package.Definitions, d => Assert.Equal(ReviewStatus.Unreviewed, d.Provenance.ReviewStatus));
        Assert.All(await data.Imports.CompareAsync(id, project), c => Assert.Equal("identical", c.State));
        var en = target.Catalogs.Single(); var de = en with { Language = "de", Entries = en.Entries.ToDictionary(p => p.Key, p => "Synthetischer Test: " + p.Value) };
        await using var catalog = StudioTestData.Json(de); await data.Projects.ImportCatalogAsync(project, target.Project.Revision, catalog, "Synthetic", "Own fictional translation");
        await data.Approve(project); Assert.NotEmpty(await data.Exports.ExportAsync(project));
        state = await data.Imports.GetAsync(id); await data.Imports.RightsAsync(id, state.Import.Revision, batch.Id, null, RedistributionDecision.Blocked, "Synthetic", "Source permission revoked", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Exports.ExportAsync(project));
    }
    [Fact]
    public async Task OriginalJsonSchemasAreRequiredAtBothImportBoundaries()
    {
        await using var data = await StudioTestData.CreateAsync();
        await using var invalid = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"batches\":[]}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => data.Imports.UploadAsync("bad.json", invalid, "Synthetic"));
        await using var badContent = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"));
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.ImportPackageAsync("Bad", badContent, "Synthetic"));
    }

    [Fact]
    public async Task AuthoringSelectionDoesNotLeakOtherApprovedSourceRecordsIntoTheReleaseProof()
    {
        await using var data = await StudioTestData.CreateAsync(); var workspace = await FixtureWorkspace(); await using var input = StudioTestData.Json(workspace);
        var id = await data.Imports.UploadAsync("synthetic.json", input, "Synthetic"); var state = await data.Imports.GetAsync(id); var batch = workspace.Batches[0];
        await data.Imports.ReviewAsync(id, state.Import.Revision, batch.Id, null, ReviewStatus.Approved, "Synthetic", "Own fixture", true);
        state = await data.Imports.GetAsync(id);
        await data.Imports.RightsAsync(id, state.Import.Revision, batch.Id, null, RedistributionDecision.Permitted, "Synthetic", "Own fixture permission", null);
        var project = await data.Imports.PromoteAsync(id, "Scoped synthetic release", "Synthetic"); var target = await data.Projects.GetAsync(project);
        var en = target.Catalogs.Single(); await using var de = StudioTestData.Json(en with { Language = "de", Entries = en.Entries.ToDictionary(p => p.Key, p => "Synthetisch " + p.Value) });
        await data.Projects.ImportCatalogAsync(project, target.Project.Revision, de, "Synthetic", "Own translation"); await data.Approve(project);
        target = await data.Projects.GetAsync(project); var omitted = target.Package.Definitions[1].Id;
        await data.Projects.ReviewAsync(project, target.Project.Revision, omitted, ReviewStatus.Rejected, "Synthetic", "Not in release");
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(await data.Exports.ExportAsync(project)));
        await using var reportStream = zip.GetEntry("source-report.json")!.Open();
        var report = await System.Text.Json.JsonSerializer.DeserializeAsync<ReleaseReport>(reportStream, ContentJson.Options);
        Assert.DoesNotContain(omitted, report!.SelectedDefinitionIds); Assert.Single(report.SelectedDefinitionIds);
        await using var sidecarStream = zip.GetEntry("source-assertions.json")!.Open();
        var sidecar = await System.Text.Json.JsonSerializer.DeserializeAsync<SourceAssertionDocument>(sidecarStream, ContentJson.Options);
        Assert.Empty(sidecar!.Assertions);
    }

    [Fact]
    public async Task WikidataAliasesKeepIndependentLanguagesThroughStudioReviewAndExport()
    {
        await using var data = await StudioTestData.CreateAsync();
        var batch = await new ImportEngine().ReadAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wikidata.json"), new()
        {
            Source = "wikidata", SourceVersion = "synthetic-1", SourceUrl = "https://example.org/synthetic", License = "CC0-1.0", LicenseText = "Own synthetic test file", Attribution = "Own test only",
            ImportedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), RetrievedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture)
        });
        await using var input = StudioTestData.Json(new ImportWorkspace { Batches = [batch] });
        var id = await data.Imports.UploadAsync("wikidata-synthetic.json", input, "Synthetic"); var state = await data.Imports.GetAsync(id);
        await data.Imports.ReviewAsync(id, state.Import.Revision, batch.Id, null, ReviewStatus.Approved, "Synthetic", "Own fixture", true);
        state = await data.Imports.GetAsync(id);
        await data.Imports.RightsAsync(id, state.Import.Revision, batch.Id, null, RedistributionDecision.Permitted, "Synthetic", "Own fixture permission", null);
        var project = await data.Imports.PromoteAsync(id, "Bilingual synthetic Wikidata", "Synthetic");
        Assert.Empty(ProjectService.Validate(await data.Projects.GetAsync(project))); await data.Approve(project);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(await data.Exports.ExportAsync(project)));
        foreach (var language in new[] { "en", "de" })
        {
            await using var catalogStream = zip.GetEntry("localization/" + language + ".json")!.Open();
            var catalog = await System.Text.Json.JsonSerializer.DeserializeAsync<LocalizationDefinition>(catalogStream, ContentJson.Options);
            Assert.Contains(catalog!.Entries.Keys, k => k.Contains(".alias." + language + ".", StringComparison.Ordinal));
            Assert.DoesNotContain(catalog.Entries.Keys, k => k.Contains(".alias." + (language == "en" ? "de" : "en") + ".", StringComparison.Ordinal));
        }
    }
    [Fact]
    public async Task RealWebHostServesGermanAndEnglishAndPreventsExternalCultureRedirects()
    {
        var folder = Path.Combine(Path.GetTempPath(), "iryomera-studio-http-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var factory = new StudioWebFactory(folder); using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            var en = await client.GetStringAsync("/"); Assert.Contains("Projects", en); Assert.Contains("Load synthetic examples", en);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/"); request.Headers.Add("Cookie", ".AspNetCore.Culture=c%3Dde%7Cuic%3Dde");
            var german = await client.SendAsync(request); Assert.Contains("Synthetische Beispiele laden", await german.Content.ReadAsStringAsync());
            var redirect = await client.GetAsync("/culture?culture=de&returnUrl=%2F%2Fevil.example"); Assert.Equal("/", redirect.Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/culture?culture=xx")).StatusCode);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class StudioWebFactory(string folder) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:DataDirectory"] = folder }));
    }
}
