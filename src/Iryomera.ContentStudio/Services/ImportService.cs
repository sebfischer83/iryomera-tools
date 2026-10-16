using System.Text;
using Iryomera.Content.SDK;
using Iryomera.ContentStudio.Data;
using Iryomera.MedicalImporter;
using Microsoft.EntityFrameworkCore;

namespace Iryomera.ContentStudio.Services;

public sealed record ImportSnapshot(StudioImport Import, ImportWorkspace Workspace);
public sealed record ImportComparison(string Id, string IncomingLabel, string? ExistingLabel, string State);

public sealed class ImportService(IDbContextFactory<StudioDbContext> factory, ProjectService projects)
{
    public async Task<List<StudioImport>> ListAsync(CancellationToken ct = default)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.Imports.AsNoTracking().OrderByDescending(i => i.UpdatedAtUtc).ToListAsync(ct); }
    public async Task<ImportSnapshot> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Imports.AsNoTracking().SingleAsync(i => i.Id == id, ct);
        return new(entity, StudioContent.Parse<ImportWorkspace>(entity.WorkspaceJson));
    }
    public async Task<Guid> UploadAsync(string name, Stream stream, string actor, CancellationToken ct = default)
    {
        ProjectService.RequireAudit(actor, "Import workspace");
        using var bytes = new MemoryStream();
        var buffer = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (bytes.Length + read > StudioOptions.MaxUploadBytes) throw new InvalidDataException("Workspace exceeds upload limit.");
            await bytes.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        bytes.Position = 0;
        var workspace = await ImportDocuments.ReadAsync<ImportWorkspace>(bytes, ct);
        var hash = ImportIdentity.Hash(bytes.ToArray());
        await using var db = await factory.CreateDbContextAsync(ct);
        var previous = await db.Imports.SingleOrDefaultAsync(i => i.Sha256 == hash, ct);
        if (previous is not null) return previous.Id;
        var entity = new StudioImport { Id = Guid.NewGuid(), Name = Path.GetFileName(name), Sha256 = hash, OriginalJson = Encoding.UTF8.GetString(bytes.ToArray()),
            WorkspaceJson = StudioContent.Json(workspace), Revision = 1, UpdatedAtUtc = DateTime.UtcNow };
        db.Imports.Add(entity);
        db.Audits.Add(new() { ScopeId = entity.Id, Action = "source-import", Target = hash, Actor = actor, Note = "Immutable source snapshot", AfterJson = entity.WorkspaceJson, AtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct); return entity.Id;
    }
    public async Task ReviewAsync(Guid id, long revision, string batch, string? record, ReviewStatus status, string actor, string note, bool bulk, CancellationToken ct = default) =>
        await MutateAsync(id, revision, actor, note, workspace =>
        {
            var at = DateTimeOffset.UtcNow;
            if (bulk) BulkReview.Apply(workspace, batch, status, actor, at, note);
            else WorkspaceValidation.Review(workspace, batch, record ?? throw new ArgumentException("Record is required."), status, actor, at, note);
        }, ct);
    public Task RightsAsync(Guid id, long revision, string batch, string? record, RedistributionDecision decision, string actor, string note, string? permission, CancellationToken ct = default) =>
        MutateAsync(id, revision, actor, note, workspace => RecordRights.Decide(workspace, new()
        { BatchId = batch, RecordId = string.IsNullOrWhiteSpace(record) ? null : record, Decision = decision, Reviewer = actor, Note = note, At = DateTimeOffset.UtcNow,
            PermissionEvidence = string.IsNullOrWhiteSpace(permission) ? null : permission }), ct);

    private async Task MutateAsync(Guid id, long revision, string actor, string note, Action<ImportWorkspace> change, CancellationToken ct)
    {
        ProjectService.RequireAudit(actor, note);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Imports.SingleAsync(i => i.Id == id, ct);
        if (entity.Revision != revision) throw new StudioConflictException();
        var workspace = StudioContent.Parse<ImportWorkspace>(entity.WorkspaceJson); var before = entity.WorkspaceJson;
        change(workspace);
        WorkspaceValidation.EnsureShape(workspace);
        entity.WorkspaceJson = StudioContent.Json(workspace); entity.Revision++; entity.UpdatedAtUtc = DateTime.UtcNow;
        db.Audits.Add(new() { ScopeId = id, Action = "source-review-rights", Target = id.ToString(), Actor = actor, Note = note, BeforeJson = before, AfterJson = entity.WorkspaceJson, AtUtc = DateTime.UtcNow });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw new StudioConflictException(); }
    }

    public async Task<Guid> PromoteAsync(Guid id, string title, string actor, CancellationToken ct = default)
    {
        var state = await GetAsync(id, ct);
        var root = Path.Combine(Path.GetTempPath(), "iryomera-studio-promote-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new ReleaseExporter().ExportAsync(state.Workspace, root, ct: ct);
            await using var content = File.OpenRead(Path.Combine(root, "content.json"));
            var package = (await ContentDocuments.ReadValidatedAsync<ContentPackage>(content, cancellationToken: ct)).Value ?? throw new InvalidDataException("Invalid importer release.");
            var catalogs = new List<LocalizationDefinition>();
            foreach (var path in Directory.GetFiles(Path.Combine(root, "localization"), "*.json"))
            { await using var input = File.OpenRead(path); catalogs.Add(await ContentJson.ReadAsync<LocalizationDefinition>(input, ct)); }
            return await projects.CreateSnapshotAsync(title, package, catalogs, actor, id, ct);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public async Task<IReadOnlyList<ImportComparison>> CompareAsync(Guid import, Guid project, CancellationToken ct = default)
    {
        var source = await GetAsync(import, ct); var target = await projects.GetAsync(project, ct);
        var index = target.Package.Definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
        return source.Workspace.Batches.SelectMany(b => b.Terms).DistinctBy(t => t.Id).Select(t =>
        {
            var existing = index.GetValueOrDefault(t.Id); var label = existing is null ? null : StudioContent.Name(target, existing, t.Language);
            return new ImportComparison(t.Id, t.Label, label, existing is null ? "new" : label == t.Label && StudioContent.Kind(existing).Equals(t.Kind.ToString(), StringComparison.OrdinalIgnoreCase) ? "identical" : "different");
        }).ToArray();
    }
}

public static class StudioOptions { public const long MaxUploadBytes = 32 * 1024 * 1024; }
