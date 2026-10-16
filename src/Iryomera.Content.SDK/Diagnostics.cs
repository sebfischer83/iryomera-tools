using System.Text.Json.Serialization;

namespace Iryomera.Content.SDK;

public enum ObservationValueKind { Numeric, Categorical, Boolean, Text }
public enum FindingVisibility { InitiallyVisible, AfterExamination, Hidden }
public enum ExaminationRepeatPolicy { UntilPatientStateChanges, AfterInterval }

/// <summary>Definition of a possible result; this is not a measurement on a living/simulated patient.</summary>
public sealed record ObservationResultDefinition : ExtensibleRecord
{
    public required ObservationValueKind ValueKind { get; init; }
    /// <summary>Exact unit code, preferably UCUM; no implicit conversion or invented clinical range.</summary>
    public string? UnitCode { get; init; }
    public List<ObservationCategory> Categories { get; init; } = [];
    public List<string> PatientContextKeys { get; init; } = [];
}
public sealed record ObservationCategory : ExtensibleRecord
{
    public required string Key { get; init; }
    public required string NameKey { get; init; }
    public string? FindingId { get; init; }
}
/// <summary>Conjunction of explicit patient-context predicates; no scripts or clinical inference.</summary>
public sealed record PatientContextCriterion : ExtensibleRecord
{
    public required string FactorKey { get; init; }
    public decimal? Minimum { get; init; }
    public decimal? Maximum { get; init; }
    public string? UnitCode { get; init; }
    public string? Category { get; init; }
    public bool? BooleanValue { get; init; }
}
public sealed record ObservationReferenceRange : ExtensibleRecord
{
    [JsonRequired] public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string ObservationId { get; init; }
    public required string UnitCode { get; init; }
    public decimal? Minimum { get; init; }
    public decimal? Maximum { get; init; }
    public List<PatientContextCriterion> AppliesWhen { get; init; } = [];
    public required Provenance Provenance { get; init; }
}
/// <summary>Optional source-reviewed contextual recommendation, never an executable doctor's checklist.</summary>
public sealed record DiagnosticRecommendation : ExtensibleRecord
{
    [JsonRequired] public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string DiseaseId { get; init; }
    public required string ExaminationId { get; init; }
    public List<string> ContextDefinitionIds { get; init; } = [];
    public List<PatientContextCriterion> AppliesWhen { get; init; } = [];
    public string? RationaleKey { get; init; }
    public required Provenance Provenance { get; init; }
}
/// <summary>Authored game distribution. Not medical evidence, a clinical range or a patient runtime value.</summary>
public sealed record SimulatedObservationDistribution : ExtensibleRecord
{
    [JsonRequired] public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public bool SimulationOnly => true;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string DiseaseId { get; init; }
    public required string ObservationId { get; init; }
    public decimal? NumericMinimum { get; init; }
    public decimal? NumericMaximum { get; init; }
    public Dictionary<string, decimal> CategoryWeights { get; init; } = new(StringComparer.Ordinal);
    public decimal? TrueProbability { get; init; }
    public string? TextKey { get; init; }
    public List<PatientContextCriterion> AppliesWhen { get; init; } = [];
}
public sealed record SimulatedManifestation : ExtensibleRecord
{
    [JsonRequired] public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public bool SimulationOnly => true;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string DiseaseId { get; init; }
    public required string ManifestationId { get; init; }
    public required decimal Probability { get; init; }
    public FindingVisibility Visibility { get; init; } = FindingVisibility.Hidden;
}
public sealed record ExaminationResultPolicy : ExtensibleRecord
{
    [JsonRequired] public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public bool SimulationOnly => true;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string ExaminationId { get; init; }
    public FindingVisibility Visibility { get; init; } = FindingVisibility.AfterExamination;
    public ExaminationRepeatPolicy RepeatPolicy { get; init; } = ExaminationRepeatPolicy.UntilPatientStateChanges;
    public decimal? RepeatIntervalSeconds { get; init; }
}
public sealed record DiagnosticSimulationSettings : ExtensibleRecord
{
    public bool SimulationOnly => true;
    public List<SimulatedObservationDistribution> ObservationDistributions { get; init; } = [];
    public List<SimulatedManifestation> Manifestations { get; init; } = [];
    public List<ExaminationResultPolicy> ExaminationPolicies { get; init; } = [];
}
