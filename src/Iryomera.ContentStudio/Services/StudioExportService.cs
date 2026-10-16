using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.ContentStudio.Data;
using Iryomera.MedicalImporter;
using Microsoft.EntityFrameworkCore;

namespace Iryomera.ContentStudio.Services;

public sealed class StudioExportService(IDbContextFactory<StudioDbContext> factory, ProjectService projects, ImportService imports)
{
    public static string Digest(object record)
    {
        var normalized = record switch
        {
            ContentDefinition d => (object)(d with { Provenance = StudioContent.Draft(d.Provenance) }),
            ContentRelation r => r with { Provenance = StudioContent.Draft(r.Provenance) },
            ObservationReferenceRange r => r with { Provenance = StudioContent.Draft(r.Provenance) },
            DiagnosticRecommendation r => r with { Provenance = StudioContent.Draft(r.Provenance) },
            MedicalAnnotation a => a with { Provenance = StudioContent.Draft(a.Provenance) },
            _ => throw new ArgumentException("Unsupported content record.")
        };
        return ImportIdentity.Hash(Encoding.UTF8.GetBytes(StudioContent.Json(normalized)));
    }
    public static bool AdditionalTerms(object record)
    {
        static bool Visit(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!.Contains("OMIM:", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => value.EnumerateArray().Any(Visit),
            JsonValueKind.Object => value.EnumerateObject().Any(p =>
                ((p.Name is "evidence" or "evidenceCode") && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == "IEA") ||
                ((p.Name is "EXTERNAL_COPYRIGHT_NOTICE" or "additional_terms" or "usage_restrictions") && p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString())) || Visit(p.Value)),
            _ => false
        };
        return Visit(JsonSerializer.SerializeToElement(record, ContentJson.Options));
    }
    public async Task GrantRecordPermissionAsync(Guid project, long revision, string id, bool permitted, string actor, string evidence, CancellationToken ct = default)
    {
        ProjectService.RequireAudit(actor, evidence);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Projects.SingleAsync(p => p.Id == project, ct);
        if (entity.Revision != revision) throw new StudioConflictException();
        var state = ProjectService.Read(entity); var record = StudioContent.Record(state.Package, id);
        db.Permissions.Add(new() { ProjectId = project, RecordId = id, Digest = Digest(record), Permitted = permitted, Reviewer = actor, Evidence = evidence, AtUtc = DateTime.UtcNow });
        entity.Revision++; entity.UpdatedAtUtc = DateTime.UtcNow;
        db.Audits.Add(new() { ScopeId = project, Action = "record-permission", Target = id, Actor = actor, Note = evidence, AfterJson = permitted.ToString(), AtUtc = DateTime.UtcNow });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw new StudioConflictException(); }
    }
    public async Task<byte[]> ExportAsync(Guid id, CancellationToken ct = default)
    {
        var state = await projects.GetAsync(id, ct);
        if (!state.Project.RightsConfirmed) throw new InvalidOperationException("Project redistribution rights are not confirmed.");
        var selected = StudioContent.Records(state.Package).Where(r => r.Provenance.ReviewStatus == ReviewStatus.Approved).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var package = state.Package with
        {
            Definitions = state.Package.Definitions.Where(d => selected.Contains(d.Id)).ToList(),
            Relations = state.Package.Relations.Where(r => selected.Contains(r.Id)).ToList(),
            MedicalAnnotations = state.Package.MedicalAnnotations.Where(a => selected.Contains(a.Id)).ToList(),
            ReferenceRanges = state.Package.ReferenceRanges.Where(r => selected.Contains(r.Id)).ToList(),
            DiagnosticRecommendations = state.Package.DiagnosticRecommendations.Where(r => selected.Contains(r.Id)).ToList(),
            DiagnosticSimulation = state.Package.DiagnosticSimulation is null ? null : state.Package.DiagnosticSimulation with
            { ObservationDistributions = state.Package.DiagnosticSimulation.ObservationDistributions.Where(d => selected.Contains(d.DiseaseId) && selected.Contains(d.ObservationId)).ToList(),
              Manifestations = state.Package.DiagnosticSimulation.Manifestations.Where(m => selected.Contains(m.DiseaseId) && selected.Contains(m.ManifestationId)).ToList(),
              ExaminationPolicies = state.Package.DiagnosticSimulation.ExaminationPolicies.Where(p => selected.Contains(p.ExaminationId)).ToList() },
            Gameplay = state.Package.Gameplay.Where(b => selected.Contains(b.DefinitionId)).ToList()
        };
        if (package.Definitions.Count == 0) throw new InvalidOperationException("No approved definitions.");
        var keys = Iryomera.Localization.LocalizationValidator.GetContentKeys(package).ToHashSet(StringComparer.Ordinal);
        var aliases = LanguageSpecificAliases.Keys(package);
        var catalogs = state.Catalogs.Select(c => c with { Entries = c.Entries.Where(e => keys.Contains(e.Key) || aliases.Contains((c.Language, e.Key))).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal) }).ToList();
        ProjectService.Check(ProjectService.Validate(new(state.Project, package, catalogs)).Where(i => i.Code != "unused-key").ToArray());
        await using var db = await factory.CreateDbContextAsync(ct);
        var permissions = await db.Permissions.Where(p => p.ProjectId == id).OrderBy(p => p.Id).ToListAsync(ct);
        foreach (var record in StudioContent.Records(package))
        {
            var permission = permissions.LastOrDefault(p => p.RecordId == record.Id && p.Digest == Digest(StudioContent.Record(package, record.Id)));
            if (permission?.Permitted == false || (AdditionalTerms(StudioContent.Record(package, record.Id)) && permission?.Permitted != true))
                throw new InvalidOperationException("Record-specific permission required: " + record.Id);
        }
        var temporary = Path.Combine(Path.GetTempPath(), "iryomera-studio-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (state.Project.SourceImportId is { } source && StudioContent.Parse<List<string>>(state.Project.SourceRecordIdsJson).Any(selected.Contains))
            {
                var workspace = await imports.GetAsync(source, ct);
                var scoped = workspace.Workspace with { Reviews = workspace.Workspace.Reviews.Where(r => selected.Contains(r.RecordId) || selected.Contains(r.RecordId + ".annotation")).ToList() };
                await new ReleaseExporter().ExportAsync(scoped, temporary, ct: ct);
                await using var sourceFile = File.OpenRead(Path.Combine(temporary, "content.json"));
                var fresh = await ContentJson.ReadAsync<ContentPackage>(sourceFile, ct);
                var available = StudioContent.Records(fresh).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
                var bound = StudioContent.Parse<List<string>>(state.Project.SourceRecordIdsJson);
                if (selected.Any(record => bound.Contains(record, StringComparer.Ordinal) && !available.Contains(record))) throw new InvalidOperationException("Source selection was revoked or is no longer approved.");
            }
            await using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                await EntryAsync(zip, "content.json", StudioContent.Json(package), ct);
                foreach (var catalog in catalogs) await EntryAsync(zip, "localization/" + catalog.Language + ".json", StudioContent.Json(catalog), ct);
                await EntryAsync(zip, "LICENSES/project.txt", package.Manifest.License + "\n" + state.Project.RightsNote, ct);
                await EntryAsync(zip, "review.json", StudioContent.Json(new StudioReleaseReport
                {
                    ProjectId = state.Project.Id, Revision = state.Project.Revision, RightsReviewer = state.Project.RightsReviewer, RightsNote = state.Project.RightsNote,
                    ApprovedRecords = StudioContent.Records(package).Select(r => new StudioReviewedRecord(r.Id, r.Provenance)).ToList(),
                    Permissions = permissions.Where(p => selected.Contains(p.RecordId) && p.Digest == Digest(StudioContent.Record(package, p.RecordId)))
                        .Select(p => new StudioPermissionNotice(p.RecordId, p.Digest, p.Permitted, p.Reviewer, p.Evidence, p.AtUtc)).ToList()
                }), ct);
                if (Directory.Exists(temporary))
                {
                    foreach (var license in Directory.GetFiles(Path.Combine(temporary, "LICENSES"), "*.txt"))
                        await EntryAsync(zip, "LICENSES/source-" + Path.GetFileName(license), await File.ReadAllTextAsync(license, ct), ct);
                    await EntryAsync(zip, "source-report.json", await File.ReadAllTextAsync(Path.Combine(temporary, "report.json"), ct), ct);
                    await EntryAsync(zip, "source-assertions.json", await File.ReadAllTextAsync(Path.Combine(temporary, "source-assertions.json"), ct), ct);
                }
            }
            return output.ToArray();
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
    private static async Task EntryAsync(ZipArchive zip, string name, string content, CancellationToken ct)
    { var entry = zip.CreateEntry(name); await using var stream = entry.Open(); await using var writer = new StreamWriter(stream, new UTF8Encoding(false)); await writer.WriteAsync(content.AsMemory(), ct); }
}
