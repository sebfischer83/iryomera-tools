using System.Text.Json;
using Iryomera.Content.SDK;

namespace Iryomera.MedicalImporter.Tests;

internal sealed class TestData : IDisposable
{
    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "iryomera-import-tests-" + Guid.NewGuid().ToString("N"));
    internal static DateTimeOffset ImportTime => DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    internal static DateTimeOffset ReviewTime => ImportTime.AddDays(1);
    internal TestData() => Directory.CreateDirectory(DirectoryPath);
    internal string PathFor(string name) => Path.Combine(DirectoryPath, name);
    internal static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    internal static ImportOptions Options(string source, string format = "auto") => new()
    {
        Source = source, SourceVersion = "synthetic-1", SourceUrl = "https://example.org/iryomera/synthetic/" + source,
        License = "MIT", LicenseText = File.ReadAllText(Fixture("LICENSE.txt")), Attribution = "Iryomera synthetic fixtures; not medical knowledge.",
        ImportedAt = ImportTime, Namespace = "synthetic_import", Format = format
    };
    internal static Task<ImportBatch> Import(string source, string file) => new ImportEngine().ReadAsync(Fixture(file), Options(source));
    internal static async Task<ImportWorkspace> CompleteWorkspace()
    {
        var workspace = new ImportWorkspace();
        workspace.Batches.Add(await Import("hpo", "hpo.json"));
        workspace.Batches.Add(await Import("hpo-annotations", "hpoa.tsv"));
        workspace.Batches.Add(await Import("maxo", "maxo.obo"));
        workspace.Batches.Add(await Import("maxo-annotations", "maxo-annotations.tsv"));
        return workspace;
    }
    internal static void Approve(ImportWorkspace workspace)
    {
        foreach (var batch in workspace.Batches)
        {
            foreach (var id in batch.Terms.Select(x => x.Id).Concat(batch.Assertions.Select(x => x.Id)))
                WorkspaceValidation.Review(workspace, batch.Id, id, ReviewStatus.Approved, "Synthetic test reviewer", ReviewTime, "Fictional fixture approval for software tests only.");
            workspace.Rights.Add(new() { BatchId = batch.Id, Decision = RedistributionDecision.Permitted, Reviewer = "Synthetic rights reviewer", At = ReviewTime, Note = "Own synthetic MIT fixtures only." });
        }
    }
    internal static async Task<T> Read<T>(string path) where T : class
    {
        await using var stream = File.OpenRead(path);
        return await ContentJson.ReadAsync<T>(stream);
    }
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, ContentJson.Options);
    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}
