using System.Globalization;
using System.Text.Json;
using CsvHelper;
using Iryomera.Content.SDK;

namespace Iryomera.NameImporter.Adapters;

/// <summary>
/// US Census Bureau 2010 frequently occurring surnames, the uncompressed CSV (header name,rank,count,prop100k,...).
/// Race and Hispanic-origin percentage columns are never read or staged (ADR 0011). Source names are uppercase; they are
/// converted to a capitalized form (first letter and letters after '-', apostrophe or space), which can differ from real
/// spellings such as McDonald; the original value stays in the source fields. The aggregate "ALL OTHER NAMES" row is skipped.
/// </summary>
public sealed class UsCensusSurnamesAdapter : INameAdapter
{
    private static readonly string[] Kept = ["name", "rank", "count", "prop100k"];
    public string SourceId => "us-census-surnames";
    public string Version => "1.0.0";
    public string Description => "US Census 2010 surnames CSV (name,rank,count,prop100k); ethnicity columns are never read.";
    public bool Redistributable => true;
    public void ValidateOptions(NameImportOptions options) { }

    public async Task ReadAsync(Stream input, NameAdapterContext context, CancellationToken ct)
    {
        using var reader = Csv.Reader(input);
        using var csv = new CsvReader(reader, Csv.Configuration(header: true));
        if (!await csv.ReadAsync() || !csv.ReadHeader() || csv.HeaderRecord is not { } header || !Kept.All(header.Contains))
            throw new InvalidDataException("Expected the Census 2010 surname header name,rank,count,prop100k.");
        var capitalized = false;
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            var row = "row " + csv.Parser.Row.ToString(CultureInfo.InvariantCulture);
            var name = csv.GetField("name") ?? "";
            if (name == "ALL OTHER NAMES") { context.Diagnostic(NameDiagnosticSeverity.Information, "aggregate-row", row, "Aggregate row skipped."); continue; }
            // Only these columns are kept; ethnicity columns are deliberately excluded from staging.
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var column in Kept) fields[column] = Csv.Text(csv.GetField(column) ?? "");
            if (!long.TryParse(csv.GetField("count"), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
            { context.Diagnostic(NameDiagnosticSeverity.Error, "count", row, "Count must be a positive integer; record not staged."); continue; }
            var proportion = decimal.TryParse(csv.GetField("prop100k"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var per100k) && per100k is >= 0 and <= 100000
                ? per100k / 100000m : (decimal?)null;
            var rank = int.TryParse(csv.GetField("rank"), NumberStyles.None, CultureInfo.InvariantCulture, out var r) && r > 0 ? r : (int?)null;
            var display = Capitalize(name);
            capitalized |= display != name;
            context.Record(row, "US", NamePart.Family, display, fields, rank: rank, count: count, proportion: proportion);
        }
        if (capitalized) context.Diagnostic(NameDiagnosticSeverity.Information, "case-normalized", "batch", "Uppercase source names converted to capitalized form; originals kept in source fields.");
    }

    internal static string Capitalize(string value)
    {
        var chars = value.ToLowerInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (i == 0 || chars[i - 1] is '-' or '\'' or ' ') chars[i] = char.ToUpperInvariant(chars[i]);
        return new string(chars);
    }
}

/// <summary>
/// Statistik Austria first names of live births (OGDEXT_VORNAMEN_1, semicolon CSV: year, district, sex, normalized name,
/// count). Rows are summed per year, sex and name across districts; rare names appear only in Austria-wide rows, so no
/// name is counted twice. The original district rows stay in the source fields. Sex codes: 1 male, 2 female.
/// </summary>
public sealed class AtStatistikAdapter : INameAdapter
{
    private static readonly string[] Columns = ["C-JAHR-0", "C-WOHNBEZIRK-0", "C-GESCHLECHT-0", "F-VORNAME_NORMALISIERT", "F-ANZAHL_LGEB"];
    public string SourceId => "at-statistik";
    public string Version => "1.0.0";
    public string Description => "Statistik Austria OGDEXT_VORNAMEN_1.csv (first names of live births by year, district and sex).";
    public bool Redistributable => true;
    public void ValidateOptions(NameImportOptions options) { }

    public async Task ReadAsync(Stream input, NameAdapterContext context, CancellationToken ct)
    {
        using var reader = Csv.Reader(input);
        using var csv = new CsvReader(reader, Csv.Configuration(header: true) with { Delimiter = ";" });
        if (!await csv.ReadAsync() || !csv.ReadHeader() || csv.HeaderRecord is not { } header || !Columns.All(header.Contains))
            throw new InvalidDataException("Expected the OGDEXT_VORNAMEN_1 header " + string.Join(";", Columns) + ".");
        var groups = new SortedDictionary<(int Year, int Sex, string Name), (long Count, List<(string District, string Count)> Rows, string FirstRow)>(
            Comparer<(int Year, int Sex, string Name)>.Create((a, b) => a.Year != b.Year ? a.Year.CompareTo(b.Year) : a.Sex != b.Sex ? a.Sex.CompareTo(b.Sex) : string.CompareOrdinal(a.Name, b.Name)));
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            var row = "row " + csv.Parser.Row.ToString(CultureInfo.InvariantCulture);
            var values = Columns.Select(c => csv.GetField(c) ?? "").ToArray();
            if (!int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year is < 1000 or > 9999)
            { context.Diagnostic(NameDiagnosticSeverity.Error, "year", row, "Year must be a four-digit year; row not staged."); continue; }
            if (values[2] is not ("1" or "2")) { context.Diagnostic(NameDiagnosticSeverity.Error, "gender", row, $"Unknown sex code '{values[2]}'; row not staged."); continue; }
            if (!long.TryParse(values[4], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
            { context.Diagnostic(NameDiagnosticSeverity.Error, "count", row, "Count must be a positive integer; row not staged."); continue; }
            var key = (year, values[2] == "1" ? 1 : 2, values[3]);
            (long Count, List<(string District, string Count)> Rows, string FirstRow) group = groups.TryGetValue(key, out var existing) ? existing : (0L, [], row);
            if (group.Rows.Any(r => r.District == values[1])) { context.Diagnostic(NameDiagnosticSeverity.Error, "duplicate-row", row, "Repeated district row for the same year, sex and name."); continue; }
            group.Rows.Add((values[1], values[4]));
            groups[key] = (group.Count + count, group.Rows, group.FirstRow);
        }
        foreach (var ((year, sex, name), group) in groups)
        {
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["year"] = Csv.Text(year.ToString(CultureInfo.InvariantCulture)), ["sex"] = Csv.Text(sex.ToString(CultureInfo.InvariantCulture)), ["name"] = Csv.Text(name),
                ["districtRows"] = NameIdentity.Json(group.Rows.OrderBy(r => r.District, StringComparer.Ordinal).Select(r => new { district = r.District, count = r.Count }))
            };
            context.Record(group.FirstRow, "AT", NamePart.Given, name, fields, sex == 1 ? NameGender.Male : NameGender.Female, count: group.Count, birthYear: year);
        }
    }
}
