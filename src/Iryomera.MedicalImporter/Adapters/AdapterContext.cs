using System.Text.Json;
using Iryomera.Content.SDK;

namespace Iryomera.MedicalImporter.Adapters;

public interface IImportAdapter
{
    string SourceId { get; }
    IReadOnlyList<string> Formats { get; }
    Task ReadAsync(Stream input, string format, AdapterContext context, CancellationToken cancellationToken);
}

public sealed class AdapterContext(ImportBatch batch)
{
    private readonly Dictionary<string, ImportedTerm> _terms = new(StringComparer.Ordinal);
    private readonly HashSet<string> _assertions = new(StringComparer.Ordinal);
    public ImportBatch Batch { get; } = batch;
    public ImportOptions Options => Batch.Options;

    public void HeaderComment(string line)
    {
        Batch.SourceMetadata["comment_" + Batch.SourceMetadata.Count] = ImportIdentity.Json(line);
        if (line.StartsWith("#version:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#hpo-version:", StringComparison.OrdinalIgnoreCase))
            Batch.ObservedSourceVersion = line[(line.IndexOf(':') + 1)..].Trim();
    }

    public Provenance Provenance(string externalId, string? reference = null, string? evidence = null) => new()
    {
        Source = Options.SourceUrl, ExternalId = externalId, SourceVersion = Options.SourceVersion,
        License = Options.License, ImportedAt = Options.ImportedAt,
        Evidence = string.IsNullOrWhiteSpace(reference) ? [] : [new() { Citation = reference, EvidenceCode = evidence }]
    };

    public void Diagnostic(DiagnosticSeverity severity, string code, string id, string message) =>
        Batch.Diagnostics.Add(new(severity, code, id, message));

    public void Term(string externalId, string label, ImportedTermKind kind, SortedDictionary<string, JsonElement> fields,
        string language = "en", string? description = null, IEnumerable<string>? synonyms = null, IEnumerable<string>? alternateIds = null, bool obsolete = false)
    {
        if (string.IsNullOrWhiteSpace(externalId) || externalId.Any(char.IsWhiteSpace) || externalId.IndexOf(':') <= 0 || externalId.EndsWith(':') || string.IsNullOrWhiteSpace(label))
        { Diagnostic(DiagnosticSeverity.Error, "term-required", externalId, "Term identifier and label are required."); return; }
        var term = new ImportedTerm
        {
            Id = ImportIdentity.ContentId(Options.Namespace, externalId), ExternalId = externalId, Label = label,
            Kind = kind, Language = language, Description = description, Provenance = Provenance(externalId), SourceFields = fields,
            Synonyms = (synonyms ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            AlternateIds = (alternateIds ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(), Obsolete = obsolete
        };
        if (_terms.TryGetValue(term.Id, out var previous))
        {
            // Annotation files may repeat the same disease label in many source assertions.
            if (previous.Label != label || previous.Kind != kind)
                Diagnostic(DiagnosticSeverity.Error, "conflicting-term", term.Id, "Repeated external ID has differing labels or types.");
            else Diagnostic(DiagnosticSeverity.Information, "repeated-term", term.Id, "Repeated term retained once; source assertion rows retain their own lineage.");
            return;
        }
        _terms.Add(term.Id, term);
        Batch.Terms.Add(term);
        if (obsolete) Diagnostic(DiagnosticSeverity.Warning, "obsolete-term", term.Id, "Obsolete source term is staged but cannot be approved for release.");
    }

    public void Assertion(SourceAssertionKind kind, string subject, string predicate, string target,
        SortedDictionary<string, JsonElement> fields, string? disease = null, string? extension = null, string? reference = null, string? evidence = null)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(target))
        { Diagnostic(DiagnosticSeverity.Error, "assertion-required", subject, "Assertion subject, predicate and object are required."); return; }
        var id = ImportIdentity.AssertionId(Options.Namespace, Options.Source, kind, subject, predicate, target, fields);
        if (!_assertions.Add(id))
        { Diagnostic(DiagnosticSeverity.Warning, "duplicate-assertion", id, "Identical source assertion repeated; retained once."); return; }
        Batch.Assertions.Add(new()
        {
            Id = id, Kind = kind, SubjectExternalId = subject, Predicate = predicate, ObjectExternalId = target,
            DiseaseExternalId = string.IsNullOrWhiteSpace(disease) ? null : disease,
            ExtensionExternalId = string.IsNullOrWhiteSpace(extension) ? null : extension,
            Provenance = Provenance(id, reference, evidence), SourceFields = fields
        });
    }
}
