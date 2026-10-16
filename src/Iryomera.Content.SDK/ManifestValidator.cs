using System.Globalization;
using NuGet.Versioning;

namespace Iryomera.Content.SDK;

/// <summary>Validates mod metadata only. Does not load assets, execute code or install archives.</summary>
public static class ManifestValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(ModManifest manifest, IEnumerable<ContentPackage>? installedPackages = null)
    {
        var errors = ContractShape.Validate(manifest).ToList();
        if (errors.Count != 0) return errors;
        errors.AddRange(ContentValidator.Validate(new ContentPackage
        {
            SchemaVersion = manifest.SchemaVersion,
            Manifest = new ContentPackageManifest
            {
                SchemaVersion = manifest.SchemaVersion, Id = manifest.Id, Namespace = manifest.Namespace,
                Version = manifest.Version, License = manifest.License, Dependencies = manifest.Dependencies
            }
        }, installedPackages));
        if (!ContentFormat.IsValidLocalizationKey(manifest.NameKey))
            errors.Add(new("localization-key", "$.nameKey", "Expected a dotted localization key."));
        if (string.IsNullOrWhiteSpace(manifest.Author)) errors.Add(new("author", "$.author", "Author is required."));
        if (!VersionRange.TryParse(manifest.CompatibleGameVersions, out _))
            errors.Add(new("version-range", "$.compatibleGameVersions", "Invalid compatible game version range."));
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in manifest.ContentFiles)
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') ||
                path.Split('/').Any(part => part is "" or "." or "..") || path.Any(char.IsControl))
                errors.Add(new("content-path", "$.contentFiles", "Use a nonempty relative forward-slash path without traversal."));
            if (!files.Add(path)) errors.Add(new("duplicate-file", "$.contentFiles", "File paths must be unique, ignoring case for portability."));
        }
        var languages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in manifest.SupportedLanguages)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(language);
                if (culture.Equals(CultureInfo.InvariantCulture) || culture.Name != language)
                    errors.Add(new("language", "$.supportedLanguages", "Use a canonical culture name."));
            }
            catch (CultureNotFoundException) { errors.Add(new("language", "$.supportedLanguages", "Unknown culture.")); }
            if (!languages.Add(language)) errors.Add(new("duplicate-language", "$.supportedLanguages", "Language is repeated."));
        }
        return errors;
    }
}
