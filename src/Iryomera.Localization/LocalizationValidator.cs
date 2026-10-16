using System.Globalization;
using System.Text.RegularExpressions;
using Iryomera.Content.SDK;
using SmartFormat;
using SmartFormat.Core.Parsing;
using SmartFormat.Core.Settings;
using SmartFormat.Extensions;

namespace Iryomera.Localization;

/// <summary>Defines the supported portable SmartFormat subset: flat variables, numeric/date formats and plural branches.</summary>
internal static class TextFormatting
{
    internal static SmartFormatter Create()
    {
        var formatter = new SmartFormatter(new SmartSettings
        {
            Parser = { ErrorAction = ParseErrorAction.ThrowError },
            Formatter = { ErrorAction = FormatErrorAction.ThrowError }
        });
        formatter.AddExtensions(new DictionarySource(), new DefaultSource());
        formatter.AddExtensions(new PluralLocalizationFormatter(), new DefaultFormatter());
        return formatter;
    }

    internal static HashSet<string> Placeholders(string text)
    {
        var formatter = Create();
        using var parsed = formatter.Parser.ParseFormat(text);
        var names = new HashSet<string>(StringComparer.Ordinal);
        Visit(parsed);
        return names;

        void Visit(Format format)
        {
            foreach (var placeholder in format.Items.OfType<Placeholder>())
            {
                var selectors = placeholder.GetSelectors();
                if (selectors.Count > 1) throw new FormatException("Use flat variables, without property access or indexing.");
                if (selectors.Count == 1 && selectors[0].RawText.Length > 0)
                {
                    var name = selectors[0].RawText;
                    if (!Regex.IsMatch(name, "^([A-Za-z_][A-Za-z0-9_]*|[0-9]+)\\z")) throw new FormatException("Invalid placeholder name.");
                    names.Add(name);
                }
                if (placeholder.FormatterName is not ("" or "plural")) throw new FormatException("Only default and plural formatters are supported.");
                if (placeholder.Format is not null) Visit(placeholder.Format);
            }
        }
    }
}

/// <summary>Translation diagnostics, including missing/unused keys and parsed placeholder parity.</summary>
public static class LocalizationValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(IEnumerable<LocalizationDefinition> catalogs,
        IEnumerable<string>? usedKeys = null, IEnumerable<string>? requiredLanguages = null)
    {
        var list = catalogs.ToList();
        var errors = list.SelectMany(ContractShape.Validate).ToList();
        if (errors.Count > 0) return errors;
        var required = (requiredLanguages ?? ["en", "de"]).ToArray();
        var byPair = new Dictionary<(string Namespace, string Language), LocalizationDefinition>();
        var signatures = new Dictionary<(string Namespace, string Language, string Key), HashSet<string>>();
        var used = usedKeys?.ToHashSet(StringComparer.Ordinal);
        void Error(string code, string path, string message) => errors.Add(new(code, path, message));
        foreach (var catalog in list)
        {
            var path = catalog.Namespace + "/" + catalog.Language;
            if (catalog.SchemaVersion != ContentFormat.SchemaVersion) Error("schema-version", path, "Unsupported localization schema version.");
            if (!ContentFormat.IsValidNamespace(catalog.Namespace)) Error("namespace", path, "Invalid catalog namespace.");
            try
            {
                var culture = CultureInfo.GetCultureInfo(catalog.Language);
                if (culture.Equals(CultureInfo.InvariantCulture) || culture.Name != catalog.Language)
                    Error("language", path, "Use a canonical culture name such as en, de or de-DE.");
            }
            catch (CultureNotFoundException) { Error("language", path, "Unknown culture."); }
            if (!byPair.TryAdd((catalog.Namespace, catalog.Language), catalog)) Error("duplicate-catalog", path, "Repeated namespace/language catalog.");
            foreach (var (key, value) in catalog.Entries)
            {
                if (!key.StartsWith(catalog.Namespace + ".", StringComparison.Ordinal) || !ContentFormat.IsValidLocalizationKey(key))
                    Error("localization-key", path + "/" + key, "Keys must be stable, dotted and owned by the catalog namespace.");
                if (string.IsNullOrWhiteSpace(value)) Error("empty-translation", path + "/" + key, "Translation is empty.");
                try { signatures[(catalog.Namespace, catalog.Language, key)] = TextFormatting.Placeholders(value); }
                catch (Exception e) when (e is ParsingErrors or FormatException)
                { Error("text-format", path + "/" + key, e.Message); }
                if (used is not null && !used.Contains(key)) Error("unused-key", path + "/" + key, "Key is not in the supplied usage set.");
            }
        }
        foreach (var group in list.GroupBy(x => x.Namespace, StringComparer.Ordinal))
        {
            if (!byPair.TryGetValue((group.Key, "en"), out var english))
            { Error("missing-reference-language", group.Key, "Each namespace needs an English reference catalog."); continue; }
            foreach (var language in required)
                if (!byPair.ContainsKey((group.Key, language))) Error("missing-language", group.Key + "/" + language, "Required language catalog is missing.");
            foreach (var catalog in group)
            {
                foreach (var key in english.Entries.Keys.Except(catalog.Entries.Keys, StringComparer.Ordinal))
                    Error("missing-translation", catalog.Language + "/" + key, "English key has no translation.");
                foreach (var key in catalog.Entries.Keys.Except(english.Entries.Keys, StringComparer.Ordinal))
                    Error("missing-reference-key", "en/" + key, "Translation has no English reference key.");
                foreach (var key in english.Entries.Keys.Intersect(catalog.Entries.Keys, StringComparer.Ordinal))
                    if (signatures.TryGetValue((group.Key, "en", key), out var expected) &&
                        signatures.TryGetValue((group.Key, catalog.Language, key), out var actual) && !expected.SetEquals(actual))
                        Error("placeholder-mismatch", catalog.Language + "/" + key, "Placeholder names differ from English.");
            }
        }
        if (used is not null)
        {
            var allEnglish = list.Where(x => x.Language == "en").SelectMany(x => x.Entries.Keys).ToHashSet(StringComparer.Ordinal);
            foreach (var key in used.Except(allEnglish, StringComparer.Ordinal)) Error("missing-key", key, "Used key has no English reference translation.");
        }
        return errors;
    }

    public static IEnumerable<string> GetContentKeys(ContentPackage package) => package.Definitions.SelectMany(d =>
        new[] { d.NameKey }.Concat(d.DescriptionKey is null ? [] : new[] { d.DescriptionKey }).Concat(d.SynonymKeys));
}
