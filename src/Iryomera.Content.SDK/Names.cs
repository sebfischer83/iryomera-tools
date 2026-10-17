using System.Text;
using System.Text.Json.Serialization;

namespace Iryomera.Content.SDK;

public enum NamePart { Given, Family }
public enum NameGender { Female, Male, Unisex, Unknown }
public enum NameOrder { GivenFamily, FamilyGiven }

/// <summary>A source that contributed entries to a pool, referenced by key from attestations.</summary>
public sealed record NamePoolSource : ExtensibleRecord
{
    public required string Key { get; init; }
    public required Provenance Provenance { get; init; }
}

/// <summary>
/// One source's evidence for an entry. Statistics stay per source and are never combined: counts from
/// different sources are not comparable. A birth-year range is a cohort; one year is from == to.
/// </summary>
public sealed record NameAttestation : ExtensibleRecord
{
    public required string SourceKey { get; init; }
    public string? ExternalId { get; init; }
    public int? Rank { get; init; }
    public long? Count { get; init; }
    public decimal? Proportion { get; init; }
    public int? BirthYearFrom { get; init; }
    public int? BirthYearTo { get; init; }
}

/// <summary>A name string is data, not localized text: it has no localization key and never changes with the UI language.</summary>
public sealed record NameEntry : ExtensibleRecord
{
    public required string Key { get; init; }
    public required string Localized { get; init; }
    public string? Romanized { get; init; }
    public string? RomanizationSourceKey { get; init; }
    public NameGender? Gender { get; init; }
    public List<NameAttestation> Attestations { get; init; } = [];
}

/// <summary>Given or family names for one country or ISO 3166-2 region. Not medical data and never a balancing input.</summary>
public sealed record NamePool : ExtensibleRecord
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string NameKey { get; init; }
    public required string Country { get; init; }
    public string? Region { get; init; }
    public required NamePart Part { get; init; }
    public required Provenance Provenance { get; init; }
    public List<NamePoolSource> Sources { get; init; } = [];
    public List<NameEntry> Entries { get; init; } = [];
}

/// <summary>
/// How parts combine for a country or region. LocalizedSeparator applies only to Localized values; romanized parts are
/// always joined with a single space. Display order for romanized names is the game's choice. Professional titles are
/// localization keys, not name data.
/// </summary>
public sealed record NameFormat : ExtensibleRecord
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = ContentFormat.SchemaVersion;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Country { get; init; }
    public string? Region { get; init; }
    public required NameOrder Order { get; init; }
    public int FamilyNameParts { get; init; } = 1;
    public string LocalizedSeparator { get; init; } = " ";
    public required Provenance Provenance { get; init; }
}

public sealed record SelectedNameEntry(string PackageId, NamePool Pool, NameEntry Entry);

/// <summary>Aggregates installed pools. Call on a validated package set; selection itself belongs to the game.</summary>
public static class NamePools
{
    /// <summary>
    /// Pools of the country without a region, plus pools of the requested region. The order is part of the contract and
    /// independent of package, file or culture order: pool ID, then entry key, both ordinal.
    /// Country and region are compared ordinally; pass uppercase codes such as "DE" and "DE-BY".
    /// </summary>
    public static IReadOnlyList<SelectedNameEntry> Select(IEnumerable<ContentPackage> packages, string country, NamePart part, string? region = null) =>
        packages.SelectMany(p => (p.NamePools ?? []).Select(pool => (p.Manifest.Id, pool)))
            .Where(x => x.pool.Country == country && x.pool.Part == part && (x.pool.Region is null || x.pool.Region == region))
            .OrderBy(x => x.pool.Id, StringComparer.Ordinal)
            .SelectMany(x => x.pool.Entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => new SelectedNameEntry(x.Id, x.pool, e)))
            .ToArray();

    /// <summary>
    /// The regional format if present, otherwise the country format, otherwise null.
    /// Country and region are compared ordinally; pass uppercase codes such as "DE" and "DE-BY".
    /// </summary>
    public static NameFormat? ResolveFormat(IEnumerable<ContentPackage> packages, string country, string? region = null)
    {
        var formats = packages.SelectMany(p => p.NameFormats ?? []).Where(f => f.Country == country).ToArray();
        if (formats.GroupBy(f => f.Region).Any(g => g.Count() > 1))
            throw new InvalidOperationException($"Competing name formats for {country}; validate the package set first (name-format-conflict).");
        return (region is null ? null : formats.SingleOrDefault(f => f.Region == region)) ?? formats.SingleOrDefault(f => f.Region is null);
    }
}

/// <summary>The name text rules used by validation, for importers and editors that check values before building pools.</summary>
public static class NameText
{
    public const int MaxLength = NameValidator.MaxNameLength;
    /// <summary>NFC, at most 100 UTF-16 units, no control/bidi/non-space whitespace and no surrounding or repeated spaces.</summary>
    public static bool IsValid(string value) => NameValidator.IsValidNameText(value);
    /// <summary>Latin letters, combining diacritics and name punctuation only.</summary>
    public static bool IsLatin(string value) => NameValidator.IsLatin(value);
}

internal static class NameValidator
{
    internal const int MaxNameLength = 100;

    internal static IReadOnlyList<ValidationIssue> Validate(ContentPackage package)
    {
        var issues = new List<ValidationIssue>();
        void Error(string code, string path, string message) => issues.Add(new(code, path, message));
        foreach (var pool in package.NamePools ?? [])
        {
            CheckScope(pool.Country, pool.Region, pool.Id, Error);
            if (!Enum.IsDefined(pool.Part)) Error("name-part", pool.Id, "Unknown name part.");
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in pool.Sources)
                if (!ContentFormat.IsValidNamespace(source.Key) || !sources.Add(source.Key))
                    Error("name-source", pool.Id, "Source keys must be unique stable keys.");
            if (pool.Sources.Count == 0) Error("name-source", pool.Id, "A pool requires at least one source.");
            if (pool.Entries.Count == 0) Error("name-empty-pool", pool.Id, "A pool requires at least one entry.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var strings = new HashSet<(string, NameGender?)>();
            foreach (var entry in pool.Entries)
            {
                var path = pool.Id + ".entries[" + entry.Key + "]";
                if (!ContentFormat.IsValidNamespace(entry.Key) || !keys.Add(entry.Key)) Error("name-duplicate-key", path, "Entry keys must be unique stable keys within the pool.");
                if (!IsValidNameText(entry.Localized)) Error("name-text", path + ".localized", "Use NFC text without control, bidirectional or line separator characters, surrounding or repeated spaces, and at most 100 characters.");
                else if (!strings.Add((entry.Localized, entry.Gender))) Error("name-duplicate-entry", path, "Merge identical names into one entry and keep every attestation.");
                if (entry.Romanized is { } romanized && (!IsValidNameText(romanized) || !IsLatin(romanized)))
                    Error("name-romanized", path + ".romanized", "Romanized names must be valid name text in Latin script.");
                if (entry.RomanizationSourceKey is { } romanizationSource && (entry.Romanized is null || !sources.Contains(romanizationSource)))
                    Error("name-source", path + ".romanizationSourceKey", "A romanization source must name a pool source and requires a romanized value.");
                if (entry.Gender is { } gender && !Enum.IsDefined(gender)) Error("name-gender", path, "Unknown name gender.");
                if (pool.Part == NamePart.Given && entry.Gender is null) Error("name-gender", path, "Given names require a gender value; use unknown when the source states none.");
                if (entry.Attestations.Count == 0) Error("name-attestation", path, "Each entry requires at least one source attestation.");
                foreach (var attestation in entry.Attestations)
                {
                    if (!sources.Contains(attestation.SourceKey)) Error("name-source", path + ".attestations", $"Unknown pool source {attestation.SourceKey}.");
                    if (attestation.ExternalId is not null && string.IsNullOrWhiteSpace(attestation.ExternalId)) Error("name-attestation", path + ".attestations", "External IDs must be nonempty when present.");
                    if (attestation.Rank is < 1 || attestation.Count is < 0 || attestation.Proportion is < 0 or > 1)
                        Error("name-statistic", path + ".attestations", "Rank must be positive, count nonnegative and proportion within [0,1].");
                    if ((attestation.BirthYearFrom is null) != (attestation.BirthYearTo is null) || attestation.BirthYearFrom is < 1000 or > 9999 ||
                        attestation.BirthYearTo is < 1000 or > 9999 || attestation.BirthYearFrom > attestation.BirthYearTo)
                        Error("name-cohort", path + ".attestations", "Birth-year cohorts need both bounds as four-digit years with from <= to.");
                }
            }
        }
        foreach (var format in package.NameFormats ?? [])
        {
            CheckScope(format.Country, format.Region, format.Id, Error);
            if (!Enum.IsDefined(format.Order)) Error("name-order", format.Id, "Unknown name order.");
            if (format.FamilyNameParts is < 1 or > 3) Error("name-format", format.Id, "Family name parts must be within [1,3].");
            if (format.LocalizedSeparator is not ("" or " ")) Error("name-format", format.Id, "The localized separator is empty or a single space.");
        }
        return issues;
    }

    /// <summary>Competing formats for one scope are an error across the whole validated set, never an implicit last-wins override.</summary>
    internal static IEnumerable<ValidationIssue> ValidateFormatConflicts(IEnumerable<ContentPackage> packages) =>
        packages.SelectMany(p => p.NameFormats ?? [])
            .GroupBy(f => (f.Country, f.Region))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.OrderBy(f => f.Id, StringComparer.Ordinal)
                .Select(f => new ValidationIssue("name-format-conflict", f.Id, $"Competing name formats for {g.Key.Country}{(g.Key.Region is null ? "" : " / " + g.Key.Region)}.")));

    private static void CheckScope(string country, string? region, string path, Action<string, string, string> error)
    {
        if (country.Length != 2 || !country.All(c => c is >= 'A' and <= 'Z')) error("name-country", path, "Use an uppercase ISO 3166-1 alpha-2 country code.");
        if (region is not null && (!region.StartsWith(country + "-", StringComparison.Ordinal) || region.Length is < 4 or > 6 ||
            !region[3..].All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9')))
            error("name-region", path, "Use an ISO 3166-2 subdivision code of the pool's country.");
    }

    /// <summary>Deterministic on every runtime: checks fixed code point sets, not runtime-specific Unicode category tables.</summary>
    internal static bool IsValidNameText(string value)
    {
        if (value.Length is 0 or > MaxNameLength || value[0] == ' ' || value[^1] == ' ' || value.Contains("  ", StringComparison.Ordinal)) return false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c)) { if (i + 1 >= value.Length || !char.IsLowSurrogate(value[++i])) return false; continue; }
            if (char.IsLowSurrogate(c)) return false;
            if (c < 0x20 || c is >= '\u007F' and <= '\u009F' || (c != ' ' && char.IsWhiteSpace(c)) || IsBidiControl(c) || c is '﻿' or '￾' or '￿') return false;
        }
        return value.IsNormalized(NormalizationForm.FormC);
    }

    private static bool IsBidiControl(char c) => c is '؜' or '‎' or '‏' or >= '‪' and <= '‮' or >= '⁦' and <= '⁩';

    // Basic Latin letters, Latin-1 Supplement and Latin Extended letters, combining diacritics, okina/apostrophe modifiers and name punctuation.
    internal static bool IsLatin(string value) => value.All(c =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or ' ' or '-' or '\'' or '.' or '’' or 'ʻ' or 'ʼ' ||
        c is >= 'À' and <= 'ɏ' && c is not '×' and not '÷' ||
        c is >= '̀' and <= 'ͯ' or >= 'Ḁ' and <= 'ỿ');
}
