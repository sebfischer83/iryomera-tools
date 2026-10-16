using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;

namespace Iryomera.MedicalImporter.Adapters;

internal static class DelimitedRecords
{
    internal static async IAsyncEnumerable<SortedDictionary<string, JsonElement>> ReadAsync(Stream input, string delimiter,
        IReadOnlyList<string> requiredHeaders, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, Action<string>? comment = null)
    {
        using var reader = new StreamReader(input, leaveOpen: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter, HasHeaderRecord = false, IgnoreBlankLines = true, ExceptionMessagesContainRawData = false
        });
        string[]? headers = null;
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            var row = csv.Parser.Record!;
            if (headers is null)
            {
                if (row[0].StartsWith('#') && !row[0].StartsWith("#database_id", StringComparison.OrdinalIgnoreCase)) { comment?.Invoke(string.Join(delimiter, row)); continue; }
                headers = row.Select((x, i) => i == 0 ? x.TrimStart('#', '\uFEFF') : x).ToArray();
                if (headers.Distinct(StringComparer.Ordinal).Count() != headers.Length || headers.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException("Delimited input has duplicate or empty header names.");
                foreach (var expected in requiredHeaders)
                    if (!headers.Contains(expected, StringComparer.Ordinal)) throw new InvalidDataException($"Missing required source column {expected}.");
                continue;
            }
            if (row.Length == 1 && row[0].StartsWith('#')) { comment?.Invoke(row[0]); continue; }
            if (row.Length != headers.Length) throw new InvalidDataException($"Record {csv.Parser.Row} has {row.Length} fields; expected {headers.Length}.");
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
            for (var i = 0; i < row.Length; i++) fields[headers[i]] = ImportIdentity.Json(row[i]);
            yield return fields;
        }
        if (headers is null) throw new InvalidDataException("Delimited input has no header.");
    }

    internal static string Text(this SortedDictionary<string, JsonElement> fields, string key) => fields.TryGetValue(key, out var value) ? value.GetString() ?? "" : "";
}

public sealed class HpoAnnotationAdapter : IImportAdapter
{
    public string SourceId => "hpo-annotations";
    public IReadOnlyList<string> Formats => ["tsv"];
    public async Task ReadAsync(Stream input, string format, AdapterContext context, CancellationToken cancellationToken)
    {
        if (format != "tsv") throw new InvalidDataException("HPO annotations require phenotype.hpoa TSV.");
        string[] required = ["database_id", "disease_name", "qualifier", "hpo_id", "reference", "evidence", "onset", "frequency", "sex", "modifier", "aspect", "biocuration"];
        await foreach (var fields in DelimitedRecords.ReadAsync(input, "\t", required, cancellationToken, context.HeaderComment))
        {
            var disease = fields.Text("database_id"); var phenotype = fields.Text("hpo_id");
            if (string.IsNullOrWhiteSpace(fields.Text("reference")) || string.IsNullOrWhiteSpace(fields.Text("evidence")) || string.IsNullOrWhiteSpace(fields.Text("biocuration")))
                context.Diagnostic(DiagnosticSeverity.Error, "annotation-metadata", disease, "HPOA reference, evidence and biocuration are required.");
            context.Term(disease, fields.Text("disease_name"), ImportedTermKind.Disease,
                new(StringComparer.Ordinal) { ["database_id"] = fields["database_id"], ["disease_name"] = fields["disease_name"] });
            context.Assertion(SourceAssertionKind.HpoAssociation, disease, "has_phenotype", phenotype, fields, reference: fields.Text("reference"), evidence: fields.Text("evidence"));
            if (fields.Text("qualifier").Length > 0 || fields.Text("aspect") != "P")
                context.Diagnostic(DiagnosticSeverity.Warning, "qualified-association", disease, "Qualifier/aspect retained; no positive clinical relation will be inferred.");
            if (fields.Text("evidence") == "IEA")
                context.Diagnostic(DiagnosticSeverity.Warning, "upstream-rights", disease, "HPOA IEA records may require separate OMIM permission; review source-specific rights before redistribution.");
        }
    }
}

public sealed class MaxoAnnotationAdapter : IImportAdapter
{
    public string SourceId => "maxo-annotations";
    public IReadOnlyList<string> Formats => ["tsv"];
    public async Task ReadAsync(Stream input, string format, AdapterContext context, CancellationToken cancellationToken)
    {
        if (format != "tsv") throw new InvalidDataException("MAxO annotations require TSV.");
        string[] required = ["disease_id", "disease_name", "source_id", "maxo_id", "maxo_name", "hpo_id", "relation", "evidence", "extension_id", "extension_name", "comment", "other", "author", "last_updated", "created"];
        await foreach (var fields in DelimitedRecords.ReadAsync(input, "\t", required, cancellationToken, context.HeaderComment))
        {
            var disease = fields.Text("disease_id");
            if (disease.Length > 0)
                context.Term(disease, fields.Text("disease_name"), ImportedTermKind.Disease,
                    new(StringComparer.Ordinal) { ["disease_id"] = fields["disease_id"], ["disease_name"] = fields["disease_name"] });
            if (string.IsNullOrWhiteSpace(fields.Text("source_id")) || string.IsNullOrWhiteSpace(fields.Text("evidence")))
                context.Diagnostic(DiagnosticSeverity.Error, "annotation-metadata", disease, "MAxO annotation source reference and evidence are required.");
            context.Assertion(SourceAssertionKind.MaxoAnnotation, fields.Text("maxo_id"), fields.Text("relation"), fields.Text("hpo_id"), fields,
                disease, fields.Text("extension_id"), fields.Text("source_id"), fields.Text("evidence"));
        }
    }
}

public sealed class LoincAdapter : IImportAdapter
{
    public string SourceId => "loinc";
    public IReadOnlyList<string> Formats => ["csv"];
    public async Task ReadAsync(Stream input, string format, AdapterContext context, CancellationToken cancellationToken)
    {
        if (format != "csv") throw new InvalidDataException("LOINC requires the official comma-separated Loinc.csv table.");
        string[] required = ["LOINC_NUM", "LONG_COMMON_NAME", "STATUS"];
        await foreach (var fields in DelimitedRecords.ReadAsync(input, ",", required, cancellationToken, context.HeaderComment))
        {
            var status = fields.Text("STATUS");
            if (string.IsNullOrWhiteSpace(fields.Text("LOINC_NUM")) || string.IsNullOrWhiteSpace(status)) throw new InvalidDataException("LOINC_NUM and STATUS must be nonempty.");
            context.Term("LOINC:" + fields.Text("LOINC_NUM"), fields.Text("LONG_COMMON_NAME"), ImportedTermKind.Observation,
                fields, description: fields.Text("DefinitionDescription") is { Length: > 0 } description ? description : null,
                obsolete: status == "DEPRECATED");
            if (status != "ACTIVE") context.Diagnostic(DiagnosticSeverity.Warning, "loinc-status", fields.Text("LOINC_NUM"), $"Original term status is {status}; retained unchanged.");
        }
    }
}
