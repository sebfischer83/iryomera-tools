using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.ContentStudio.Data;

namespace Iryomera.ContentStudio.Services;

public sealed class StudioValidationException(IReadOnlyList<ValidationIssue> issues) : Exception(string.Join("\n", issues.Select(i => i.Code + ": " + i.Message)))
{ public IReadOnlyList<ValidationIssue> Issues { get; } = issues; }
public sealed class StudioConflictException() : Exception("The document changed. Reload before saving.");

public sealed class ProjectSnapshot(StudioProject project, ContentPackage package, List<LocalizationDefinition> catalogs)
{
    public StudioProject Project { get; } = project;
    public ContentPackage Package { get; set; } = package;
    public List<LocalizationDefinition> Catalogs { get; } = catalogs;
}
public sealed record SearchResult(int Total, IReadOnlyList<DefinitionIndex> Items);
public sealed record DuplicateCandidate(string LeftId, string RightId, string Text);

public sealed class DefinitionEdit
{
    [Required] public string Id { get; set; } = "";
    [Required] public string Kind { get; set; } = "disease";
    [Required] public string Version { get; set; } = "1.0.0";
    [Required] public string NameEn { get; set; } = "";
    public string NameDe { get; set; } = "";
    public string DescriptionEn { get; set; } = "";
    public string DescriptionDe { get; set; } = "";
    public string SynonymsEn { get; set; } = "";
    public string SynonymsDe { get; set; } = "";
    public string ClassificationsJson { get; set; } = "[]";
    [Required] public string Source { get; set; } = "local:curation";
    [Required] public string ExternalId { get; set; } = "";
    [Required] public string SourceVersion { get; set; } = "1.0.0";
    [Required] public string License { get; set; } = "MIT";
    [Required] public string Actor { get; set; } = "";
    [Required] public string Note { get; set; } = "";
}

public static class StudioContent
{
    public static readonly string[] Kinds = ["disease", "symptom", "finding", "examination", "observation", "treatment", "medicalProcedure", "medicalDepartment", "medicalEquipment", "staffQualification", "room", "furniture", "staffRole", "complication"];
    public static string Json<T>(T value) => JsonSerializer.Serialize(value, ContentJson.Options);
    public static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, ContentJson.Options) ?? throw new InvalidDataException("Expected a JSON value.");
    public static string Kind(ContentDefinition definition) => char.ToLowerInvariant(definition.GetType().Name[0]) + definition.GetType().Name[1..^10];
    public static string Text(IEnumerable<LocalizationDefinition> catalogs, string key, string language) =>
        catalogs.FirstOrDefault(c => c.Language == language)?.Entries.GetValueOrDefault(key) ?? "";
    public static string Name(ProjectSnapshot state, ContentDefinition d, string language) =>
        Text(state.Catalogs, d.NameKey, language) is { Length: > 0 } text ? text : Text(state.Catalogs, d.NameKey, "en") is { Length: > 0 } english ? english : d.Id;
    public static Provenance Draft(Provenance p) => p with { ReviewStatus = ReviewStatus.Unreviewed, ReviewedAt = null, ReviewedBy = null };
    public static IEnumerable<(string Id, Provenance Provenance)> Records(ContentPackage p) =>
        p.Definitions.Select(d => (d.Id, d.Provenance)).Concat(p.Relations.Select(r => (r.Id, r.Provenance))).Concat(p.MedicalAnnotations.Select(a => (a.Id, a.Provenance))).Concat(p.ReferenceRanges.Select(r => (r.Id, r.Provenance))).Concat(p.DiagnosticRecommendations.Select(r => (r.Id, r.Provenance)));
    public static object Record(ContentPackage p, string id) => (object?)p.Definitions.Find(d => d.Id == id) ?? (object?)p.Relations.Find(r => r.Id == id) ?? (object?)p.MedicalAnnotations.Find(a => a.Id == id) ?? (object?)p.ReferenceRanges.Find(r => r.Id == id) ?? p.DiagnosticRecommendations.Find(r => r.Id == id) ?? throw new ArgumentException("Unknown record.");
    public static ContentDefinition NewDefinition(string kind, string id, string nameKey, string version, Provenance provenance) => kind switch
    {
        "disease" => new DiseaseDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "symptom" => new SymptomDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "finding" => new FindingDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "examination" => new ExaminationDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "observation" => new ObservationDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "treatment" => new TreatmentDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "medicalProcedure" => new MedicalProcedureDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "medicalDepartment" => new MedicalDepartmentDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "medicalEquipment" => new MedicalEquipmentDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "staffQualification" => new StaffQualificationDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "room" => new RoomDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "furniture" => new FurnitureDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        "staffRole" => new StaffRoleDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance },
        // New complications start as an extra stay during the stay; the Assets tab changes effect and timing.
        "complication" => new ComplicationDefinition { Id = id, NameKey = nameKey, Version = version, Provenance = provenance, Effect = ComplicationEffect.ExtraStay, Timing = ComplicationTiming.DuringStay },
        _ => throw new ArgumentException("Unknown definition kind.")
    };
}
