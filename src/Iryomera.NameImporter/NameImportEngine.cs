using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.NameImporter.Adapters;

namespace Iryomera.NameImporter;

/// <summary>Local-only adapter orchestration; no network, hidden data acquisition or automatic approval.</summary>
public sealed class NameImportEngine
{
    private static readonly INameAdapter[] Adapters = [new UsSsaAdapter(), new UsCensusSurnamesAdapter(), new AtStatistikAdapter(), new WikidataNamesAdapter(), new SigpwnedAdapter()];
    public static IReadOnlyList<INameAdapter> Sources => Array.AsReadOnly(Adapters);

    public async Task<NameBatch> ReadAsync(string inputPath, NameImportOptions options, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        var adapter = Adapters.FirstOrDefault(x => x.SourceId == options.Source) ?? throw new ArgumentException("Unknown name source adapter.", nameof(options));
        adapter.ValidateOptions(options);
        await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length == 0 || input.Length > options.MaxInputBytes) throw new InvalidDataException("Input is empty or exceeds max-input-bytes.");
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken));
        input.Position = 0;
        options = options with { ImportedAt = options.ImportedAt.ToUniversalTime(), RetrievedAt = options.RetrievedAt?.ToUniversalTime() };
        var batch = new NameBatch
        {
            Id = "batch_" + NameIdentity.HashJson(new { hash, options, adapter = adapter.SourceId, adapterVersion = adapter.Version }),
            InputSha256 = hash, InputBytes = input.Length, Options = options, AdapterVersion = adapter.Version, DetectedFormat = adapter.Format
        };
        try { await adapter.ReadAsync(input, new NameAdapterContext(batch), cancellationToken); }
        catch (DecoderFallbackException e) { throw new InvalidDataException("Input is not valid UTF-8; no batch was saved.", e); }
        catch (JsonException e) { throw new InvalidDataException("Input is not valid JSON; no batch was saved.", e); }
        cancellationToken.ThrowIfCancellationRequested();
        input.Position = 0;
        if (hash != Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken)))
            throw new IOException("Input changed during import; no batch was saved.");
        if (batch.Records.Count == 0) throw new InvalidDataException("Input contained no importable names.");
        batch.Records.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
        batch.Diagnostics.Sort((a, b) => StringComparer.Ordinal.Compare(a.RecordId + "/" + a.Code + "/" + a.Message, b.RecordId + "/" + b.Code + "/" + b.Message));
        return batch;
    }

    public static void ValidateOptions(NameImportOptions options)
    {
        if (ContractShape.Validate(options).Count > 0) throw new ArgumentException("Import options contain null required values.");
        if (!ContentFormat.IsValidNamespace(options.Namespace)) throw new ArgumentException("Invalid namespace.");
        if (new[] { options.Source, options.SourceVersion, options.SourceUrl, options.License, options.LicenseText, options.Attribution }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Source, version, URL, license, full license text and attribution are required.");
        if (!Uri.TryCreate(options.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new ArgumentException("Source URL must be an absolute HTTP(S) URL; no request will be made.");
        if (options.ImportedAt == default || options.MaxInputBytes <= 0) throw new ArgumentException("Import timestamp and positive input limit are required.");
        if (options.BirthYear is < 1000 or > 9999) throw new ArgumentException("Birth year must be a four-digit year.");
    }
}

/// <summary>
/// Shared normalization: surrounding whitespace is removed and text is converted to NFC, both reported as diagnostics;
/// the original values stay in SourceFields. Invalid names are reported and not staged.
/// </summary>
public sealed class NameAdapterContext(NameBatch batch)
{
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    public NameBatch Batch { get; } = batch;
    public NameImportOptions Options => Batch.Options;

    public void Diagnostic(NameDiagnosticSeverity severity, string code, string id, string message) => Batch.Diagnostics.Add(new(severity, code, id, message));

    public void Record(string row, string country, NamePart part, string localized, SortedDictionary<string, JsonElement> fields,
        NameGender? gender = null, string? romanized = null, string? romanizationOrigin = null, int? rank = null, long? count = null,
        decimal? proportion = null, int? birthYear = null, string? externalId = null)
    {
        var id = Options.Namespace + ":name_" + NameIdentity.HashJson(new { source = Options.Source, fields })[..32];
        if (country.Length != 2 || !country.All(c => c is >= 'A' and <= 'Z'))
        { Diagnostic(NameDiagnosticSeverity.Error, "name-country", row, $"Unsupported country code '{country}'; record not staged."); return; }
        var name = Normalize(localized, row, "localized");
        if (name is null || !NameText.IsValid(name))
        { Diagnostic(NameDiagnosticSeverity.Error, "name-text", row, "Name text is empty or violates the SDK name rules; record not staged."); return; }
        var latin = romanized is null ? null : Normalize(romanized, row, "romanized");
        if (latin is not null && (!NameText.IsValid(latin) || !NameText.IsLatin(latin)))
        { Diagnostic(NameDiagnosticSeverity.Warning, "romanized-dropped", row, "Romanized value is not valid Latin-script name text; it was not staged."); latin = null; }
        if (latin == name) latin = null;
        if (!_ids.Add(id)) { Diagnostic(NameDiagnosticSeverity.Warning, "duplicate-row", row, "Identical source row repeated; retained once."); return; }
        Batch.Records.Add(new()
        {
            Id = id, Country = country, Part = part, Localized = name, Romanized = latin, RomanizationOrigin = latin is null ? null : romanizationOrigin,
            Gender = gender, Rank = rank, Count = count, Proportion = proportion, BirthYearFrom = birthYear, BirthYearTo = birthYear,
            ExternalId = externalId, SourceFields = fields
        });
    }

    private string? Normalize(string value, string row, string field)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed != value) Diagnostic(NameDiagnosticSeverity.Warning, "name-trimmed", row, $"Surrounding whitespace removed from {field}.");
        string normalized;
        // Unpaired surrogates cannot be normalized; NameText.IsValid then rejects the unchanged value.
        try { normalized = trimmed.IsNormalized(NormalizationForm.FormC) ? trimmed : trimmed.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return trimmed; }
        if (normalized != trimmed) Diagnostic(NameDiagnosticSeverity.Information, "name-normalized", row, $"{field} converted to Unicode NFC.");
        return normalized;
    }
}
