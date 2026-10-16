using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.MedicalImporter.Adapters;

namespace Iryomera.MedicalImporter;

/// <summary>Local-only adapter orchestration; no network, hidden data acquisition or automatic approval.</summary>
public sealed class ImportEngine
{
    public const string AdapterVersion = "1.1.0";
    private static readonly IImportAdapter[] Adapters =
    [new HpoAdapter(), new MaxoAdapter(), new HpoAnnotationAdapter(), new MaxoAnnotationAdapter(), new Icd10GmAdapter(), new LoincAdapter(), new OpsAdapter()];
    public static IReadOnlyList<IImportAdapter> Sources => Array.AsReadOnly(Adapters);

    public async Task<ImportBatch> ReadAsync(string inputPath, ImportOptions options, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        var adapter = Adapters.FirstOrDefault(x => x.SourceId == options.Source) ?? throw new ArgumentException("Unknown source adapter.", nameof(options));
        await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length == 0 || input.Length > options.MaxInputBytes) throw new InvalidDataException("Input is empty or exceeds max-input-bytes.");
        var format = options.Format == "auto" ? await DetectAsync(input, inputPath, cancellationToken) : options.Format;
        if (!adapter.Formats.Contains(format, StringComparer.Ordinal)) throw new InvalidDataException($"{options.Source} supports: {string.Join(", ", adapter.Formats)}; received {format}.");
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken));
        input.Position = 0;
        options = options with { Format = format, ImportedAt = options.ImportedAt.ToUniversalTime() };
        var batch = new ImportBatch
        {
            Id = BatchId(hash, options), AdapterVersion = AdapterVersion, InputSha256 = hash, InputBytes = input.Length, Options = options, DetectedFormat = format
        };
        await adapter.ReadAsync(input, format, new AdapterContext(batch), cancellationToken);
        if (batch.ObservedSourceVersion is { Length: > 0 } observed)
        {
            var expected = options.SourceVersion.StartsWith('v') ? options.SourceVersion[1..] : options.SourceVersion;
            var observedParts = observed.Split('/').Select(x => x.StartsWith('v') ? x[1..] : x);
            if (!observedParts.Contains(expected, StringComparer.Ordinal))
                batch.Diagnostics.Add(new(DiagnosticSeverity.Error, "source-version-mismatch", batch.Id,
                    $"Configured source version {options.SourceVersion} does not match observed source metadata {observed}."));
        }
        cancellationToken.ThrowIfCancellationRequested();
        input.Position = 0;
        if (hash != Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken)))
            throw new IOException("Input changed during import; no batch was saved.");
        if (batch.Terms.Count == 0 && batch.Assertions.Count == 0 && !batch.SourceMetadata.Keys.Any(k => k.StartsWith("unlabelledObsolete_", StringComparison.Ordinal))) throw new InvalidDataException("Input contained no supported records.");
        batch.Terms.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
        batch.Assertions.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
        batch.Diagnostics.Sort((a, b) => StringComparer.Ordinal.Compare(a.RecordId + "/" + a.Code + "/" + a.Message, b.RecordId + "/" + b.Code + "/" + b.Message));
        return batch;
    }

    internal static string BatchId(string hash, ImportOptions options, string adapterVersion = AdapterVersion, string? legacyNewLine = null) => "batch_" + ImportIdentity.Hash(JsonSerializer.SerializeToUtf8Bytes(new { hash, options, adapterVersion }, legacyNewLine is null ? ContentJson.Options : new(ContentJson.Options) { NewLine = legacyNewLine }));

    public static void ValidateOptions(ImportOptions options)
    {
        if (ContractShape.Validate(options).Count > 0) throw new ArgumentException("Import options contain null required values.");
        if (!ContentFormat.IsValidNamespace(options.Namespace)) throw new ArgumentException("Invalid namespace.");
        if (new[] { options.Source, options.SourceVersion, options.SourceUrl, options.License, options.LicenseText, options.Attribution }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Source, version, URL, license, full license text and attribution are required.");
        if (!Uri.TryCreate(options.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new ArgumentException("Source URL must be an absolute HTTP(S) URL; no request will be made.");
        if (options.ImportedAt == default || options.MaxInputBytes <= 0) throw new ArgumentException("Import timestamp and positive input limit are required.");
    }

    private static async Task<string> DetectAsync(Stream input, string path, CancellationToken ct)
    {
        var buffer = new byte[1024]; var count = await input.ReadAsync(buffer, ct); input.Position = 0;
        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, count).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (text.StartsWith('{')) return "json";
        if (text.StartsWith('<')) return "xml";
        if (text.StartsWith("format-version:", StringComparison.Ordinal)) return "obo";
        if (text.Contains('\t')) return "tsv";
        if (text.Contains(',')) return "csv";
        // HPOA metadata comments can fill the initial sniff buffer.
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".hpoa" or ".tsv" => "tsv", ".csv" => "csv", ".obo" => "obo", ".json" => "json", ".xml" => "xml",
            _ => throw new InvalidDataException("Unknown input format; configure --format explicitly. Compressed archives must be extracted locally first.")
        };
    }
}

/// <summary>Atomic workspace storage with exclusive writer lock; callers own the mutation transaction.</summary>
public sealed class WorkspaceStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public async Task<ImportWorkspace> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Path)) return new();
        await using var input = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous);
        var workspace = await ImportDocuments.ReadAsync<ImportWorkspace>(input, ct);
        WorkspaceValidation.EnsureShape(workspace);
        return workspace;
    }

    public async Task UpdateAsync(Func<ImportWorkspace, Task> mutation, CancellationToken ct = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        // Leave the lock file in place to avoid unlink/recreate races between concurrent processes.
        await using var exclusive = new FileStream(Path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var workspace = await ReadAsync(ct);
        await mutation(workspace);
        WorkspaceValidation.EnsureShape(workspace);
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

    public Task AddAsync(ImportBatch batch, CancellationToken ct = default) => UpdateAsync(workspace =>
    {
        if (!workspace.Batches.Any(x => x.Id == batch.Id)) workspace.Batches.Add(batch);
        workspace.Batches.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
        return Task.CompletedTask;
    }, ct);
}
