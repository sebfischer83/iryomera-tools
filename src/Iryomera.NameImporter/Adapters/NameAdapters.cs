using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Iryomera.Content.SDK;

namespace Iryomera.NameImporter.Adapters;

public interface INameAdapter
{
    string SourceId { get; }
    /// <summary>Bump whenever normalized output rules change; it is part of the batch identity.</summary>
    string Version { get; }
    string Description { get; }
    /// <summary>Input format recorded in the batch: csv or json.</summary>
    string Format => "csv";
    /// <summary>False for sources whose license is not safe enough to ship; their batches can never be permitted or exported.</summary>
    bool Redistributable { get; }
    void ValidateOptions(NameImportOptions options);
    Task ReadAsync(Stream input, NameAdapterContext context, CancellationToken cancellationToken);
}

internal static class Csv
{
    internal static StreamReader Reader(Stream input) => new(input, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true, 65536, leaveOpen: true);
    internal static CsvConfiguration Configuration(bool header) => new(CultureInfo.InvariantCulture) { HasHeaderRecord = header, TrimOptions = TrimOptions.None, DetectDelimiter = false };
    internal static JsonElement Text(string value) => NameIdentity.Json(value);
}

/// <summary>
/// US Social Security Administration national baby names, one uncompressed yobYYYY.txt file per batch: rows Name,Sex,Count
/// without header. Sex is the binary value reported by the source. Counts are kept; no rank or proportion is derived.
/// </summary>
public sealed class UsSsaAdapter : INameAdapter
{
    public string SourceId => "us-ssa";
    public string Version => "1.0.0";
    public string Description => "US SSA national baby names yobYYYY.txt (Name,Sex,Count); requires --birth-year.";
    public bool Redistributable => true;

    public void ValidateOptions(NameImportOptions options)
    {
        if (options.BirthYear is not { } year || year < 1880) throw new ArgumentException("us-ssa requires --birth-year (1880 or later) for the yobYYYY.txt file.");
    }

    public async Task ReadAsync(Stream input, NameAdapterContext context, CancellationToken ct)
    {
        using var reader = Csv.Reader(input);
        using var csv = new CsvReader(reader, Csv.Configuration(header: false));
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            var row = "row " + csv.Parser.Row.ToString(CultureInfo.InvariantCulture);
            var values = csv.Parser.Record ?? [];
            if (values.Length != 3) { context.Diagnostic(NameDiagnosticSeverity.Error, "row-shape", row, "Expected exactly Name,Sex,Count."); continue; }
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal) { ["name"] = Csv.Text(values[0]), ["sex"] = Csv.Text(values[1]), ["count"] = Csv.Text(values[2]) };
            NameGender? gender = values[1] switch { "F" => NameGender.Female, "M" => NameGender.Male, _ => null };
            if (gender is null) { context.Diagnostic(NameDiagnosticSeverity.Error, "gender", row, $"Unknown sex value '{values[1]}'; record not staged."); continue; }
            if (!long.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
            { context.Diagnostic(NameDiagnosticSeverity.Error, "count", row, "Count must be a positive integer; record not staged."); continue; }
            context.Record(row, "US", NamePart.Given, values[0], fields, gender, count: count, birthYear: context.Options.BirthYear);
        }
    }
}

/// <summary>
/// Local analysis only: batches from this source can never be permitted or exported (see Redistributable).
/// sigpwned popular-names-by-country CSV (common-forenames-by-country.csv or common-surnames-by-country.csv). The source
/// derives from Wikipedia lists; romanizations may be machine translations and are marked unverified. Regional lists use
/// free-text regions, which are kept in the source fields but not mapped to ISO 3166-2.
/// </summary>
public sealed class SigpwnedAdapter : INameAdapter
{
    private const string RomanizationOrigin = "sigpwned popular-names-by-country (source list or machine translation; unverified)";
    public string SourceId => "names-sigpwned";
    public string Version => "1.0.0";
    public string Description => "sigpwned common-forenames-by-country.csv or common-surnames-by-country.csv; local analysis only, never released.";
    /// <summary>Declared CC0 but derived from Wikipedia lists (CC BY-SA) with partly machine-translated romanizations. Owner decision 2026-10-17.</summary>
    public bool Redistributable => false;
    public void ValidateOptions(NameImportOptions options) { }

    public async Task ReadAsync(Stream input, NameAdapterContext context, CancellationToken ct)
    {
        using var reader = Csv.Reader(input);
        using var csv = new CsvReader(reader, Csv.Configuration(header: true));
        if (!await csv.ReadAsync() || !csv.ReadHeader() || csv.HeaderRecord is not { } header) throw new InvalidDataException("Missing CSV header.");
        string[] forenames = ["Country", "Region", "Year", "Index", "Gender", "Localized Name", "Romanized Name"];
        string[] surnames = ["Country", "Rank", "Localized Name", "Romanized Name", "Count", "Percent"];
        var part = forenames.All(header.Contains) ? NamePart.Given : surnames.All(header.Contains) ? NamePart.Family
            : throw new InvalidDataException("Header matches neither the sigpwned forename nor surname CSV.");
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            var row = "row " + csv.Parser.Row.ToString(CultureInfo.InvariantCulture);
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var column in header) fields[column] = Csv.Text(csv.GetField(column) ?? "");
            string Field(string column) => csv.GetField(column) ?? "";
            if (part == NamePart.Given)
            {
                if (Field("Region").Length > 0)
                    context.Diagnostic(NameDiagnosticSeverity.Warning, "region-not-mapped", row, "Free-text regional list kept in source fields and staged country-wide.");
                NameGender gender = Field("Gender") switch { "F" => NameGender.Female, "M" => NameGender.Male, _ => NameGender.Unknown };
                if (gender == NameGender.Unknown) context.Diagnostic(NameDiagnosticSeverity.Warning, "gender-unknown", row, $"Gender '{Field("Gender")}' staged as unknown.");
                context.Record(row, Field("Country"), part, Field("Localized Name"), fields, gender, Field("Romanized Name"), RomanizationOrigin,
                    rank: Positive(Field("Index")), birthYear: Year(Field("Year"), context, row));
            }
            else
            {
                var proportion = decimal.TryParse(Field("Percent"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var p) && p is >= 0 and <= 1 ? p : (decimal?)null;
                if (proportion is null && Field("Percent").Length > 0) context.Diagnostic(NameDiagnosticSeverity.Warning, "proportion", row, "Percent is not a fraction within [0,1]; not staged.");
                context.Record(row, Field("Country"), part, Field("Localized Name"), fields, romanized: Field("Romanized Name"), romanizationOrigin: RomanizationOrigin,
                    rank: Positive(Field("Rank")), count: long.TryParse(Field("Count"), NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : null, proportion: proportion);
            }
        }
    }

    private static int? Positive(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
    private static int? Year(string value, NameAdapterContext context, string row)
    {
        if (value.Length == 0) return null;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year is >= 1000 and <= 9999) return year;
        context.Diagnostic(NameDiagnosticSeverity.Warning, "year", row, "Year is not a four-digit year; no cohort staged."); return null;
    }
}
