using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.MedicalImporter.Tests;

public sealed class PipelineTests
{
    [Fact]
    public async Task RepeatedImportsAreByteReproducibleAndIdempotent()
    {
        using var data = new TestData();
        var first = await TestData.Import("hpo", "hpo.json"); var second = await TestData.Import("hpo", "hpo.json");
        Assert.Equal(TestData.Json(first), TestData.Json(second));
        var store = new WorkspaceStore(data.PathFor("workspace.json"));
        await store.AddAsync(first); var bytes = await File.ReadAllBytesAsync(store.Path);
        await store.AddAsync(second); Assert.Equal(bytes, await File.ReadAllBytesAsync(store.Path));
        Assert.Single((await store.ReadAsync()).Batches);
    }

    [Fact]
    public async Task FailedOrCanceledWorkspaceMutationPreservesThePreviousFile()
    {
        using var data = new TestData(); var store = new WorkspaceStore(data.PathFor("workspace.json"));
        await store.AddAsync(await TestData.Import("hpo", "hpo.json")); var before = await File.ReadAllBytesAsync(store.Path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(w => { w.Batches.Clear(); throw new InvalidOperationException("synthetic failure"); }));
        Assert.Equal(before, await File.ReadAllBytesAsync(store.Path));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UpdateAsync(_ => Task.CompletedTask, cts.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(store.Path));
    }

    [Fact]
    public async Task UnresolvedReferencesRemainVisibleUntilDefinitionsAreImported()
    {
        var workspace = new ImportWorkspace(); workspace.Batches.Add(await TestData.Import("hpo-annotations", "hpoa.tsv"));
        Assert.Contains(WorkspaceValidation.Validate(workspace), d => d.Code == "unresolved-reference");
        workspace.Batches.Add(await TestData.Import("hpo", "hpo.json"));
        Assert.DoesNotContain(WorkspaceValidation.Validate(workspace), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task AlternateIdsResolveOnlyFromExplicitOntologyAliases()
    {
        using var data = new TestData();
        await File.WriteAllTextAsync(data.PathFor("alias.tsv"), (await File.ReadAllTextAsync(TestData.Fixture("hpoa.tsv"))).Replace("HP:9999001", "HP:9999091", StringComparison.Ordinal));
        var workspace = new ImportWorkspace { Batches = [await TestData.Import("hpo", "hpo.json"), await new ImportEngine().ReadAsync(data.PathFor("alias.tsv"), TestData.Options("hpo-annotations"))] };
        Assert.DoesNotContain(WorkspaceValidation.Validate(workspace), d => d.Code == "unresolved-reference");
    }

    [Fact]
    public async Task ImportsNeverImplyApprovalOrDistributionPermission()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReleaseExporter().ExportAsync(workspace, data.PathFor("out")));
        TestData.Approve(workspace); workspace.Rights.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReleaseExporter().ExportAsync(workspace, data.PathFor("out")));
        Assert.False(Directory.Exists(data.PathFor("out")));
    }

    [Fact]
    public async Task ApprovedExportIsValidAndPreservesFullSourceContext()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace(); TestData.Approve(workspace);
        Assert.DoesNotContain(WorkspaceValidation.Validate(workspace), d => d.Severity == DiagnosticSeverity.Error);
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        var package = await TestData.Read<ContentPackage>(data.PathFor("out/content.json"));
        Assert.Empty(ContentValidator.Validate(package)); Assert.Equal(5, package.Definitions.Count);
        Assert.IsType<FindingDefinition>(package.Definitions.Single(x => x.Id == ImportIdentity.ContentId("synthetic_import", "HP:9999001")));
        Assert.Single(package.Relations); Assert.Equal(RelationKind.DiseaseFinding, package.Relations[0].Kind);
        Assert.DoesNotContain(package.Relations, r => r.Kind == RelationKind.DiseaseTreatment);
        Assert.All(package.Definitions, d => { Assert.Equal(ReviewStatus.Approved, d.Provenance.ReviewStatus); Assert.NotEmpty(d.Classifications); });
        var sidecar = await TestData.Read<SourceAssertionDocument>(data.PathFor("out/source-assertions.json"));
        var maxo = sidecar.Assertions.Single(a => a.Source.Kind == SourceAssertionKind.MaxoAnnotation);
        Assert.Equal("TREATS", maxo.Source.Predicate); Assert.NotNull(maxo.DiseaseId); Assert.Equal("SYNTHETIC:extension", maxo.Source.ExtensionExternalId);
        Assert.Equal(4, Directory.GetFiles(data.PathFor("out/LICENSES")).Length);
    }

    [Fact]
    public async Task RejectedOrUnreviewedContentCannotLeakThroughExportReports()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace(); TestData.Approve(workspace);
        var batch = workspace.Batches.Single(b => b.Options.Source == "maxo-annotations");
        foreach (var id in batch.Terms.Select(x => x.Id).Concat(batch.Assertions.Select(x => x.Id)))
            WorkspaceValidation.Review(workspace, batch.Id, id, ReviewStatus.Rejected, "Synthetic reviewer", TestData.ReviewTime.AddHours(1), "Excluded fixture.");
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        foreach (var path in Directory.GetFiles(data.PathFor("out"), "*", SearchOption.AllDirectories))
            Assert.DoesNotContain("Synthetic azure syndrome", await File.ReadAllTextAsync(path));
        var report = await TestData.Read<ReleaseReport>(data.PathFor("out/report.json")); Assert.Equal(2, report.OmittedUnapprovedRecords);
    }

    [Fact]
    public async Task ApprovedDanglingAssertionsBlockExportWithoutPartialFiles()
    {
        using var data = new TestData(); var workspace = new ImportWorkspace { Batches = [await TestData.Import("hpo-annotations", "hpoa-missing.tsv")] };
        TestData.Approve(workspace);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseExporter().ExportAsync(workspace, data.PathFor("out")));
        Assert.False(Directory.Exists(data.PathFor("out"))); Assert.Empty(Directory.GetDirectories(data.DirectoryPath));
    }

    [Fact]
    public async Task NegativeAnnotationsNeverBecomePositiveSdkRelations()
    {
        using var data = new TestData();
        var workspace = new ImportWorkspace { Batches = [await TestData.Import("hpo", "hpo.json"), await TestData.Import("hpo-annotations", "hpoa-negative.tsv")] };
        TestData.Approve(workspace); await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        Assert.Empty((await TestData.Read<ContentPackage>(data.PathFor("out/content.json"))).Relations);
        Assert.Contains((await TestData.Read<SourceAssertionDocument>(data.PathFor("out/source-assertions.json"))).Assertions, a => a.Source.SourceFields.TryGetValue("qualifier", out var v) && v.GetString() == "NOT");
    }

    [Fact]
    public async Task ObsoleteTermsCannotBeApprovedAndReviewNeedsAuditMetadata()
    {
        var batch = await TestData.Import("hpo", "hpo.obo"); var workspace = new ImportWorkspace { Batches = [batch] };
        var obsolete = batch.Terms.Single(t => t.Obsolete);
        Assert.Throws<InvalidOperationException>(() => WorkspaceValidation.Review(workspace, batch.Id, obsolete.Id, ReviewStatus.Approved, "Reviewer", TestData.ReviewTime, "Synthetic"));
        Assert.Throws<ArgumentException>(() => WorkspaceValidation.Review(workspace, batch.Id, batch.Terms[0].Id, ReviewStatus.Approved, "", TestData.ReviewTime, "Synthetic"));
    }

    [Fact]
    public async Task ExportIsByteReproducibleAndNeverOverwritesAnExistingRelease()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace(); TestData.Approve(workspace);
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("one")); await new ReleaseExporter().ExportAsync(workspace, data.PathFor("two"));
        foreach (var path in Directory.GetFiles(data.PathFor("one"), "*", SearchOption.AllDirectories))
            Assert.Equal(await File.ReadAllBytesAsync(path), await File.ReadAllBytesAsync(Path.Combine(data.PathFor("two"), Path.GetRelativePath(data.PathFor("one"), path))));
        await Assert.ThrowsAsync<IOException>(() => new ReleaseExporter().ExportAsync(workspace, data.PathFor("one")));
    }

    [Fact]
    public async Task MissingGermanTranslationsAreReportedWithoutFabrication()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace(); TestData.Approve(workspace);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"), ["en", "de"]));
        Assert.False(Directory.Exists(data.PathFor("out")));
    }

    [Fact]
    public async Task ConflictingSourceReleasesAreDetectedInsteadOfOverwritingProvenance()
    {
        var first = await TestData.Import("hpo", "hpo.json"); var second = await TestData.Import("hpo", "hpo.json");
        second.Terms[0] = second.Terms[0] with { Label = "A conflicting synthetic label" };
        var workspace = new ImportWorkspace { Batches = [first, second] };
        Assert.Contains(WorkspaceValidation.Validate(workspace), d => d.Code == "duplicate-id");
    }
}
