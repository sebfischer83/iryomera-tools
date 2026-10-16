using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Iryomera.Content.SDK;

/// <summary>Version constants for portable v1 JSON documents.</summary>
public static class ContentFormat
{
    public const string SchemaVersion = "1.0.0";
    public const string IdPattern = "^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*:[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*\\z";
    public const string NamespacePattern = "^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*\\z";
    public const string LocalizationKeyPattern = "^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)+\\z";
    public static bool IsValidNamespace(string? value) => value is not null && Regex.IsMatch(value, NamespacePattern, RegexOptions.CultureInvariant);
    public static bool IsValidLocalizationKey(string? value) => value is not null && Regex.IsMatch(value, LocalizationKeyPattern, RegexOptions.CultureInvariant);
    public static bool IsValidId(string? value) => value is not null && Regex.IsMatch(value, IdPattern, RegexOptions.CultureInvariant);
}

/// <summary>Unknown additive properties survive load/edit/save, including in nested metadata.</summary>
public abstract record ExtensibleRecord
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public enum ReviewStatus { Unreviewed, InReview, Approved, Rejected }

/// <summary>Per-information lineage; imported data keeps its original license and review state.</summary>
public sealed record Provenance : ExtensibleRecord
{
    public required string Source { get; init; }
    public required string ExternalId { get; init; }
    public required string SourceVersion { get; init; }
    public required string License { get; init; }
    public required DateTimeOffset ImportedAt { get; init; }
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Unreviewed;
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public List<EvidenceReference> Evidence { get; init; } = [];
}

public sealed record EvidenceReference : ExtensibleRecord
{
    public required string Citation { get; init; }
    public string? Uri { get; init; }
    public string? EvidenceCode { get; init; }
}

public sealed record ExternalIdentifier : ExtensibleRecord
{
    public required string System { get; init; }
    public required string Value { get; init; }
    public required Provenance Provenance { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DiseaseDefinition), "disease")]
[JsonDerivedType(typeof(SymptomDefinition), "symptom")]
[JsonDerivedType(typeof(FindingDefinition), "finding")]
[JsonDerivedType(typeof(ExaminationDefinition), "examination")]
[JsonDerivedType(typeof(ObservationDefinition), "observation")]
[JsonDerivedType(typeof(TreatmentDefinition), "treatment")]
[JsonDerivedType(typeof(MedicalProcedureDefinition), "medicalProcedure")]
[JsonDerivedType(typeof(MedicalDepartmentDefinition), "medicalDepartment")]
[JsonDerivedType(typeof(MedicalEquipmentDefinition), "medicalEquipment")]
[JsonDerivedType(typeof(StaffQualificationDefinition), "staffQualification")]
[JsonDerivedType(typeof(RoomDefinition), "room")]
[JsonDerivedType(typeof(FurnitureDefinition), "furniture")]
[JsonDerivedType(typeof(StaffRoleDefinition), "staffRole")]
public abstract record ContentDefinition : ExtensibleRecord
{
    public required string Id { get; init; }
    public required string NameKey { get; init; }
    public string? DescriptionKey { get; init; }
    public required string Version { get; init; }
    public required Provenance Provenance { get; init; }
    public List<ExternalIdentifier> Classifications { get; init; } = [];
    public List<string> SynonymKeys { get; init; } = [];
    public List<AssetReference>? Assets { get; init; }
}

public sealed record DiseaseDefinition : ContentDefinition;
public sealed record SymptomDefinition : ContentDefinition;
public sealed record FindingDefinition : ContentDefinition;
public sealed record ExaminationDefinition : ContentDefinition;
/// <summary>An observation/measurement or panel code; does not declare a playable examination.</summary>
public sealed record ObservationDefinition : ContentDefinition
{
    public ObservationResultDefinition? Result { get; init; }
}
public sealed record TreatmentDefinition : ContentDefinition;
public sealed record MedicalProcedureDefinition : ContentDefinition;
public sealed record MedicalDepartmentDefinition : ContentDefinition;
public sealed record MedicalEquipmentDefinition : ContentDefinition;
public sealed record StaffQualificationDefinition : ContentDefinition;
public sealed record RoomDefinition : ContentDefinition;
public sealed record FurnitureDefinition : ContentDefinition
{
    public FurniturePlacement? Placement { get; init; }
}
public sealed record StaffRoleDefinition : ContentDefinition;

/// <summary>Directed, typed assertions, never inferred from balancing values.</summary>
public enum RelationKind
{
    DiseaseSymptom, DiseaseFinding, ExaminationFinding, DiseaseExamination,
    DiseaseTreatment, ExaminationEquipment, TreatmentQualification, RoomFurniture, ExaminationObservation, ExaminationRoom, ExaminationQualification, ExaminationPrerequisite
}

public sealed record ContentRelation : ExtensibleRecord
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required RelationKind Kind { get; init; }
    public required string FromId { get; init; }
    public required string ToId { get; init; }
    public required Provenance Provenance { get; init; }
}

/// <summary>Simulation only: none of these values expresses clinical evidence or advice.</summary>
public sealed record GameplayBalance : ExtensibleRecord
{
    public required string DefinitionId { get; init; }
    public decimal? Rarity { get; init; }
    public decimal? Severity { get; init; }
    public decimal? TreatmentDurationSeconds { get; init; }
    public decimal? ExaminationDurationSeconds { get; init; }
    public decimal? ExaminationCost { get; init; }
    public decimal? TreatmentCost { get; init; }
    public decimal? DiagnosticDifficulty { get; init; }
    public decimal? SimulatedSuccessProbability { get; init; }
    public Dictionary<string, decimal> RequiredResources { get; init; } = [];
}

public sealed record ContentDependency : ExtensibleRecord
{
    public required string PackageId { get; init; }
    public required string VersionRange { get; init; }
}

public sealed record ContentPackageManifest : ExtensibleRecord
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public required string Id { get; init; }
    public required string Namespace { get; init; }
    public required string Version { get; init; }
    public required string License { get; init; }
    public List<ContentDependency> Dependencies { get; init; } = [];
}

/// <summary>Phase 1 data contract only; package loading/installation is planned in ModKit.</summary>
public sealed record ModManifest : ExtensibleRecord
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public required string Id { get; init; }
    public required string Namespace { get; init; }
    public required string NameKey { get; init; }
    public required string Author { get; init; }
    public required string Version { get; init; }
    public required string CompatibleGameVersions { get; init; }
    public required string License { get; init; }
    public List<ContentDependency> Dependencies { get; init; } = [];
    public List<string> ContentFiles { get; init; } = [];
    public List<string> SupportedLanguages { get; init; } = [];
}

/// <summary>A package is the atomic content exchange unit; dependencies are explicit inputs.</summary>
public sealed record ContentPackage : ExtensibleRecord
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public required ContentPackageManifest Manifest { get; init; }
    public List<ContentDefinition> Definitions { get; init; } = [];
    public List<ContentRelation> Relations { get; init; } = [];
    public List<MedicalAnnotation> MedicalAnnotations { get; init; } = [];
    public List<ObservationReferenceRange> ReferenceRanges { get; init; } = [];
    public List<DiagnosticRecommendation> DiagnosticRecommendations { get; init; } = [];
    public DiagnosticSimulationSettings? DiagnosticSimulation { get; init; }
    public List<GameplayBalance> Gameplay { get; init; } = [];
}

/// <summary>A separately stored language catalog, owned by a package namespace.</summary>
public sealed record LocalizationDefinition : ExtensibleRecord
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public required string Namespace { get; init; }
    public required string Language { get; init; }
    public Dictionary<string, string> Entries { get; init; } = [];
}
