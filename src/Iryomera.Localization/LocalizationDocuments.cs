using Iryomera.Content.SDK;

namespace Iryomera.Localization;

/// <summary>Uses the SDK's raw schema/semantic pipeline and adds cross-catalog localization diagnostics.</summary>
public static class LocalizationDocuments
{
    public static Task<ContentReadResult<LocalizationDefinition>> ReadValidatedAsync(Stream input,
        IEnumerable<LocalizationDefinition>? otherCatalogs = null, IEnumerable<string>? usedKeys = null,
        IEnumerable<string>? requiredLanguages = null, CancellationToken cancellationToken = default) =>
        ContentDocuments.ReadValidatedAsync<LocalizationDefinition>(input,
            catalog => LocalizationValidator.Validate((otherCatalogs ?? []).Append(catalog), usedKeys, requiredLanguages),
            cancellationToken: cancellationToken);
}
