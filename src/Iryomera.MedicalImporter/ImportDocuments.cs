using System.Text.Json;
using Json.Schema;
using Iryomera.Content.SDK;

namespace Iryomera.MedicalImporter;

/// <summary>Offline schema validation on original importer JSON, before CLR defaults.</summary>
public static class ImportDocuments
{
    private static readonly Lazy<JsonSchema> Bundle = new(() =>
    {
        using var resource = typeof(ImportDocuments).Assembly.GetManifestResourceStream("Iryomera.MedicalImporter.Schemas.v1.json")
            ?? throw new InvalidOperationException("Missing importer schemas.");
        using var reader = new StreamReader(resource);
        return JsonSchema.FromText(reader.ReadToEnd());
    });

    public static async Task<T> ReadAsync<T>(Stream input, CancellationToken ct = default) where T : class
    {
        var name = typeof(T) == typeof(ImportWorkspace) ? "importWorkspace" : typeof(T) == typeof(SourceAssertionDocument) ? "sourceAssertionDocument" :
            typeof(T) == typeof(ReleaseReport) ? "releaseReport" : throw new NotSupportedException("Unsupported importer document type.");
        using var document = await JsonDocument.ParseAsync(input, new JsonDocumentOptions { AllowDuplicateProperties = false }, ct);
        var schema = JsonSchema.FromText("{\"$ref\":\"https://schemas.iryomera.org/importer/v1/contracts.schema.json#/$defs/" + name + "\"}");
        var options = new EvaluationOptions { RequireFormatValidation = true };
        options.SchemaRegistry.Register(Bundle.Value);
        if (!schema.Evaluate(document.RootElement, options).IsValid) throw new InvalidDataException("Importer JSON does not match " + name + " schema v1.");
        var value = document.RootElement.Deserialize<T>(ContentJson.Options) ?? throw new InvalidDataException("Expected importer document object.");
        if (value is ImportWorkspace workspace) WorkspaceValidation.EnsureShape(workspace);
        return value;
    }
}
