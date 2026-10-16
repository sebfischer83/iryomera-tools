using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Iryomera.Content.SDK;

namespace Iryomera.MedicalImporter;

public enum ImportedTermKind { Disease, Finding, Examination, MedicalProcedure, Observation, Symptom, Treatment }
public enum SourceAssertionKind { HpoAssociation, MaxoAnnotation, OntologyHierarchy, CrossReference, WikidataStatement }
public enum DiagnosticSeverity { Information, Warning, Error }
public enum RedistributionDecision { Undecided, Permitted, Blocked }

/// <summary>All source claims are retained verbatim separately from their SDK projection.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportedTerm
{
    public required string Id { get; init; }
    public required string ExternalId { get; init; }
    public required ImportedTermKind Kind { get; init; }
    public required string Label { get; init; }
    public required string Language { get; init; }
    public string? Description { get; init; }
    public List<string> Synonyms { get; init; } = [];
    public List<string> AlternateIds { get; init; } = [];
    public bool Obsolete { get; init; }
    public required Provenance Provenance { get; init; }
    public SortedDictionary<string, ImportedTranslation>? Translations { get; init; }
    public List<ExternalIdentifier>? ExternalIdentifiers { get; init; }
    public SortedDictionary<string, JsonElement> SourceFields { get; init; } = new(StringComparer.Ordinal);
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportedTranslation
{
    public required string Label { get; init; }
    public string? Description { get; init; }
    public List<string> Aliases { get; init; } = [];
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceAssertion
{
    public required string Id { get; init; }
    public required SourceAssertionKind Kind { get; init; }
    public required string Predicate { get; init; }
    public required string SubjectExternalId { get; init; }
    public required string ObjectExternalId { get; init; }
    public string? DiseaseExternalId { get; init; }
    public string? ExtensionExternalId { get; init; }
    public required Provenance Provenance { get; init; }
    public SortedDictionary<string, JsonElement> SourceFields { get; init; } = new(StringComparer.Ordinal);
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportDiagnostic(DiagnosticSeverity Severity, string Code, string RecordId, string Message);

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportOptions
{
    public required string Source { get; init; }
    public required string SourceVersion { get; init; }
    public required string SourceUrl { get; init; }
    public required string License { get; init; }
    public required string LicenseText { get; init; }
    public required string Attribution { get; init; }
    public required DateTimeOffset ImportedAt { get; init; }
    public DateTimeOffset? RetrievedAt { get; init; }
    public string Namespace { get; init; } = "medical";
    public string Format { get; init; } = "auto";
    public long MaxInputBytes { get; init; } = 512 * 1024 * 1024;
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportBatch
{
    public required string Id { get; init; }
    public required string InputSha256 { get; init; }
    public required long InputBytes { get; init; }
    public required ImportOptions Options { get; init; }
    public string AdapterVersion { get; init; } = "1.0.0";
    public required string DetectedFormat { get; init; }
    public string? ObservedSourceVersion { get; set; }
    public SortedDictionary<string, JsonElement> SourceMetadata { get; init; } = new(StringComparer.Ordinal);
    public List<ImportedTerm> Terms { get; init; } = [];
    public List<SourceAssertion> Assertions { get; init; } = [];
    public List<ImportDiagnostic> Diagnostics { get; init; } = [];
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewDecision
{
    public required string BatchId { get; init; }
    public required string RecordId { get; init; }
    public required ReviewStatus Status { get; init; }
    public required string Reviewer { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string Note { get; init; }
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record RightsDecision
{
    public required string BatchId { get; init; }
    /// <summary>Null applies to the batch baseline; a record decision never grants batch rights.</summary>
    public string? RecordId { get; init; }
    public string? PermissionEvidence { get; init; }
    public required RedistributionDecision Decision { get; init; }
    public required string Reviewer { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string Note { get; init; }
}

/// <summary>Versioned hand-off contract for the future editor; review history never mutates raw imports.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportWorkspace
{
    [System.Text.Json.Serialization.JsonRequired]
    public string SchemaVersion { get; init; } = "1.0.0";
    public List<ImportBatch> Batches { get; init; } = [];
    public List<ReviewDecision> Reviews { get; init; } = [];
    public List<RightsDecision> Rights { get; init; } = [];
    public List<BulkReviewAudit> BulkReviews { get; init; } = [];
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ResolvedSourceAssertion
{
    public required SourceAssertion Source { get; init; }
    public List<SourceAssertion> SourceClaims { get; init; } = [];
    public required string SubjectId { get; init; }
    public required string ObjectId { get; init; }
    public string? DiseaseId { get; init; }
    // Extension IDs may be chemicals or other ontologies not modeled as SDK definitions.
    // They remain external, unchanged in Source.ExtensionExternalId.
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceAssertionDocument
{
    [System.Text.Json.Serialization.JsonRequired]
    public string SchemaVersion { get; init; } = "1.0.0";
    public List<ResolvedSourceAssertion> Assertions { get; init; } = [];
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record BulkReviewException(string RecordId, string Reason);

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record BulkReviewAudit
{
    public required string BatchId { get; init; }
    public required ReviewStatus Status { get; init; }
    public required string Reviewer { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string Note { get; init; }
    public List<string> SelectedIds { get; init; } = [];
    public List<BulkReviewException> Exceptions { get; init; } = [];
}

public static class ImportIdentity
{
    public static string ContentId(string ns, string externalId) => ns + ":id_" + Convert.ToHexStringLower(Encoding.UTF8.GetBytes(externalId));
    public static string Key(string id) => id.Replace(':', '.') + ".name";
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string AssertionId(string ns, string source, SourceAssertionKind kind, string subject, string predicate, string target, SortedDictionary<string, JsonElement> fields) =>
        AssertionId(ns, source, kind, subject, predicate, target, fields, null);
    internal static string AssertionId(string ns, string source, SourceAssertionKind kind, string subject, string predicate, string target, SortedDictionary<string, JsonElement> fields, string? legacyNewLine) =>
        ns + ":assertion_" + Hash(JsonSerializer.SerializeToUtf8Bytes(new { source, kind, subject, predicate, target, fields }, legacyNewLine is null ? ContentJson.Options : new(ContentJson.Options) { NewLine = legacyNewLine }));
    public static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, ContentJson.Options);
}
