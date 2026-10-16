using System.Text.Json;
using System.Text.Json.Nodes;
using Iryomera.Content.SDK;
using Json.Schema;

namespace Iryomera.Foundation.Tests;

internal static class Fixtures
{
    internal static string Example(string name) => Path.Combine(AppContext.BaseDirectory, "examples", name + ".json");
    internal static async Task<T> Load<T>(string name) where T : class
    {
        await using var input = File.OpenRead(Example(name));
        return await ContentJson.ReadAsync<T>(input);
    }

    internal static ContentPackage Package() => JsonSerializer.Deserialize<ContentPackage>(File.ReadAllText(Example("content")), ContentJson.Options)!;
    internal static LocalizationDefinition Catalog(string language) => JsonSerializer.Deserialize<LocalizationDefinition>(File.ReadAllText(Example(language)), ContentJson.Options)!;
    internal static EvaluationResults Evaluate(string contract, JsonNode? node)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "schemas");
        var options = new EvaluationOptions { RequireFormatValidation = true, OutputFormat = OutputFormat.List };
        options.SchemaRegistry.Register(JsonSchema.FromFile(Path.Combine(directory, "contracts.schema.json")));
        return JsonSchema.FromFile(Path.Combine(directory, contract + ".schema.json")).Evaluate(node, options);
    }
}
