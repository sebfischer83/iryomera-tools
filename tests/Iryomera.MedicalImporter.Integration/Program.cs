using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.MedicalImporter;

namespace Iryomera.MedicalImporter.Integration;

public sealed record DatasetCase
{
    public required string Input { get; init; }
    public required string Sha256 { get; init; }
    public required ImportOptions Options { get; init; }
    public string? ExpectedNormalizedSha256 { get; init; }
    public int? ExpectedTerms { get; init; }
    public int? ExpectedAssertions { get; init; }
    public SortedDictionary<string, int>? ExpectedErrors { get; init; }
}

public sealed record DatasetReport
{
    public required string Source { get; init; }
    public required string Version { get; init; }
    public required string SourceUrl { get; init; }
    public required string Sha256 { get; init; }
    public long InputBytes { get; init; }
    public int Terms { get; init; }
    public int Assertions { get; init; }
    public int ErrorRecordCount { get; init; }
    public SortedDictionary<string, int> AssertionKindCounts { get; init; } = new(StringComparer.Ordinal);
    public int ObsoleteTerms { get; init; }
    public double FirstImportSeconds { get; init; }
    public double RepeatImportSeconds { get; init; }
    public double ValidationSeconds { get; init; }
    public long PeakProcessWorkingSetBytes { get; init; }
    public long AllocatedBytes { get; init; }
    public required string NormalizedSha256 { get; init; }
    public bool Reproducible { get; init; }
    public bool ExpectationsMatch { get; init; }
    public required string Runtime { get; init; }
    public required string OperatingSystem { get; init; }
    public int ProcessorCount { get; init; }
    public required SortedDictionary<string, int> ErrorCounts { get; init; }
    public required SortedDictionary<string, int> DiagnosticCounts { get; init; }
}

/// <summary>Opt-in real-file test; no download, approval, source-data output or release export.</summary>
public static class FullDatasetRun
{
    public static async Task<DatasetReport> ExecuteAsync(DatasetCase test, CancellationToken ct = default)
    {
        await using (var input = File.OpenRead(test.Input))
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct)) != test.Sha256)
                throw new InvalidDataException("Full dataset SHA-256 differs from pinned case; refusing test.");
        var allocationStart = GC.GetTotalAllocatedBytes();
        var timer = Stopwatch.StartNew();
        var engine = new ImportEngine();
        var first = await engine.ReadAsync(test.Input, test.Options, ct);
        var firstSeconds = timer.Elapsed.TotalSeconds;
        var normalized = await DigestAsync(first, ct);
        timer.Restart();
        var diagnostics = WorkspaceValidation.Validate(new() { Batches = [first] });
        var validationSeconds = timer.Elapsed.TotalSeconds;
        var terms = first.Terms.Count; var assertions = first.Assertions.Count; var obsolete = first.Terms.Count(t => t.Obsolete);
        var bytes = first.InputBytes;
        first = null!; // Make the first graph collectible before loading the repeat.
        timer.Restart();
        var repeat = await engine.ReadAsync(test.Input, test.Options, ct);
        var repeatSeconds = timer.Elapsed.TotalSeconds;
        var reproducible = normalized == await DigestAsync(repeat, ct);
        var errors = Counts(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var expectations = (test.ExpectedNormalizedSha256 is null || test.ExpectedNormalizedSha256 == normalized) &&
            (!test.ExpectedTerms.HasValue || test.ExpectedTerms == terms) &&
            (!test.ExpectedAssertions.HasValue || test.ExpectedAssertions == assertions) &&
            (test.ExpectedErrors is null || test.ExpectedErrors.SequenceEqual(errors));
        return new()
        {
            Source = test.Options.Source, Version = test.Options.SourceVersion, SourceUrl = test.Options.SourceUrl,
            Sha256 = test.Sha256, InputBytes = bytes, Terms = terms, Assertions = assertions, ObsoleteTerms = obsolete,
            ErrorRecordCount = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.RecordId).Distinct(StringComparer.Ordinal).Count(),
            AssertionKindCounts = new(repeat.Assertions.GroupBy(a => a.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count()), StringComparer.Ordinal),
            FirstImportSeconds = firstSeconds, RepeatImportSeconds = repeatSeconds, ValidationSeconds = validationSeconds,
            PeakProcessWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            AllocatedBytes = GC.GetTotalAllocatedBytes() - allocationStart, NormalizedSha256 = normalized,
            Reproducible = reproducible, ExpectationsMatch = expectations,
            Runtime = RuntimeInformation.FrameworkDescription, OperatingSystem = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount, ErrorCounts = errors, DiagnosticCounts = Counts(diagnostics)
        };
    }

    private static SortedDictionary<string, int> Counts(IEnumerable<ImportDiagnostic> diagnostics) =>
        new(diagnostics.GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);

    private static async Task<string> DigestAsync(ImportBatch batch, CancellationToken ct)
    {
        using var hash = SHA256.Create();
        await using var output = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        await ContentJson.WriteAsync(output, batch, ct);
        await output.FlushFinalBlockAsync(ct);
        return Convert.ToHexStringLower(hash.Hash!);
    }
}

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: integration <case.json> <report.json>"); return 1; }
        try
        {
            await using var input = File.OpenRead(args[0]);
            var test = await ContentJson.ReadAsync<DatasetCase>(input);
            var report = await FullDatasetRun.ExecuteAsync(test);
            await using var output = File.Create(args[1]);
            await ContentJson.WriteAsync(output, report);
            Console.WriteLine($"{report.Source}: {report.Terms} terms, {report.Assertions} assertions; reproducible={report.Reproducible}, expectations={report.ExpectationsMatch}; errors={report.ErrorCounts.Values.Sum()}");
            return report.Reproducible && report.ExpectationsMatch ? 0 : 2;
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidOperationException)
        { Console.Error.WriteLine(exception.Message); return 1; }
    }
}
