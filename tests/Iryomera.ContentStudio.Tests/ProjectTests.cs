using Iryomera.Content.SDK;
using Iryomera.ContentStudio.Data;
using Iryomera.ContentStudio.Services;
using Iryomera.Localization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Iryomera.ContentStudio.Tests;

public sealed class ProjectTests
{
    [Fact]
    public async Task RealSqliteMigrationPersistsProjectsAndSearchesAcrossReopenedContexts()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic();
        await using var db = await data.Factory.CreateDbContextAsync();
        Assert.Single(await db.Database.GetAppliedMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(22, (await data.Projects.SearchAsync(id)).Total);
        Assert.Equal(3, (await data.Projects.SearchAsync(id, kind: "disease")).Total);
        Assert.NotEmpty((await data.Projects.SearchAsync(id, "Bernstein")).Items);
        Assert.All((await data.Projects.SearchAsync(id, status: ReviewStatus.Unreviewed.ToString())).Items, d => Assert.Equal("Unreviewed", d.Status));
        Assert.Contains(await data.Projects.AuditAsync(id), a => a.Action == "create");
    }
    [Fact]
    public async Task MedicalEditPreservesSourceAndResetsRecordDependentApprovalAndProjectRights()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); await data.Approve(id);
        var state = await data.Projects.GetAsync(id); var disease = state.Package.Definitions.OfType<DiseaseDefinition>().First(); var source = disease.Provenance.Source;
        var edit = ProjectService.EditModel(state, disease); edit.NameEn = "Fictional corrected label"; edit.Actor = "Synthetic editor"; edit.Note = "Own fixture correction";
        await data.Projects.SaveDefinitionAsync(id, state.Project.Revision, edit);
        var after = await data.Projects.GetAsync(id); Assert.Equal(source, after.Package.Definitions.Single(d => d.Id == disease.Id).Provenance.Source);
        Assert.Equal(ReviewStatus.Unreviewed, after.Package.Definitions.Single(d => d.Id == disease.Id).Provenance.ReviewStatus); Assert.False(after.Project.RightsConfirmed);
        Assert.All(after.Package.Relations.Where(r => r.FromId == disease.Id || r.ToId == disease.Id), r => Assert.Equal(ReviewStatus.Unreviewed, r.Provenance.ReviewStatus));
        Assert.Equal("Fictional corrected label", StudioContent.Text(after.Catalogs, disease.NameKey, "en"));
        Assert.Contains("Fictional corrected label", (await data.Projects.AuditAsync(id)).First().AfterJson);
    }
    [Fact]
    public async Task InvalidEditsAndStaleRevisionsCannotPartiallyChangeTheDatabase()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); var state = await data.Projects.GetAsync(id);
        var edit = ProjectService.EditModel(state, state.Package.Definitions[0]); edit.Actor = "Synthetic"; edit.Note = "Test"; edit.Version = "invalid";
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveDefinitionAsync(id, state.Project.Revision, edit));
        Assert.Equal(state.Project.Revision, (await data.Projects.GetAsync(id)).Project.Revision);
        edit.Version = "1.0.0"; await data.Projects.SaveDefinitionAsync(id, state.Project.Revision, edit);
        await Assert.ThrowsAsync<StudioConflictException>(() => data.Projects.SaveDefinitionAsync(id, state.Project.Revision, edit));
    }
    [Fact]
    public async Task SimulationEditsPreserveMedicalApprovalAndRejectInvalidProbability()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); await data.Approve(id); var state = await data.Projects.GetAsync(id);
        var definition = state.Package.Definitions[0]; var balance = new GameplayBalance { DefinitionId = definition.Id, SimulatedSuccessProbability = 0.25m, ExaminationDurationSeconds = 17 };
        await data.Projects.SaveGameplayAsync(id, state.Project.Revision, balance, "Synthetic balancing editor", "Simulation only");
        var after = await data.Projects.GetAsync(id); Assert.True(after.Project.RightsConfirmed); Assert.Equal(ReviewStatus.Approved, after.Package.Definitions[0].Provenance.ReviewStatus);
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveGameplayAsync(id, after.Project.Revision, balance with { SimulatedSuccessProbability = 1.5m }, "Synthetic", "Test"));
    }
    [Fact]
    public async Task DuplicateIdsAndWrongRelationshipTypesAreRejectedServerSide()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); var state = await data.Projects.GetAsync(id);
        var edit = ProjectService.EditModel(state, state.Package.Definitions[0]); edit.Actor = "Synthetic"; edit.Note = "Test";
        await Assert.ThrowsAsync<ArgumentException>(() => data.Projects.SaveDefinitionAsync(id, state.Project.Revision, edit, create: true));
        var original = state.Package.Relations[0];
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveRelationAsync(id, state.Project.Revision, original with { ToId = original.FromId }, "Synthetic", "Wrong type test"));
    }
    [Fact]
    public async Task ApprovalDoesNotBypassMissingGermanTranslationsOrPlaceholderErrors()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); var state = await data.Projects.GetAsync(id);
        var edit = ProjectService.EditModel(state, state.Package.Definitions[0]); edit.NameDe = ""; edit.Actor = "Synthetic"; edit.Note = "Missing translation test";
        await data.Projects.SaveDefinitionAsync(id, state.Project.Revision, edit); state = await data.Projects.GetAsync(id);
        Assert.Contains(ProjectService.Validate(state), i => i.Code == "missing-translation");
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.ReviewAsync(id, state.Project.Revision, edit.Id, ReviewStatus.Approved, "Synthetic", "Cannot approve"));
        edit.NameEn = "Synthetic {value}"; edit.NameDe = "Synthetisch {other}";
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Projects.SaveDefinitionAsync(id, state.Project.Revision, edit));
    }
    [Fact]
    public async Task ExportRequiresRightsAndApprovedReferenceClosureAndDropsRejectedDefinitions()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic();
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Exports.ExportAsync(id)); await data.Approve(id);
        Assert.Equal(22, (await StudioTestData.PackageFromZip(await data.Exports.ExportAsync(id))).Definitions.Count);
        var state = await data.Projects.GetAsync(id); var target = state.Package.Relations[0].ToId;
        await data.Projects.ReviewAsync(id, state.Project.Revision, target, ReviewStatus.Rejected, "Synthetic", "Testing rejected endpoint");
        await Assert.ThrowsAsync<StudioValidationException>(() => data.Exports.ExportAsync(id));
    }
    [Fact]
    public async Task RecordPermissionIsBoundToCurrentContentAndCanBeRevoked()
    {
        await using var data = await StudioTestData.CreateAsync(); var id = await data.Synthetic(); var state = await data.Projects.GetAsync(id);
        var disease = state.Package.Definitions.OfType<DiseaseDefinition>().First(); var edit = ProjectService.EditModel(state, disease);
        edit.ClassificationsJson = StudioContent.Json(new[] { new ExternalIdentifier { System = "OMIM", Value = "OMIM:999999", Provenance = disease.Provenance } }); edit.Actor = "Synthetic"; edit.Note = "Own fictional restricted-ID test";
        await data.Projects.SaveDefinitionAsync(id, state.Project.Revision, edit); await data.Approve(id); state = await data.Projects.GetAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Exports.ExportAsync(id));
        await data.Exports.GrantRecordPermissionAsync(id, state.Project.Revision, disease.Id, true, "Synthetic rights", "Own fixture permission only");
        Assert.NotEmpty(await data.Exports.ExportAsync(id)); state = await data.Projects.GetAsync(id);
        await data.Exports.GrantRecordPermissionAsync(id, state.Project.Revision, disease.Id, false, "Synthetic rights", "Test revocation");
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Exports.ExportAsync(id));
    }
    [Fact]
    public async Task UnknownExtensionFieldsSurviveAuthoringAndOriginalSourceCannotBeRelicensed()
    {
        await using var data = await StudioTestData.CreateAsync(); var original = await data.Synthetic(); var seed = await data.Projects.GetAsync(original);
        seed.Package.Definitions[0] = seed.Package.Definitions[0] with { Extensions = new() { ["futureField"] = System.Text.Json.JsonSerializer.SerializeToElement(new { value = 42 }) } };
        await using var input = StudioTestData.Json(seed.Package); var id = await data.Projects.ImportPackageAsync("Extension fixture", input, "Synthetic");
        var state = await data.Projects.GetAsync(id); var recordId = state.Package.Definitions[0].Id; var edit = ProjectService.EditModel(state, state.Package.Definitions[0]); edit.NameEn = "Own synthetic test label"; edit.NameDe = "Eigene synthetische Bezeichnung"; edit.Actor = "Synthetic"; edit.Note = "Preservation test";
        await data.Projects.SaveDefinitionAsync(id, state.Project.Revision, edit); state = await data.Projects.GetAsync(id);
        Assert.Equal(42, state.Package.Definitions.Single(d => d.Id == recordId).Extensions!["futureField"].GetProperty("value").GetInt32());
        edit.License = "Changed license"; await Assert.ThrowsAsync<ArgumentException>(() => data.Projects.SaveDefinitionAsync(id, state.Project.Revision, edit));
    }
    [Fact]
    public void UiCatalogsAreBilingualAndUseTheSharedLocalizer()
    {
        var catalogs = StudioUiCatalogs.Load(); var localizer = new StudioStringLocalizer(catalogs);
        Assert.True(catalogs.TryGetTemplate("studio.projects", System.Globalization.CultureInfo.GetCultureInfo("de"), out var german)); Assert.Equal("Projekte", german);
        Assert.True(catalogs.GetKeys(System.Globalization.CultureInfo.GetCultureInfo("en"), false).Count() > 100);
        Assert.Equal(catalogs.GetKeys(System.Globalization.CultureInfo.GetCultureInfo("en"), false), catalogs.GetKeys(System.Globalization.CultureInfo.GetCultureInfo("de"), false));
        Assert.False(localizer["studio.kind.medicalProcedure"].ResourceNotFound);
    }
}
