using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.ContentStudio.Data;
using Iryomera.ContentStudio.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Iryomera.ContentStudio.Tests;

internal sealed class StudioTestData : IAsyncDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "iryomera-studio-tests-" + Guid.NewGuid().ToString("N"));
    public ServiceProvider Services { get; }
    public ProjectService Projects => Services.GetRequiredService<ProjectService>();
    public ImportService Imports => Services.GetRequiredService<ImportService>();
    public StudioExportService Exports => Services.GetRequiredService<StudioExportService>();
    public IDbContextFactory<StudioDbContext> Factory => Services.GetRequiredService<IDbContextFactory<StudioDbContext>>();
    private StudioTestData()
    {
        Directory.CreateDirectory(DirectoryPath);
        var services = new ServiceCollection();
        services.AddDbContextFactory<StudioDbContext>(options => options.UseSqlite("Data Source=" + Path.Combine(DirectoryPath, "test.db") + ";Default Timeout=5"));
        services.AddScoped<ProjectService>(); services.AddScoped<ImportService>(); services.AddScoped<StudioExportService>();
        Services = services.BuildServiceProvider();
    }
    public static async Task<StudioTestData> CreateAsync()
    {
        var data = new StudioTestData(); await using var db = await data.Factory.CreateDbContextAsync(); await db.Database.MigrateAsync(); return data;
    }
    public Task<Guid> Synthetic() => Projects.LoadSyntheticAsync("Synthetic software test operator");
    public async Task Approve(Guid id)
    {
        var state = await Projects.GetAsync(id);
        await Projects.ReviewSelectionAsync(id, state.Project.Revision, StudioContent.Records(state.Package).Select(r => r.Id).ToArray(), "Synthetic reviewer", "Own fictional software fixtures only");
        state = await Projects.GetAsync(id);
        await Projects.ConfirmRightsAsync(id, state.Project.Revision, true, "Synthetic rights reviewer", "Own fictional MIT content only");
    }
    public static async Task<ContentPackage> PackageFromZip(byte[] data)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(data));
        await using var input = zip.GetEntry("content.json")!.Open();
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(input);
        if (!result.IsValid) throw new InvalidDataException(string.Join(';', result.Issues));
        return result.Value!;
    }
    public static MemoryStream Json<T>(T value) => new(JsonSerializer.SerializeToUtf8Bytes(value, ContentJson.Options));
    public async ValueTask DisposeAsync()
    { await Services.DisposeAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(DirectoryPath, true); }
}
