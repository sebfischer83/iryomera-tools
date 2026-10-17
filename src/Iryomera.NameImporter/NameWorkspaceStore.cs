using System.Globalization;
using System.Text.Json;
using Iryomera.Content.SDK;
using Json.Schema;

namespace Iryomera.NameImporter;

/// <summary>Offline schema validation on original staging JSON, before CLR defaults.</summary>
public static class NameDocuments
{
    private static readonly Lazy<JsonSchema> Bundle = new(() =>
    {
        using var resource = typeof(NameDocuments).Assembly.GetManifestResourceStream("Iryomera.NameImporter.Schemas.v1.json")
            ?? throw new InvalidOperationException("Missing name importer schemas.");
        using var reader = new StreamReader(resource);
        return JsonSchema.FromText(reader.ReadToEnd());
    });

    public static async Task<T> ReadAsync<T>(Stream input, CancellationToken ct = default) where T : class
    {
        var name = typeof(T) == typeof(NameWorkspace) ? "nameWorkspace" : typeof(T) == typeof(NameReleaseReport) ? "nameReleaseReport"
            : throw new NotSupportedException("Unsupported name importer document type.");
        using var document = await JsonDocument.ParseAsync(input, new JsonDocumentOptions { AllowDuplicateProperties = false }, ct);
        var schema = JsonSchema.FromText("{\"$ref\":\"https://schemas.iryomera.org/names/v1/contracts.schema.json#/$defs/" + name + "\"}");
        var options = new EvaluationOptions { RequireFormatValidation = true };
        options.SchemaRegistry.Register(Bundle.Value);
        if (!schema.Evaluate(document.RootElement, options).IsValid) throw new InvalidDataException("Name importer JSON does not match " + name + " schema v1.");
        return document.RootElement.Deserialize<T>(ContentJson.Options) ?? throw new InvalidDataException("Expected a name importer document object.");
    }
}

/// <summary>Atomic workspace storage with an exclusive writer lock; callers own the mutation transaction.</summary>
public sealed class NameWorkspaceStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public async Task<NameWorkspace> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Path)) return new();
        await using var input = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous);
        var workspace = await NameDocuments.ReadAsync<NameWorkspace>(input, ct);
        NameWorkspaceValidation.EnsureShape(workspace);
        return workspace;
    }

    public async Task UpdateAsync(Action<NameWorkspace> mutation, CancellationToken ct = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        // Leave the lock file in place to avoid unlink/recreate races between concurrent processes.
        await using var exclusive = new FileStream(Path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var workspace = await ReadAsync(ct);
        mutation(workspace);
        NameWorkspaceValidation.EnsureShape(workspace);
        ct.ThrowIfCancellationRequested();
        var temporary = Path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await ContentJson.WriteAsync(output, workspace, ct);
                await output.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, Path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Re-importing identical input and options is idempotent.</summary>
    public Task AddAsync(NameBatch batch, CancellationToken ct = default) => UpdateAsync(workspace =>
    {
        if (!workspace.Batches.Any(x => x.Id == batch.Id)) workspace.Batches.Add(batch);
        workspace.Batches.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
    }, ct);
}

public static class NameWorkspaceValidation
{
    /// <summary>Structural invariants every stored workspace must satisfy; violations are not repairable diagnostics.</summary>
    public static void EnsureShape(NameWorkspace workspace)
    {
        if (workspace.SchemaVersion != "1.0.0") throw new InvalidDataException("Unsupported name workspace schema version.");
        if (ContractShape.Validate(workspace).Count > 0) throw new InvalidDataException("Name workspace contains null required values.");
        var batches = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var batch in workspace.Batches)
        {
            var records = new HashSet<string>(StringComparer.Ordinal);
            if (!batches.TryAdd(batch.Id, records)) throw new InvalidDataException($"Duplicate batch {batch.Id}.");
            foreach (var record in batch.Records)
                if (!records.Add(record.Id)) throw new InvalidDataException($"Duplicate record {record.Id} in {batch.Id}.");
        }
        foreach (var review in workspace.Reviews)
        {
            if (!batches.TryGetValue(review.BatchId, out var records)) throw new InvalidDataException($"Review references unknown batch {review.BatchId}.");
            if (review.RecordId is { } id && !records.Contains(id)) throw new InvalidDataException($"Review references unknown record {id}.");
            if (string.IsNullOrWhiteSpace(review.Reviewer) || string.IsNullOrWhiteSpace(review.Note) || review.At == default) throw new InvalidDataException("Reviews require reviewer, note and timestamp.");
        }
        foreach (var rights in workspace.Rights)
        {
            if (!batches.ContainsKey(rights.BatchId)) throw new InvalidDataException($"Rights decision references unknown batch {rights.BatchId}.");
            if (string.IsNullOrWhiteSpace(rights.Reviewer) || string.IsNullOrWhiteSpace(rights.Note) || rights.At == default) throw new InvalidDataException("Rights decisions require reviewer, note and timestamp.");
            if (rights.Decision == RedistributionDecision.Permitted && string.IsNullOrWhiteSpace(rights.PermissionEvidence))
                throw new InvalidDataException("A permitted redistribution decision requires permission evidence.");
        }
    }

    /// <summary>The latest record decision wins over the latest batch decision; no decision means unreviewed.</summary>
    public static ReviewStatus Status(NameWorkspace workspace, string batchId, string recordId) =>
        (workspace.Reviews.LastOrDefault(r => r.BatchId == batchId && r.RecordId == recordId)
         ?? workspace.Reviews.LastOrDefault(r => r.BatchId == batchId && r.RecordId is null))?.Status ?? ReviewStatus.Unreviewed;

    public static RedistributionDecision Rights(NameWorkspace workspace, string batchId) =>
        workspace.Rights.LastOrDefault(r => r.BatchId == batchId)?.Decision ?? RedistributionDecision.Undecided;

    /// <summary>Repairable findings: import errors and staged text that no longer satisfies the SDK name rules.</summary>
    public static IReadOnlyList<NameDiagnostic> Validate(NameWorkspace workspace)
    {
        var diagnostics = new List<NameDiagnostic>();
        foreach (var batch in workspace.Batches)
        {
            diagnostics.AddRange(batch.Diagnostics.Where(d => d.Severity == NameDiagnosticSeverity.Error));
            foreach (var record in batch.Records)
                if (!NameText.IsValid(record.Localized) || record.Romanized is { } r && (!NameText.IsValid(r) || !NameText.IsLatin(r)))
                    diagnostics.Add(new(NameDiagnosticSeverity.Error, "name-text", record.Id, "Staged name text does not satisfy the SDK name rules."));
        }
        return diagnostics;
    }
}
