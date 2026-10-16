using System.Text.Json;
using System.Text.Json.Serialization;

namespace Iryomera.Content.SDK;

/// <summary>Canonical camelCase, string enums, strict numbers, UTF-8 async exchange.</summary>
public static class ContentJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowOutOfOrderMetadataProperties = true,
            AllowDuplicateProperties = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static async Task<T> ReadAsync<T>(Stream input, CancellationToken cancellationToken = default) where T : class
    {
        if (typeof(ContentDefinition).IsAssignableFrom(typeof(T)))
        {
            var definition = await JsonSerializer.DeserializeAsync<ContentDefinition>(input, Options, cancellationToken).ConfigureAwait(false);
            return definition as T ?? throw new JsonException($"Expected a non-null {typeof(T).Name} with a matching kind.");
        }
        return await JsonSerializer.DeserializeAsync<T>(input, Options, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The document must not be null.");
    }

    public static Task WriteAsync<T>(Stream output, T value, CancellationToken cancellationToken = default) =>
        value is ContentDefinition definition
            ? JsonSerializer.SerializeAsync<ContentDefinition>(output, definition, Options, cancellationToken)
            : JsonSerializer.SerializeAsync(output, value, Options, cancellationToken);
}
