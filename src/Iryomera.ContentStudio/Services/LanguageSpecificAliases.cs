using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.Localization;

namespace Iryomera.ContentStudio.Services;

/// <summary>Wikidata aliases have independent language identities; never invent a translation pairing.</summary>
internal static class LanguageSpecificAliases
{
    internal static IReadOnlyList<(string Language, string Key)> Keys(ContentPackage package) => package.Definitions
        .Where(d => d.Extensions?.TryGetValue("localizedAliasKeys", out _) == true)
        .SelectMany(d => d.Extensions!["localizedAliasKeys"].ValueKind == JsonValueKind.Object
            ? d.Extensions["localizedAliasKeys"].EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array)
                .SelectMany(p => p.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => (p.Name, v.GetString()!)))
            : []).ToArray();

    internal static IReadOnlyList<ValidationIssue> Validate(ContentPackage package, IReadOnlyList<LocalizationDefinition> catalogs)
    {
        var aliases = Keys(package);
        var common = LocalizationValidator.GetContentKeys(package).ToHashSet(StringComparer.Ordinal);
        var used = common.Concat(aliases.Select(a => a.Key)).ToHashSet(StringComparer.Ordinal);
        var issues = LocalizationValidator.Validate(catalogs, used, ["en", "de"]).Where(issue =>
            !(issue.Code == "missing-key" && !common.Contains(issue.Path) && aliases.Any(a => a.Language != "en" && a.Key == issue.Path)) &&
            !(issue.Code is "missing-translation" or "missing-reference-key" && aliases.Any(a =>
                issue.Path.EndsWith("/" + a.Key, StringComparison.Ordinal) && !issue.Path.StartsWith(a.Language + "/", StringComparison.Ordinal)))).ToList();
        foreach (var (language, key) in aliases)
            if (!catalogs.Any(c => c.Language == language && c.Entries.ContainsKey(key)))
                issues.Add(new("missing-translation", language + "/" + key, "Language-specific source alias is missing in its original language."));
        foreach (var definition in package.Definitions.Where(d => d.Extensions?.ContainsKey("localizedAliasKeys") == true))
        {
            var map = definition.Extensions!["localizedAliasKeys"];
            if (map.ValueKind != JsonValueKind.Object || map.EnumerateObject().Any(p => p.Name is not ("en" or "de") || p.Value.ValueKind != JsonValueKind.Array || p.Value.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String)))
                issues.Add(new("alias-map", definition.Id, "Source alias maps must contain en/de arrays of stable localization keys."));
        }
        return issues;
    }
}
