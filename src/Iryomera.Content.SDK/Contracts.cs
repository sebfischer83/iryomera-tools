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
[JsonDerivedType(typeof(ComplicationDefinition), "complication")]
[JsonDerivedType(typeof(InsurerDefinition), "insurer")]
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
    /// <summary>Optional, omitted when null; assets resolved by the game catalog and never shipped.</summary>
    public List<CatalogAssetReference>? CatalogAssets { get; init; }
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
/// <summary>A room type. Size and door rules are game layout rules (ADR 0017), not medical statements; all are optional.</summary>
public sealed record RoomDefinition : ContentDefinition
{
    /// <summary>Minimum floor area in grid cells; rooms may have any shape made of cells.</summary>
    public int? MinimumCells { get; init; }
    /// <summary>True when the room needs at least one door; absent means no door rule.</summary>
    public bool? RequiresDoor { get; init; }
    /// <summary>Optional, omitted when null: the clinic role of this room type (ADR 0020); absent means no role.</summary>
    public RoomRole? Role { get; init; }
    /// <summary>True when several departments may use one room of this type; absent means not shared.</summary>
    public bool? Shareable { get; init; }
}
/// <summary>Reception, waiting room or ward (bed room). Beds are ward furniture with a lying point.</summary>
public enum RoomRole { Reception, Waiting, Ward }
/// <summary>Simulation only: how a case reaches the clinic. Walk-in cases come during opening hours; emergency cases only to an open emergency department.</summary>
public enum PatientArrival { WalkIn, Emergency }
public sealed record FurnitureDefinition : ContentDefinition
{
    public FurniturePlacement? Placement { get; init; }
    /// <summary>Optional, omitted when null; absent means fixed.</summary>
    public FurnitureMobility? Mobility { get; init; }
    /// <summary>Optional, omitted when null: patients this item carries (ADR 0016).</summary>
    public FurnitureTransport? Transport { get; init; }
}
public sealed record StaffRoleDefinition : ContentDefinition;
/// <summary>
/// A game complication after a treatment (ADR 0021). Effect and timing are game rules; probabilities and extra time are
/// simulation-only balancing. Not a medical statement.
/// </summary>
public sealed record ComplicationDefinition : ContentDefinition
{
    public required ComplicationEffect Effect { get; init; }
    public required ComplicationTiming Timing { get; init; }
}
/// <summary>What a complication does: extra stay time, a required examination or a follow-up treatment.</summary>
public enum ComplicationEffect { ExtraStay, RequiredExamination, FollowUpTreatment }
/// <summary>When a complication can occur.</summary>
public enum ComplicationTiming { DuringTreatment, DuringStay }
/// <summary>
/// A fictional game insurer (ADR 0022). Contract terms are simulation-only balancing (minimumReputation, priceFactor,
/// patientShare); not an insurance-law statement and not a real organisation.
/// </summary>
public sealed record InsurerDefinition : ContentDefinition;

/// <summary>Directed, typed assertions, never inferred from balancing values.</summary>
public enum RelationKind
{
    DiseaseSymptom, DiseaseFinding, ExaminationFinding, DiseaseExamination,
    DiseaseTreatment, ExaminationEquipment, TreatmentQualification, RoomFurniture, ExaminationObservation, ExaminationRoom, ExaminationQualification, ExaminationPrerequisite,
    TreatmentEquipment, RoomEquipment, ExaminationStaffRole, DiseaseDepartment, FurnitureEquipment, DepartmentRoom, DepartmentPrerequisite,
    TreatmentRoom, TreatmentStaffRole, TreatmentComplication, ComplicationExamination, ComplicationTreatment
}

/// <summary>
/// For roomEquipment and roomFurniture: whether a room type needs the item to count as complete; for departmentRoom: whether
/// a department needs the room type. Absent means optional for roomEquipment and required for roomFurniture and departmentRoom;
/// use RoomContents.EffectiveRequirement.
/// </summary>
public enum RelationRequirement { Required, Optional }

public sealed record ContentRelation : ExtensibleRecord
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required RelationKind Kind { get; init; }
    public required string FromId { get; init; }
    public required string ToId { get; init; }
    public required Provenance Provenance { get; init; }
    /// <summary>Optional, omitted when null; allowed only for roomEquipment, roomFurniture and departmentRoom.</summary>
    public RelationRequirement? Requirement { get; init; }
    /// <summary>Optional, omitted when null; only for diseaseDepartment and diseaseTreatment: 1 is the first choice, higher values are fallbacks.</summary>
    public int? Priority { get; init; }
    /// <summary>Optional, omitted when null; only for departmentRoom: the room type where the department treats patients (ADR 0020).</summary>
    public bool? TreatmentRoom { get; init; }
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
    /// <summary>Simulation only: patients a department or room handles at the same time.</summary>
    public decimal? Capacity { get; init; }
    /// <summary>Simulation only: game currency to buy one item, typically furniture or equipment; not a real price.</summary>
    public decimal? PurchaseCost { get; init; }
    /// <summary>Simulation only: game currency per game hour, typically for a staff role; not a real wage.</summary>
    public decimal? WagePerHour { get; init; }
    /// <summary>Simulation only: game currency to open a department; not a real cost.</summary>
    public decimal? UnlockCost { get; init; }
    /// <summary>Simulation only: discharged patients needed before a department can open; a whole number.</summary>
    public decimal? UnlockDischargedPatients { get; init; }
    /// <summary>Simulation only: first and last game hour (0–24, whole numbers) a department admits new patients and referrals.</summary>
    public decimal? OpenFromHour { get; init; }
    public decimal? OpenToHour { get; init; }
    /// <summary>Simulation only: how a disease's cases arrive.</summary>
    public PatientArrival? Arrival { get; init; }
    /// <summary>Simulation only: inpatient stay after treatment; 0 or absent means outpatient.</summary>
    public decimal? StayDurationSeconds { get; init; }
    /// <summary>Simulation only: probability in [0,1] that a treatment leads to an inpatient stay.</summary>
    public decimal? StayChance { get; init; }
    /// <summary>Simulation only: longest stay; with stayDurationSeconds as the shortest it forms a range.</summary>
    public decimal? StayDurationMaxSeconds { get; init; }
    /// <summary>Simulation only: base probability in [0,1] of a complication, typically on a treatmentComplication relation.</summary>
    public decimal? ComplicationChance { get; init; }
    /// <summary>Simulation only: extra stay a complication with effect extraStay adds.</summary>
    public decimal? ExtraStayDurationSeconds { get; init; }
    /// <summary>Simulation only: comfort of a furniture item, a whole number in [0,10]; absent means 0.</summary>
    public decimal? Comfort { get; init; }
    /// <summary>Simulation only: reputation in [0,100] from which an insurer offers a contract; absent means 0.</summary>
    public decimal? MinimumReputation { get; init; }
    /// <summary>Simulation only: factor above 0 on the income from an insurer's patients; absent means 1.</summary>
    public decimal? PriceFactor { get; init; }
    /// <summary>Simulation only: an insurer's share in [0,1] of all arrivals; absent means 0, the rest are self-payers.</summary>
    public decimal? PatientShare { get; init; }
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
    /// <summary>Optional, omitted when null to keep the legacy wire shape and digests.</summary>
    public List<NamePool>? NamePools { get; init; }
    public List<NameFormat>? NameFormats { get; init; }
    /// <summary>Optional, omitted when null; simulation-only balancing overrides for declared dependencies (ADR 0015).</summary>
    public List<GameplayOverride>? GameplayOverrides { get; init; }
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
