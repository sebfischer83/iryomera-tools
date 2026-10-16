using System.Globalization;
using Iryomera.Content.SDK;
using Microsoft.Extensions.Localization;

namespace Iryomera.Localization;

/// <summary>Engine-independent contract usable by Godot: load catalogs once and format using an explicit culture.</summary>
public interface IContentLocalizer
{
    bool TryGetTemplate(string key, CultureInfo culture, out string template);
    string Format(string key, CultureInfo culture, IReadOnlyDictionary<string, object?>? arguments = null);
}

/// <summary>Offline immutable catalog snapshots; specific culture → parents → English → key fallback.</summary>
public sealed class ContentLocalizer : IContentLocalizer
{
    private readonly Dictionary<string, Dictionary<string, string>> _languages = new(StringComparer.Ordinal);

    public ContentLocalizer(IEnumerable<LocalizationDefinition> catalogs)
    {
        var list = catalogs.ToList();
        var fatal = LocalizationValidator.Validate(list).Where(x => x.Code is not ("missing-translation" or "missing-language" or "unused-key")).ToArray();
        if (fatal.Length != 0) throw new ArgumentException(string.Join(Environment.NewLine, fatal.Select(x => $"{x.Code} {x.Path}: {x.Message}")), nameof(catalogs));
        foreach (var catalog in list)
        {
            if (!_languages.TryGetValue(catalog.Language, out var entries)) _languages[catalog.Language] = entries = new(StringComparer.Ordinal);
            foreach (var (key, value) in catalog.Entries)
                if (!entries.TryAdd(key, value)) throw new ArgumentException($"Duplicate localization key {key} in {catalog.Language}.", nameof(catalogs));
        }
    }

    public bool TryGetTemplate(string key, CultureInfo culture, out string template)
    {
        foreach (var language in FallbackLanguages(culture))
            if (_languages.TryGetValue(language, out var catalog) && catalog.TryGetValue(key, out var value))
            { template = value; return true; }
        template = key;
        return false;
    }

    public string Format(string key, CultureInfo culture, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        if (!TryGetTemplate(key, culture, out var template)) return key;
        var values = arguments is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(arguments, StringComparer.Ordinal);
        // A per-call formatter avoids shared mutable SmartFormat state across server requests.
        return TextFormatting.Create().Format(culture, template, values);
    }

    public IEnumerable<string> GetKeys(CultureInfo culture, bool includeParents) =>
        (includeParents ? FallbackLanguages(culture) : [culture.Name])
        .Where(_languages.ContainsKey).SelectMany(x => _languages[x].Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    private static IEnumerable<string> FallbackLanguages(CultureInfo culture)
    {
        for (var current = culture; !current.Equals(CultureInfo.InvariantCulture); current = current.Parent)
            yield return current.Name;
        yield return "en";
    }
}

/// <summary>IStringLocalizer adapter for future Blazor tools. DI registration belongs to the host.</summary>
public sealed class ContentStringLocalizer(ContentLocalizer localizer) : IStringLocalizer
{
    public LocalizedString this[string name]
    {
        get
        {
            var found = localizer.TryGetTemplate(name, CultureInfo.CurrentUICulture, out var value);
            return new LocalizedString(name, value, !found);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var culture = CultureInfo.CurrentUICulture;
            var found = localizer.TryGetTemplate(name, culture, out _);
            var values = arguments.Select((value, index) => (value, index)).ToDictionary(x => x.index.ToString(CultureInfo.InvariantCulture), x => (object?)x.value);
            return new LocalizedString(name, localizer.Format(name, culture, values), !found);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => localizer
        .GetKeys(CultureInfo.CurrentUICulture, includeParentCultures).Select(key => this[key]);
}
