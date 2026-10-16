using Iryomera.Content.SDK;
using Iryomera.Localization;

namespace Iryomera.ContentStudio.Services;

internal static class LanguageSpecificAliases
{
    internal static IReadOnlyList<(string Language, string Key)> Keys(ContentPackage package) => ContentLocalizationValidator.Keys(package);
    internal static IReadOnlyList<ValidationIssue> Validate(ContentPackage package, IReadOnlyList<LocalizationDefinition> catalogs) => ContentLocalizationValidator.Validate(package, catalogs);
}
