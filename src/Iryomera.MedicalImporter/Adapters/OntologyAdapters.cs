using System.Text.Json;
using System.Text.RegularExpressions;

namespace Iryomera.MedicalImporter.Adapters;

public sealed class HpoAdapter : OntologyAdapter
{
    public override string SourceId => "hpo";
    protected override string Prefix => "HP:";
    protected override ImportedTermKind TermKind => ImportedTermKind.Finding;
}

public sealed class MaxoAdapter : OntologyAdapter
{
    public override string SourceId => "maxo";
    protected override string Prefix => "MAXO:";
    protected override ImportedTermKind TermKind => ImportedTermKind.MedicalProcedure;
}

/// <summary>Reads published OBO Graph JSON and the documented term/edge subset of OBO 1.2.</summary>
public abstract class OntologyAdapter : IImportAdapter
{
    public abstract string SourceId { get; }
    protected abstract string Prefix { get; }
    protected abstract ImportedTermKind TermKind { get; }
    public IReadOnlyList<string> Formats => ["json", "obo"];

    public async Task ReadAsync(Stream input, string format, AdapterContext context, CancellationToken cancellationToken)
    {
        if (format == "json") await ReadJsonAsync(input, context, cancellationToken);
        else if (format == "obo") await ReadOboAsync(input, context, cancellationToken);
        else throw new InvalidDataException($"{SourceId} does not support {format}; use OBO Graph JSON or OBO.");
    }

    internal static string ExternalId(string value)
    {
        const string baseUri = "http://purl.obolibrary.org/obo/";
        if (value.StartsWith(baseUri, StringComparison.Ordinal) || value.StartsWith(baseUri.Replace("http:", "https:"), StringComparison.Ordinal))
        {
            var local = value[(value.LastIndexOf('/') + 1)..];
            var separator = local.IndexOf('_');
            if (separator > 0) return local[..separator] + ":" + local[(separator + 1)..];
        }
        return value;
    }

    private async Task ReadJsonAsync(Stream input, AdapterContext context, CancellationToken ct)
    {
        using var document = await JsonDocument.ParseAsync(input, cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("graphs", out var graphs) || graphs.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected OBO Graph JSON with a graphs array, not an arbitrary JSON ontology export.");
        foreach (var graph in graphs.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            if (graph.TryGetProperty("meta", out var metadata))
            {
                context.Batch.SourceMetadata["graph_" + context.Batch.SourceMetadata.Count] = metadata.Clone();
                if (metadata.TryGetProperty("version", out var version)) context.Batch.ObservedSourceVersion = version.GetString();
            }
            if (!graph.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("OBO Graph requires a nodes array.");
            foreach (var node in nodes.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (node.TryGetProperty("type", out var type) && type.GetString() != "CLASS") continue;
                var id = ExternalId(node.GetProperty("id").GetString() ?? "");
                if (!id.StartsWith(Prefix, StringComparison.Ordinal))
                { context.Diagnostic(DiagnosticSeverity.Information, "foreign-ontology-term", id, "Term belongs to another ontology and is not reclassified."); continue; }
                var label = node.TryGetProperty("lbl", out var lbl) ? lbl.GetString() ?? "" : "";
                var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal) { ["node"] = node.Clone() };
                var synonyms = new List<string>(); var aliases = new List<string>(); string? description = null; var obsolete = false;
                if (node.TryGetProperty("meta", out var meta))
                {
                    if (meta.TryGetProperty("deprecated", out var deprecated)) obsolete = deprecated.GetBoolean();
                    if (meta.TryGetProperty("definition", out var def) && def.TryGetProperty("val", out var val)) description = val.GetString();
                    if (meta.TryGetProperty("synonyms", out var syns))
                        foreach (var synonym in syns.EnumerateArray())
                            if (synonym.TryGetProperty("val", out var value) && value.GetString() is { } text) synonyms.Add(text);
                    if (meta.TryGetProperty("basicPropertyValues", out var props))
                        foreach (var prop in props.EnumerateArray())
                            if (prop.GetProperty("pred").GetString()?.EndsWith("hasAlternativeId", StringComparison.Ordinal) == true)
                                aliases.Add(ExternalId(prop.GetProperty("val").GetString()!));
                    if (meta.TryGetProperty("xrefs", out var xrefs))
                        foreach (var xref in xrefs.EnumerateArray())
                            context.Assertion(SourceAssertionKind.CrossReference, id, "xref", xref.GetProperty("val").GetString()!, new(StringComparer.Ordinal) { ["xref"] = xref.Clone() });
                }
                context.Term(id, label, TermKind, fields, description: description, synonyms: synonyms, alternateIds: aliases, obsolete: obsolete);
            }
            if (graph.TryGetProperty("edges", out var edges))
                foreach (var edge in edges.EnumerateArray())
                    context.Assertion(SourceAssertionKind.OntologyHierarchy, ExternalId(edge.GetProperty("sub").GetString()!), edge.GetProperty("pred").GetString()!,
                        ExternalId(edge.GetProperty("obj").GetString()!), new(StringComparer.Ordinal) { ["edge"] = edge.Clone() });
        }
    }

    private async Task ReadOboAsync(Stream input, AdapterContext context, CancellationToken ct)
    {
        using var reader = new StreamReader(input, leaveOpen: true);
        var stanza = "header";
        var tags = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var header = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        while (await reader.ReadLineAsync(ct) is { } raw)
        {
            ct.ThrowIfCancellationRequested();
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('!')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush(); tags.Clear(); stanza = line;
                continue;
            }
            var separator = line.IndexOf(':');
            if (separator <= 0) throw new InvalidDataException("Malformed OBO tag line.");
            var tag = line[..separator]; var value = line[(separator + 1)..].Trim();
            var destination = stanza == "header" ? header : tags;
            if (!destination.TryGetValue(tag, out var values)) destination[tag] = values = [];
            values.Add(value);
        }
        Flush();
        if (!header.TryGetValue("format-version", out var format) || !format[0].StartsWith("1.", StringComparison.Ordinal))
            throw new InvalidDataException("Expected an OBO 1.x format-version header.");
        context.Batch.SourceMetadata["header"] = ImportIdentity.Json(header);
        if (header.TryGetValue("data-version", out var version)) context.Batch.ObservedSourceVersion = version[0];

        void Flush()
        {
            if (stanza != "[Term]" || tags.Count == 0) return;
            var id = First("id");
            if (!id.StartsWith(Prefix, StringComparison.Ordinal))
            { context.Diagnostic(DiagnosticSeverity.Information, "foreign-ontology-term", id, "Foreign ontology term retained only through source references."); return; }
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (key, values) in tags) fields[key] = ImportIdentity.Json(values);
            context.Term(id, First("name"), TermKind, fields,
                description: tags.ContainsKey("def") ? Quoted(First("def")) : null,
                synonyms: tags.GetValueOrDefault("synonym", []).Select(Quoted), alternateIds: tags.GetValueOrDefault("alt_id", []), obsolete: First("is_obsolete") == "true");
            foreach (var parent in tags.GetValueOrDefault("is_a", []))
                context.Assertion(SourceAssertionKind.OntologyHierarchy, id, "is_a", parent.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], fields);
            foreach (var relation in tags.GetValueOrDefault("relationship", []))
            {
                var parts = relation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) throw new InvalidDataException("Malformed OBO relationship tag.");
                context.Assertion(SourceAssertionKind.OntologyHierarchy, id, parts[0], parts[1], fields);
            }
            foreach (var xref in tags.GetValueOrDefault("xref", []))
                context.Assertion(SourceAssertionKind.CrossReference, id, "xref", xref.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], fields);
        }
        string First(string key) => tags.TryGetValue(key, out var values) ? values[0] : "";
    }

    private static string Quoted(string value)
    {
        var match = Regex.Match(value, "^\"((?:\\\\.|[^\"\\\\])*)\"");
        if (!match.Success) throw new InvalidDataException("Expected an OBO quoted label/definition.");
        return Regex.Replace(match.Groups[1].Value, @"\\(.)", m => m.Groups[1].Value switch { "n" => "\n", "t" => "\t", "W" => " ", var text => text });
    }
}
