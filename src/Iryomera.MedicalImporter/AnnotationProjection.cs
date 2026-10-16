using Iryomera.Content.SDK;

namespace Iryomera.MedicalImporter;

internal static class AnnotationProjection
{
    internal static MedicalAnnotation Create(SourceAssertion source, Provenance provenance, Dictionary<string, string> resolved, List<SourceAssertion> claims)
    {
        string Text(string name) => source.SourceFields.TryGetValue(name, out var field) && field.ValueKind == System.Text.Json.JsonValueKind.String ? field.GetString() ?? "" : "";
        AnnotationReference Reference(string external) => new() { ExternalId = external, ContentId = resolved.GetValueOrDefault(external) };
        var qualifiers = Text("qualifier").Split([';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var frequency = Text("frequency");
        return new()
        {
            Id = source.Id + ".annotation", Version = "1.0.0",
            Kind = source.Kind == SourceAssertionKind.HpoAssociation ? MedicalAnnotationKind.DiseasePhenotype : MedicalAnnotationKind.MedicalAction,
            Predicate = source.Predicate, Subject = Reference(source.SubjectExternalId), Object = Reference(source.ObjectExternalId),
            DiseaseContext = source.DiseaseExternalId is null ? null : Reference(source.DiseaseExternalId),
            ExtensionContext = source.ExtensionExternalId is null ? null : Reference(source.ExtensionExternalId),
            IsNegated = qualifiers.Contains("NOT", StringComparer.Ordinal) ? true : null,
            Frequency = frequency.Length == 0 ? null : new() { SourceValue = frequency, Term = frequency.StartsWith("HP:", StringComparison.Ordinal) ? Reference(frequency) : null },
            Onset = Text("onset") is { Length: > 0 } onset ? Reference(onset) : null,
            Sex = Text("sex") is { Length: > 0 } sex ? sex : null, Aspect = Text("aspect") is { Length: > 0 } aspect ? aspect : null,
            Qualifiers = qualifiers.ToList(), Modifiers = Text("modifier").Split([';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Reference).ToList(),
            Provenance = provenance, SourceFields = new(source.SourceFields, StringComparer.Ordinal),
            Extensions = new() { ["sourceClaims"] = ImportIdentity.Json(claims) }
        };
    }
}
