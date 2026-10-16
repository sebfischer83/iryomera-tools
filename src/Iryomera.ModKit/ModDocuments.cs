using System.Text.Json;
using Iryomera.Content.SDK;
using Json.Schema;

namespace Iryomera.ModKit;

/// <summary>Checks original JSON before defaults; schemas are embedded and never fetched.</summary>
public static class ModDocuments
{
    private static readonly Lazy<JsonSchema> Bundle = new(() =>
    {
        using var stream = typeof(ModDocuments).Assembly.GetManifestResourceStream("Iryomera.ModKit.Schemas.v1.json")!;
        using var reader = new StreamReader(stream); return JsonSchema.FromText(reader.ReadToEnd());
    });
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct = default) where T : class
    {
        if (typeof(T) != typeof(ModProject) && typeof(T) != typeof(ModArchiveIndex)) throw new NotSupportedException("Use a registered ModKit document type.");
        using var document = await JsonDocument.ParseAsync(stream, new() { AllowDuplicateProperties = false }, ct);
        var name = char.ToLowerInvariant(typeof(T).Name[0]) + typeof(T).Name[1..];
        var schema = JsonSchema.FromText("{\"$ref\":\"https://schemas.iryomera.org/modkit/v1/contracts.schema.json#/$defs/" + name + "\"}");
        var options = new EvaluationOptions { RequireFormatValidation = true }; options.SchemaRegistry.Register(Bundle.Value);
        if (!schema.Evaluate(document.RootElement, options).IsValid) throw new InvalidDataException("ModKit JSON does not match the " + name + " v1 schema.");
        return document.RootElement.Deserialize<T>(ContentJson.Options) ?? throw new InvalidDataException("Expected a document.");
    }
}
