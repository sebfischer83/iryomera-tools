using System.Globalization;
using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.Localization;
using Xunit;

namespace Iryomera.MedicalImporter.Tests;

public sealed class AdditionalCasesTests
{
    [Fact]
    public async Task SynonymMatchesAreCandidatesAndNeverCreateMedicalLinks()
    {
        var batch = await TestData.Import("hpo", "hpo.json");
        batch.Terms[1] = batch.Terms[1] with { Synonyms = [batch.Terms[0].Label.ToUpperInvariant()] };
        var workspace = new ImportWorkspace { Batches = [batch] };
        var before = batch.Assertions.Count; var match = Assert.Single(TermMatching.Candidates(workspace));
        Assert.NotEqual(match.LeftId, match.RightId); Assert.Equal(before, batch.Assertions.Count);
    }

    [Fact]
    public async Task SourceBracesAreEscapedInCatalogsAndRetainedUnmodifiedInSourceData()
    {
        using var data = new TestData();
        var text = await File.ReadAllTextAsync(TestData.Fixture("maxo.json"));
        await File.WriteAllTextAsync(data.PathFor("braces.json"), text.Replace("Synthetic amber action", "Synthetic {amber} action", StringComparison.Ordinal));
        var workspace = new ImportWorkspace { Batches = [await new ImportEngine().ReadAsync(data.PathFor("braces.json"), TestData.Options("maxo"))] };
        TestData.Approve(workspace); await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        var catalog = await TestData.Read<LocalizationDefinition>(data.PathFor("out/localization/en.json"));
        var key = Assert.Single(catalog.Entries).Key;
        Assert.Equal("Synthetic {amber} action", new ContentLocalizer([catalog]).Format(key, CultureInfo.GetCultureInfo("en")));
        var definition = Assert.Single((await TestData.Read<ContentPackage>(data.PathFor("out/content.json"))).Definitions);
        Assert.Contains("Synthetic {amber} action", definition.Extensions!["sourceRecord"].GetRawText());
    }

    [Fact]
    public async Task AConflictingWriterCannotCorruptTheWorkspace()
    {
        using var data = new TestData(); var store = new WorkspaceStore(data.PathFor("workspace.json"));
        await store.AddAsync(await TestData.Import("hpo", "hpo.json")); var before = await File.ReadAllBytesAsync(store.Path);
        await using var held = new FileStream(store.Path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => store.UpdateAsync(_ => Task.CompletedTask));
        Assert.Equal(before, await File.ReadAllBytesAsync(store.Path));
    }

    [Fact]
    public async Task CancelledExportCannotLeaveAPartialRelease()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace(); TestData.Approve(workspace);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"), ct: cts.Token));
        Assert.False(Directory.Exists(data.PathFor("out"))); Assert.Empty(Directory.GetDirectories(data.DirectoryPath));
    }
    [Fact]
    public async Task MismatchedSourceVersionIsAVisibleError()
    {
        var batch = await new ImportEngine().ReadAsync(TestData.Fixture("hpo.json"), TestData.Options("hpo") with { SourceVersion = "wrong-release" });
        Assert.Contains(batch.Diagnostics, d => d.Code == "source-version-mismatch" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task BulkReviewIsTransactionalWhenObsoleteRecordsArePresent()
    {
        var batch = await TestData.Import("hpo", "hpo.obo"); var workspace = new ImportWorkspace { Batches = [batch] };
        Assert.Throws<InvalidOperationException>(() => WorkspaceValidation.ReviewBatch(workspace, batch.Id, ReviewStatus.Approved, "Reviewer", TestData.ReviewTime, "Synthetic"));
        Assert.Empty(workspace.Reviews);
    }

    [Theory]
    [InlineData("0%")]
    [InlineData("0/19")]
    [InlineData("HP:0040285")]
    [InlineData("2/5")]
    public async Task FrequencyContextIsNeverFlattenedIntoAnUnconditionalRelation(string frequency)
    {
        using var data = new TestData();
        var row = (await File.ReadAllLinesAsync(TestData.Fixture("hpoa.tsv")))[^1].Split('\t');
        row[7] = frequency;
        var lines = await File.ReadAllLinesAsync(TestData.Fixture("hpoa.tsv")); lines[^1] = string.Join('\t', row);
        await File.WriteAllLinesAsync(data.PathFor("context.tsv"), lines);
        var workspace = new ImportWorkspace { Batches = [await TestData.Import("hpo", "hpo.json"), await new ImportEngine().ReadAsync(data.PathFor("context.tsv"), TestData.Options("hpo-annotations"))] };
        TestData.Approve(workspace); await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        Assert.Empty((await TestData.Read<ContentPackage>(data.PathFor("out/content.json"))).Relations);
        Assert.Contains((await TestData.Read<SourceAssertionDocument>(data.PathFor("out/source-assertions.json"))).Assertions,
            a => a.Source.SourceFields.TryGetValue("frequency", out var f) && f.GetString() == frequency);
    }

}
