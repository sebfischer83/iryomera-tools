using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.ContentStudio.Data;
using Iryomera.Localization;
using Microsoft.EntityFrameworkCore;

namespace Iryomera.ContentStudio.Services;

public sealed partial class ProjectService(IDbContextFactory<StudioDbContext> factory)
{
    public Task<List<StudioProject>> ListAsync(CancellationToken ct = default) => WithDb(db => db.Projects.AsNoTracking().OrderByDescending(p => p.UpdatedAtUtc).ToListAsync(ct), ct);
    public async Task<ProjectSnapshot> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        return Read(entity);
    }
    internal static ProjectSnapshot Read(StudioProject entity) => new(entity, StudioContent.Parse<ContentPackage>(entity.PackageJson), StudioContent.Parse<List<LocalizationDefinition>>(entity.CatalogsJson));

    public async Task<SearchResult> SearchAsync(Guid projectId, string? search = null, string? kind = null, string? status = null, int page = 0, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Definitions.AsNoTracking().Where(d => d.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(d => d.Id.Contains(search) || d.NameEn.Contains(search) || d.NameDe.Contains(search));
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(d => d.Kind == kind);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(d => d.Status == status);
        return new(await query.CountAsync(ct), await query.OrderBy(d => d.Id).Skip(Math.Max(0, page) * 30).Take(30).ToListAsync(ct));
    }

    public async Task<Guid> CreateAsync(string title, string ns, string actor, CancellationToken ct = default)
    {
        var package = new ContentPackage { Manifest = new() { Id = ns + ":content", Namespace = ns, Version = "1.0.0", License = "MIT" } };
        Check(ContentValidator.Validate(package));
        return await CreateSnapshotAsync(title, package, [new() { Namespace = ns, Language = "en" }, new() { Namespace = ns, Language = "de" }], actor, null, ct);
    }

    public async Task<Guid> ImportPackageAsync(string title, Stream input, string actor, CancellationToken ct = default)
    {
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(input, cancellationToken: ct);
        if (result.Value is null) throw new StudioValidationException(result.Issues);
        Check(result.Issues.Where(i => i.Code is "duplicate-id" or "id" or "namespace-owner").ToArray());
        return await CreateSnapshotAsync(title, result.Value, [], actor, null, ct);
    }

    public async Task<Guid> LoadSyntheticAsync(string actor, CancellationToken ct = default)
    {
        var assembly = typeof(ProjectService).Assembly;
        // Files are copied for both dotnet run and published/offline execution.
        var samples = Path.Combine(Path.GetDirectoryName(assembly.Location)!, "Samples");
        await using var input = File.OpenRead(Path.Combine(samples, "content.json"));
        var package = (await ContentDocuments.ReadValidatedAsync<ContentPackage>(input, cancellationToken: ct)).Value ?? throw new InvalidDataException("Invalid bundled sample.");
        var catalogs = new List<LocalizationDefinition>();
        foreach (var language in new[] { "en", "de" })
        {
            await using var catalogInput = File.OpenRead(Path.Combine(samples, language + ".json"));
            catalogs.Add((await ContentDocuments.ReadValidatedAsync<LocalizationDefinition>(catalogInput, cancellationToken: ct)).Value ?? throw new InvalidDataException("Invalid sample catalog."));
        }
        return await CreateSnapshotAsync("Synthetic examples / Synthetische Beispiele", package, catalogs, actor, null, ct);
    }

    internal async Task<Guid> CreateSnapshotAsync(string title, ContentPackage package, List<LocalizationDefinition> catalogs, string actor, Guid? sourceImport, CancellationToken ct)
    {
        RequireAudit(actor, "Create project");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160) throw new ArgumentException("Project title is required (maximum 160 characters).");
        var original = StudioContent.Json(package);
        var sourceIds = sourceImport is null ? [] : StudioContent.Records(package).Select(r => r.Id).ToArray();
        for (var i = 0; i < package.Definitions.Count; i++) package.Definitions[i] = package.Definitions[i] with { Provenance = StudioContent.Draft(package.Definitions[i].Provenance) };
        for (var i = 0; i < package.Relations.Count; i++) package.Relations[i] = package.Relations[i] with { Provenance = StudioContent.Draft(package.Relations[i].Provenance) };
        for (var i = 0; i < package.MedicalAnnotations.Count; i++) package.MedicalAnnotations[i] = package.MedicalAnnotations[i] with { Provenance = StudioContent.Draft(package.MedicalAnnotations[i].Provenance) };
        for (var i = 0; i < package.ReferenceRanges.Count; i++) package.ReferenceRanges[i] = package.ReferenceRanges[i] with { Provenance = StudioContent.Draft(package.ReferenceRanges[i].Provenance) };
        for (var i = 0; i < package.DiagnosticRecommendations.Count; i++) package.DiagnosticRecommendations[i] = package.DiagnosticRecommendations[i] with { Provenance = StudioContent.Draft(package.DiagnosticRecommendations[i].Provenance) };
        var entity = new StudioProject { Id = Guid.NewGuid(), Title = title, SourceImportId = sourceImport, SourceRecordIdsJson = StudioContent.Json(sourceIds) };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Projects.Add(entity);
        await PersistAsync(db, new(entity, package, catalogs), "create", entity.Id.ToString(), actor, "New draft; source approval not inherited", original, ct);
        return entity.Id;
    }

    public static IReadOnlyList<ValidationIssue> Validate(ProjectSnapshot state)
    {
        var issues = ContentValidator.Validate(state.Package).ToList();
        issues.AddRange(LanguageSpecificAliases.Validate(state.Package, state.Catalogs));
        foreach (var gameplay in state.Package.Gameplay)
            foreach (var key in gameplay.RequiredResources.Keys.Where(k => k.Contains(':')))
                if (!state.Package.Definitions.Any(d => d.Id == key)) issues.Add(new("resource-reference", gameplay.DefinitionId, "Unknown simulation resource: " + key));
        return issues;
    }

    public static IReadOnlyList<DuplicateCandidate> Duplicates(ProjectSnapshot state)
    {
        string Normalize(string text) => text.Normalize(System.Text.NormalizationForm.FormKC).Trim().ToUpperInvariant();
        return state.Package.Definitions.SelectMany(d => new[] { "en", "de" }.Select(language => (d.Id, Language: language, Text: StudioContent.Text(state.Catalogs, d.NameKey, language))))
            .Where(x => !string.IsNullOrWhiteSpace(x.Text)).GroupBy(x => (x.Language, Text: Normalize(x.Text)))
            .SelectMany(g => g.OrderBy(x => x.Id, StringComparer.Ordinal).SelectMany((left, index) => g.OrderBy(x => x.Id, StringComparer.Ordinal).Skip(index + 1).Where(right => left.Id != right.Id).Select(right => new DuplicateCandidate(left.Id, right.Id, left.Text))))
            .Take(1000).ToArray();
    }

    public Task<List<StudioAudit>> AuditAsync(Guid id, CancellationToken ct = default) => WithDb(db => db.Audits.AsNoTracking().Where(a => a.ScopeId == id).OrderByDescending(a => a.Id).Take(100).ToListAsync(ct), ct);

    internal async Task MutateAsync(Guid id, long revision, string action, string target, string actor, string note, Action<ProjectSnapshot> mutation, bool invalidateRights = true, CancellationToken ct = default)
    {
        RequireAudit(actor, note);
        await using var db = await factory.CreateDbContextAsync(ct);
        var project = await db.Projects.SingleAsync(p => p.Id == id, ct);
        if (project.Revision != revision) throw new StudioConflictException();
        var state = Read(project); var before = AuditSnapshot(state);
        var oldIssues = Validate(state).Select(IssueKey).ToHashSet(StringComparer.Ordinal);
        mutation(state);
        Check(Validate(state).Where(i => i.Code is not ("missing-language" or "missing-translation" or "unused-key") && !oldIssues.Contains(IssueKey(i))).ToArray());
        if (invalidateRights) project.RightsConfirmed = false;
        await PersistAsync(db, state, action, target, actor, note, before, ct);
    }

    private static string IssueKey(ValidationIssue issue) => issue.Code + "|" + issue.Path + "|" + issue.Message;
    internal static void Check(IReadOnlyList<ValidationIssue> issues) { if (issues.Count > 0) throw new StudioValidationException(issues); }
    internal static void RequireAudit(string actor, string note)
    { if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(note) || actor.Length > 200 || note.Length > 4000) throw new ArgumentException("Reviewer and reason are required (maximum 200 / 4000 characters)."); }

    private static async Task PersistAsync(StudioDbContext db, ProjectSnapshot state, string action, string target, string actor, string note, string before, CancellationToken ct)
    {
        var project = state.Project;
        project.PackageJson = StudioContent.Json(state.Package); project.CatalogsJson = StudioContent.Json(state.Catalogs);
        project.Revision++; project.UpdatedAtUtc = DateTime.UtcNow;
        db.Audits.Add(new() { ScopeId = project.Id, Action = action, Target = target, Actor = actor, Note = note, BeforeJson = before, AfterJson = AuditSnapshot(state), AtUtc = project.UpdatedAtUtc });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await db.Definitions.Where(d => d.ProjectId == project.Id).ExecuteDeleteAsync(ct);
            db.Definitions.AddRange(state.Package.Definitions.Select(d => new DefinitionIndex
            { ProjectId = project.Id, Id = d.Id, Kind = StudioContent.Kind(d), NameEn = StudioContent.Text(state.Catalogs, d.NameKey, "en"), NameDe = StudioContent.Text(state.Catalogs, d.NameKey, "de"), Status = d.Provenance.ReviewStatus.ToString() }));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw new StudioConflictException(); }
    }

    private async Task<T> WithDb<T>(Func<StudioDbContext, Task<T>> action, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await action(db); }

    private static string AuditSnapshot(ProjectSnapshot state) => StudioContent.Json(new
    {
        Package = state.Package, Catalogs = state.Catalogs, state.Project.Revision,
        state.Project.RightsConfirmed, state.Project.RightsReviewer, state.Project.RightsNote
    });
}
