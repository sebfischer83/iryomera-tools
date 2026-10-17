using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Iryomera.Content.SDK;

namespace Iryomera.NameImporter;

public enum NameDiagnosticSeverity { Information, Warning, Error }
public enum RedistributionDecision { Undecided, Permitted, Blocked }

/// <summary>Batch metadata supplied by the operator; the importer never downloads or accepts upstream terms.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NameImportOptions
{
    public required string Source { get; init; }
    public required string SourceVersion { get; init; }
    public required string SourceUrl { get; init; }
    public required string License { get; init; }
    public required string LicenseText { get; init; }
    public required string Attribution { get; init; }
    public required DateTimeOffset ImportedAt { get; init; }
    public string Namespace { get; init; } = "names";
    /// <summary>Birth year of a single-year newborn file (US SSA yobYYYY.txt).</summary>
    public int? BirthYear { get; init; }
    /// <summary>Actual retrieval time of a saved query result (wikidata-names).</summary>
    public DateTimeOffset? RetrievedAt { get; init; }
    /// <summary>
    /// Operator decision mapping name-label language tags to ISO 3166-1 countries (wikidata-names); recorded in the
    /// batch identity. Labels in unmapped languages are not staged. The tool never derives countries from languages itself.
    /// </summary>
    public SortedDictionary<string, string>? LanguageCountries { get; init; }
    public long MaxInputBytes { get; init; } = 256L * 1024 * 1024;
}

/// <summary>One normalized source row. SourceFields keep the original values verbatim.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NameRecord
{
    public required string Id { get; init; }
    public required string Country { get; init; }
    public string? Region { get; init; }
    public required NamePart Part { get; init; }
    public required string Localized { get; init; }
    public string? Romanized { get; init; }
    public string? RomanizationOrigin { get; init; }
    public NameGender? Gender { get; init; }
    public int? Rank { get; init; }
    public long? Count { get; init; }
    public decimal? Proportion { get; init; }
    public int? BirthYearFrom { get; init; }
    public int? BirthYearTo { get; init; }
    public string? ExternalId { get; init; }
    public SortedDictionary<string, JsonElement> SourceFields { get; init; } = new(StringComparer.Ordinal);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NameDiagnostic(NameDiagnosticSeverity Severity, string Code, string RecordId, string Message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NameBatch
{
    public required string Id { get; init; }
    public required string InputSha256 { get; init; }
    public required long InputBytes { get; init; }
    public required NameImportOptions Options { get; init; }
    public required string AdapterVersion { get; init; }
    public required string DetectedFormat { get; init; }
    public List<NameRecord> Records { get; init; } = [];
    public List<NameDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>Append-only. A null RecordId applies to every record of the batch; a later record decision overrides it.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NameReviewDecision
{
    public required string BatchId { get; init; }
    public string? RecordId { get; init; }
    public required ReviewStatus Status { get; init; }
    public required string Reviewer { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string Note { get; init; }
}

/// <summary>Append-only batch redistribution decision; permission requires evidence for the exact source release and use.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NameRightsDecision
{
    public required string BatchId { get; init; }
    public required RedistributionDecision Decision { get; init; }
    public string? PermissionEvidence { get; init; }
    public required string Reviewer { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string Note { get; init; }
}

/// <summary>Strict, versioned name staging workspace; independent of the additive SDK content format.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NameWorkspace
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = "1.0.0";
    public List<NameBatch> Batches { get; init; } = [];
    public List<NameReviewDecision> Reviews { get; init; } = [];
    public List<NameRightsDecision> Rights { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NameReleaseBatch(string Id, string Source, string SourceVersion, string License, string InputSha256, int ExportedRecords);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NameReleasePool(string Id, string Country, string? Region, NamePart Part, int Entries);

/// <summary>Aggregate release report; contains no rejected, unreviewed or blocked record content.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NameReleaseReport
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = "1.0.0";
    public required string PackageId { get; init; }
    public required string Version { get; init; }
    public List<NameReleaseBatch> Batches { get; init; } = [];
    public List<NameReleasePool> Pools { get; init; } = [];
}

public static class NameIdentity
{
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string HashJson<T>(T value) => Hash(JsonSerializer.SerializeToUtf8Bytes(value, ContentJson.Options));
    public static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, ContentJson.Options);
    /// <summary>Stable across releases: depends only on the name string and gender, never on sources or order.</summary>
    public static string EntryKey(string localized, NameGender? gender) =>
        "n_" + Hash(Encoding.UTF8.GetBytes(localized + "\0" + (gender?.ToString() ?? "")))[..16];
}
