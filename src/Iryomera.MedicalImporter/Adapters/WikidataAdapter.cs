using System.Text.Json;
using System.Text.RegularExpressions;
using Iryomera.Content.SDK;

namespace Iryomera.MedicalImporter.Adapters;

/// <summary>Offline SELECT-result adapter; saved query results are claims, never medical approval.</summary>
public sealed class WikidataAdapter : IImportAdapter
{
    public const string Version = "1.2.0";
    public string SourceId => "wikidata";
    public IReadOnlyList<string> Formats => ["json", "csv"];
    private static readonly HashSet<string> Properties = ["P780", "P923", "P924", "P828"];

    public async Task ReadAsync(Stream input, string format, AdapterContext context, CancellationToken cancellationToken)
    {
        if (context.Options.License != "CC0-1.0") throw new InvalidDataException("Wikidata structured data uses CC0-1.0; supply the actual CC0 terms.");
        var rows = new List<SortedDictionary<string, JsonElement>>();
        if (format == "json")
        {
            using var document = await JsonDocument.ParseAsync(input, new JsonDocumentOptions { AllowDuplicateProperties = false }, cancellationToken);
            if (!document.RootElement.TryGetProperty("head", out var head) || !head.TryGetProperty("vars", out var vars) ||
                !vars.EnumerateArray().Any(v => v.GetString() == "item") || !document.RootElement.TryGetProperty("results", out var results) || !results.TryGetProperty("bindings", out var bindings) || bindings.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Expected SPARQL SELECT JSON head.vars and results.bindings with item; arbitrary entity JSON is not supported.");
            context.Batch.SourceMetadata["sparqlHead"] = head.Clone();
            foreach (var row in bindings.EnumerateArray())
            {
                var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var binding in row.EnumerateObject())
                {
                    if (binding.Value.ValueKind != JsonValueKind.Object || !binding.Value.TryGetProperty("type", out _) || !binding.Value.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("SPARQL bindings must retain type/value objects.");
                    fields.Add(binding.Name, binding.Value.Clone());
                }
                rows.Add(fields);
            }
        }
        else if (format == "csv")
            await foreach (var row in DelimitedRecords.ReadAsync(input, ",", ["item"], cancellationToken)) rows.Add(row);
        else throw new InvalidDataException("Wikidata supports saved SPARQL JSON or CSV only.");
        cancellationToken.ThrowIfCancellationRequested();
        if (rows.Count == 0) throw new InvalidDataException("No Wikidata result rows.");
        context.Batch.SourceMetadata["license"] = ImportIdentity.Json("CC0-1.0");
        context.Batch.SourceMetadata["retrievedAt"] = ImportIdentity.Json(context.Options.RetrievedAt);
        context.Batch.SourceMetadata["profile"] = ImportIdentity.Json("iryomera-wikidata-select-v1");
        var grouped = rows.GroupBy(r => Q(Text(r, "item"))).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        foreach (var group in grouped)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal) { ["rows"] = ImportIdentity.Json(EntityRows(group, "item")) };
            var translations = Translations(group, "item");
            if (translations.Count == 0) { context.Diagnostic(DiagnosticSeverity.Error, "wikidata-label-missing", group.Key, "No actual English or German disease label supplied; no label invented."); continue; }
            var primary = translations.GetValueOrDefault("en") ?? translations.First().Value;
            var external = "WIKIDATA:" + group.Key;
            context.Term(external, primary.Label, ImportedTermKind.Disease, fields, translations.ContainsKey("en") ? "en" : "de", primary.Description, primary.Aliases);
            var term = context.Batch.Terms.Find(t => t.ExternalId == external)!;
            context.Batch.Terms[context.Batch.Terms.IndexOf(term)] = term with { Translations = translations, ExternalIdentifiers = Identifiers(group, context, external) };
        }
        var targets = rows.Where(r => Text(r, "property").Length > 0 && Properties.Contains(Property(Text(r, "property"))) && TryQ(Text(r, "value"), out _))
            .GroupBy(r => Q(Text(r, "value"))).OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var external = "WIKIDATA:" + target.Key;
            if (context.Batch.Terms.Any(t => t.ExternalId == external)) continue;
            var types = target.Select(r => TargetKind(r)).Where(k => k.HasValue).Select(k => k!.Value).Distinct().ToArray();
            var translations = Translations(target, "value");
            if (types.Length != 1 || translations.Count == 0)
            { context.Diagnostic(DiagnosticSeverity.Warning, "wikidata-target-untyped", external, "Target has no unique explicit role/label; kept as an unresolved source reference."); continue; }
            var primary = translations.GetValueOrDefault("en") ?? translations.First().Value;
            context.Term(external, primary.Label, types[0], new(StringComparer.Ordinal) { ["rows"] = ImportIdentity.Json(EntityRows(target, "value")) }, translations.ContainsKey("en") ? "en" : "de", primary.Description, primary.Aliases);
            var term = context.Batch.Terms.Find(t => t.ExternalId == external)!;
            context.Batch.Terms[context.Batch.Terms.IndexOf(term)] = term with { Translations = translations, ExternalIdentifiers = Identifiers(target, context, external, "value") };
        }
        var assertions = rows.Where(r => !string.IsNullOrWhiteSpace(Text(r, "property"))).GroupBy(r =>
        {
            var property = Property(Text(r, "property"));
            if (!Properties.Contains(property)) throw new InvalidDataException("Unsupported medical statement property: " + property);
            var statement = Text(r, "statement");
            return (Subject: "WIKIDATA:" + Q(Text(r, "item")), Property: property, Statement: statement,
                Target: TryQ(Text(r, "value"), out var value) ? "WIKIDATA:" + value : "WIKIDATA_VALUE:" + ImportIdentity.Hash(r.TryGetValue("value", out var binding)
                    ? JsonSerializer.SerializeToUtf8Bytes(binding, ContentJson.Options) : "unbound"u8.ToArray()));
        }).OrderBy(g => g.Key.Subject + "/" + g.Key.Property + "/" + g.Key.Statement + "/" + g.Key.Target, StringComparer.Ordinal);
        var statementIdentities = new Dictionary<string, (string Subject, string Property, string Target)>(StringComparer.Ordinal);
        foreach (var group in assertions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(group.Key.Statement))
            {
                var identity = (group.Key.Subject, group.Key.Property, group.Key.Target);
                if (statementIdentities.TryGetValue(group.Key.Statement, out var previous) && previous != identity)
                    throw new InvalidDataException("One Wikidata statement ID cannot have conflicting subjects, properties or values.");
                statementIdentities[group.Key.Statement] = identity;
            }
            var ranks = group.Select(r => Rank(Text(r, "rank"))).Distinct(StringComparer.Ordinal).ToArray();
            if (ranks.Length != 1) throw new InvalidDataException("One statement cannot have conflicting ranks.");
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
            { ["statementId"] = ImportIdentity.Json(group.Key.Statement), ["propertyId"] = ImportIdentity.Json(group.Key.Property), ["rank"] = ImportIdentity.Json(ranks[0]),
                ["rows"] = ImportIdentity.Json(CanonicalRows(group)),
                ["references"] = ImportIdentity.Json(CanonicalRows(group.Where(r => Text(r, "reference").Length > 0 || Text(r, "referenceValue").Length > 0))),
                ["qualifiers"] = ImportIdentity.Json(CanonicalRows(group.Where(r => Text(r, "qualifierProperty").Length > 0 || Text(r, "qualifierValue").Length > 0))) };
            context.Assertion(SourceAssertionKind.WikidataStatement, group.Key.Subject, group.Key.Property, group.Key.Target, fields);
            var last = context.Batch.Assertions[^1];
            context.Batch.Assertions[^1] = last with { Provenance = last.Provenance with { ExternalId = string.IsNullOrWhiteSpace(group.Key.Statement) ? last.Id : group.Key.Statement } };
            if (ReviewConcern(last) is { } concern) context.Diagnostic(DiagnosticSeverity.Warning, concern, last.Id, "Wikidata metadata is incomplete, unreferenced or deprecated; explicit medical review is required.");
        }
    }

    internal static string Text(SortedDictionary<string, JsonElement> row, string name) => !row.TryGetValue(name, out var value) ? "" : value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetProperty("value").GetString() ?? "";
    private static bool TryQ(string text, out string q)
    { q = text.StartsWith("http://www.wikidata.org/entity/", StringComparison.Ordinal) || text.StartsWith("https://www.wikidata.org/entity/", StringComparison.Ordinal) ? text[(text.LastIndexOf('/') + 1)..] : text; return Regex.IsMatch(q, "^Q[1-9][0-9]*\\z", RegexOptions.CultureInvariant); }
    private static string Q(string text) => TryQ(text, out var q) ? q : throw new InvalidDataException("Expected an actual Wikidata Q ID/entity URI, received: " + text);
    private static string Property(string text)
    {
        var p = text;
        if (text.Contains('/'))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Host != "www.wikidata.org" || uri.Scheme is not ("http" or "https") || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                !(uri.AbsolutePath.StartsWith("/entity/P", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/prop/P", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/prop/direct/P", StringComparison.Ordinal)))
                throw new InvalidDataException("Expected a canonical Wikidata property URI.");
            p = text[(text.LastIndexOf('/') + 1)..];
        }
        return Regex.IsMatch(p, "^P[1-9][0-9]*\\z") ? p : throw new InvalidDataException("Expected a Wikidata property ID.");
    }
    private static string Rank(string text) => text switch
    { "http://wikiba.se/ontology#NormalRank" or "normal" => "normal", "http://wikiba.se/ontology#PreferredRank" or "preferred" => "preferred", "http://wikiba.se/ontology#DeprecatedRank" or "deprecated" => "deprecated", "" => "unknown", _ => throw new InvalidDataException("Unknown Wikidata rank.") };
    private static SortedDictionary<string, JsonElement>[] CanonicalRows(IEnumerable<SortedDictionary<string, JsonElement>> rows) => rows.DistinctBy(r => JsonSerializer.Serialize(r, ContentJson.Options)).OrderBy(r => JsonSerializer.Serialize(r, ContentJson.Options), StringComparer.Ordinal).ToArray();
    // Entity approval must not smuggle an unapproved statement through its sourceClaims.
    // Full statement rows, including extra query columns, remain on the separately reviewed assertion.
    private static SortedDictionary<string, JsonElement>[] EntityRows(IEnumerable<SortedDictionary<string, JsonElement>> rows, string prefix) => CanonicalRows(rows.Select(row =>
        new SortedDictionary<string, JsonElement>(row.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) ||
            (prefix == "item" && p.Key is "hpoId" or "icd10" or "icd9" or "icd10cm" or "icd11Foundation" or "externalSystem" or "externalId"))
            .ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal)));
    private static ImportedTermKind? TargetKind(SortedDictionary<string, JsonElement> row) => Text(row, "valueKind") switch
    { "symptom" => ImportedTermKind.Symptom, "finding" => ImportedTermKind.Finding, "examination" => ImportedTermKind.Examination, "treatment" => ImportedTermKind.Treatment, "medicalProcedure" => ImportedTermKind.MedicalProcedure,
        "" => Property(Text(row, "property")) switch { "P923" => ImportedTermKind.Examination, "P924" => ImportedTermKind.Treatment, _ => null }, _ => throw new InvalidDataException("Unsupported explicit valueKind.") };
    private static SortedDictionary<string, ImportedTranslation> Translations(IEnumerable<SortedDictionary<string, JsonElement>> rows, string prefix)
    {
        var result = new SortedDictionary<string, ImportedTranslation>(StringComparer.Ordinal);
        foreach (var language in new[] { "en", "de" })
        {
            var suffix = language == "en" ? "En" : "De";
            var labels = rows.Select(r => Text(r, prefix + "Label" + suffix)).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToArray();
            var descriptions = rows.Select(r => Text(r, prefix + "Description" + suffix)).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToArray();
            if (labels.Length > 1 || descriptions.Length > 1) throw new InvalidDataException("Conflicting labels/descriptions for one Q ID and language.");
            if (labels.Length == 0) continue;
            result[language] = new() { Label = labels[0], Description = descriptions.SingleOrDefault(), Aliases = rows.Select(r => Text(r, prefix + "Alias" + suffix)).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList() };
        }
        return result;
    }
    private static List<ExternalIdentifier> Identifiers(IEnumerable<SortedDictionary<string, JsonElement>> rows, AdapterContext context, string external, string prefix = "")
    {
        string Column(string name) => prefix.Length == 0 ? name : prefix + char.ToUpperInvariant(name[0]) + name[1..];
        var mappings = new[] { ("hpoId", "HP", "P3841"), ("icd10", "ICD10", "P494"), ("icd9", "ICD9", "P493"), ("icd10cm", "ICD10CM", "P4229"), ("icd11Foundation", "ICD11Foundation", "P7807") };
        var identifiers = new List<ExternalIdentifier>();
        foreach (var row in rows)
        {
            foreach (var (column, system, property) in mappings)
                if (Text(row, Column(column)) is { Length: > 0 } value) identifiers.Add(new() { System = system, Value = value, Provenance = context.Provenance(external), Extensions = new() { ["wikidataPropertyId"] = ImportIdentity.Json(property) } });
            if (Text(row, Column("externalSystem")) is { Length: > 0 } explicitSystem && Text(row, Column("externalId")) is { Length: > 0 } id)
                identifiers.Add(new() { System = explicitSystem, Value = id, Provenance = context.Provenance(external) });
        }
        return identifiers.DistinctBy(i => i.System + "\0" + i.Value).OrderBy(i => i.System + "\0" + i.Value, StringComparer.Ordinal).ToList();
    }
    public static bool Deprecated(SourceAssertion assertion) => assertion.SourceFields.TryGetValue("rank", out var value) && value.GetString() == "deprecated";
    public static string? ReviewConcern(SourceAssertion assertion)
    {
        if (!assertion.ObjectExternalId.StartsWith("WIKIDATA:", StringComparison.Ordinal)) return "wikidata-incomplete-statement";
        if (Deprecated(assertion)) return "wikidata-deprecated";
        if (!assertion.SourceFields.TryGetValue("statementId", out var statement) || string.IsNullOrWhiteSpace(statement.GetString()) || !assertion.SourceFields.TryGetValue("rank", out var rank) || rank.GetString() == "unknown") return "wikidata-incomplete-statement";
        if (!assertion.SourceFields.TryGetValue("references", out var references) || !references.EnumerateArray().Any(row =>
            !string.IsNullOrWhiteSpace(Text(new(row.EnumerateObject().ToDictionary(p => p.Name, p => p.Value), StringComparer.Ordinal), "referenceProperty")) &&
            !string.IsNullOrWhiteSpace(Text(new(row.EnumerateObject().ToDictionary(p => p.Name, p => p.Value), StringComparer.Ordinal), "referenceValue")))) return "wikidata-unreferenced-statement";
        return null;
    }
}
