using System.Text.Json;
using System.Text.Json.Serialization;

namespace Iryomera.Content.SDK;

/// <summary>Source assertion shapes; these are not instructions or gameplay probabilities.</summary>
public enum MedicalAnnotationKind { DiseasePhenotype, MedicalAction }

/// <summary>An original source identifier and optional explicit resolution to a content definition.</summary>
public sealed record AnnotationReference : ExtensibleRecord
{
    public required string ExternalId { get; init; }
    public string? ContentId { get; init; }
}

/// <summary>Unmodified observational frequency (e.g. a term ID, fraction or percentage); never a game probability.</summary>
public sealed record ObservationFrequency : ExtensibleRecord
{
    public required string SourceValue { get; init; }
    public AnnotationReference? Term { get; init; }
}

/// <summary>
/// A complete sourced medical assertion. Qualifiers and multi-way context remain attached to the assertion.
/// Nothing in this record is implicitly converted to a positive binary relation or clinical recommendation.
/// </summary>
public sealed record MedicalAnnotation : ExtensibleRecord
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required MedicalAnnotationKind Kind { get; init; }
    public required string Predicate { get; init; }
    public required AnnotationReference Subject { get; init; }
    public required AnnotationReference Object { get; init; }
    public AnnotationReference? DiseaseContext { get; init; }
    public AnnotationReference? ExtensionContext { get; init; }
    public bool? IsNegated { get; init; }
    public ObservationFrequency? Frequency { get; init; }
    public AnnotationReference? Onset { get; init; }
    public string? Sex { get; init; }
    public string? Aspect { get; init; }
    public List<AnnotationReference> Modifiers { get; init; } = [];
    public List<string> Qualifiers { get; init; } = [];
    public required Provenance Provenance { get; init; }
    public Dictionary<string, JsonElement> SourceFields { get; init; } = new(StringComparer.Ordinal);
}
