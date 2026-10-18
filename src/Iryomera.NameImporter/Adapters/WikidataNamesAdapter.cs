using System.Text.Json;
using System.Text.RegularExpressions;
using Iryomera.Content.SDK;

namespace Iryomera.NameImporter.Adapters;

/// <summary>
/// Saved Wikidata SPARQL SELECT JSON for name items (query profile examples/wikidata/names.rq). Required bindings: item,
/// type, statement, rank and name (the P1705 native label with its language tag); optional romanized. Countries come only
/// from the operator's explicit language-to-country mapping; unmapped languages are not staged. Name items carry no
/// frequency, so no rank, count or proportion is staged. Deprecated statements are not staged. All bindings stay in
/// the source fields with their Q/P/statement identities. CC0 is required; it is not an approval.
/// </summary>
public sealed partial class WikidataNamesAdapter : INameAdapter
{
    private static readonly Dictionary<string, (NamePart Part, NameGender? Gender)> Types = new(StringComparer.Ordinal)
    {
        ["Q202444"] = (NamePart.Given, NameGender.Unknown), ["Q11879590"] = (NamePart.Given, NameGender.Female),
        ["Q12308941"] = (NamePart.Given, NameGender.Male), ["Q3409032"] = (NamePart.Given, NameGender.Unisex), ["Q101352"] = (NamePart.Family, null)
    };
    private const string Entity = "http://www.wikidata.org/entity/";
    public string SourceId => "wikidata-names";
    public string Version => "1.0.0";
    public string Description => "Saved Wikidata SPARQL JSON for name items (P1705 native labels); requires --retrieved-at and --language-country.";
    public bool Redistributable => true;
    public string Format => "json";

    public void ValidateOptions(NameImportOptions options)
    {
        if (options.License != "CC0-1.0") throw new ArgumentException("Wikidata structured data uses CC0-1.0; supply the actual CC0 terms.");
        if (options.RetrievedAt is null || options.RetrievedAt == default(DateTimeOffset)) throw new ArgumentException("wikidata-names requires the actual query retrieval timestamp (--retrieved-at).");
        if (options.LanguageCountries is not { Count: > 0 } map) throw new ArgumentException("wikidata-names requires at least one explicit --language-country mapping such as ja=JP.");
        foreach (var (language, country) in map)
            if (!LanguageTag().IsMatch(language) || country.Length != 2 || !country.All(c => c is >= 'A' and <= 'Z'))
                throw new ArgumentException($"Invalid language-country mapping {language}={country}.");
    }

    public async Task ReadAsync(Stream input, NameAdapterContext context, CancellationToken ct)
    {
        using var document = await JsonDocument.ParseAsync(input, new JsonDocumentOptions { AllowDuplicateProperties = false }, ct);
        if (!document.RootElement.TryGetProperty("head", out var head) || !head.TryGetProperty("vars", out var vars) || vars.ValueKind != JsonValueKind.Array ||
            !document.RootElement.TryGetProperty("results", out var results) || !results.TryGetProperty("bindings", out var bindings) || bindings.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected SPARQL SELECT JSON with head.vars and results.bindings.");
        string[] required = ["item", "type", "statement", "rank", "name"];
        if (!required.All(r => vars.EnumerateArray().Any(v => v.GetString() == r))) throw new InvalidDataException("Expected the names.rq bindings item, type, statement, rank and name.");
        var map = context.Options.LanguageCountries!;
        var index = 0;
        foreach (var binding in bindings.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var row = "row " + (++index).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var value in binding.EnumerateObject())
            {
                if (value.Value.ValueKind != JsonValueKind.Object || !value.Value.TryGetProperty("value", out var text) || text.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("SPARQL bindings must retain type/value objects.");
                fields[value.Name] = value.Value.Clone();
            }
            string Value(string name) => fields.TryGetValue(name, out var v) ? v.GetProperty("value").GetString()! : "";
            if (!required.All(fields.ContainsKey)) { context.Diagnostic(NameDiagnosticSeverity.Error, "binding-missing", row, "A required binding is unbound."); continue; }
            var item = Value("item"); var type = Value("type");
            if (!item.StartsWith(Entity, StringComparison.Ordinal) || !type.StartsWith(Entity, StringComparison.Ordinal) || !Types.TryGetValue(type[Entity.Length..], out var kind))
            { context.Diagnostic(NameDiagnosticSeverity.Error, "name-type", row, "Item or type is not a supported Wikidata name entity."); continue; }
            if (Value("rank").EndsWith("#DeprecatedRank", StringComparison.Ordinal))
            { context.Diagnostic(NameDiagnosticSeverity.Warning, "deprecated-statement", row, "Deprecated native label statement not staged."); continue; }
            var language = fields["name"].TryGetProperty("xml:lang", out var lang) ? lang.GetString() ?? "" : "";
            if (!map.TryGetValue(language, out var country))
            { context.Diagnostic(NameDiagnosticSeverity.Information, "language-not-mapped", row, $"Language '{language}' has no operator country mapping; not staged."); continue; }
            context.Record(row, country, kind.Part, Value("name"), fields, kind.Gender, Value("romanized") is { Length: > 0 } romanized ? romanized : null,
                "Wikidata (query binding 'romanized')", externalId: item[Entity.Length..]);
        }
    }

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTag();
}
