using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Iryomera.Content.SDK;

namespace Iryomera.NameImporter;

public static class Program
{
    public static Task<int> Main(string[] args) => CreateCommand().Parse(args).InvokeAsync();

    /// <summary>Builds the same command tree used by the installed tool and offline tests.</summary>
    public static RootCommand CreateCommand()
    {
        var root = new RootCommand("Iryomera name importer — local inputs, explicit review and rights, export of SDK name pools.");
        var sources = new Command("sources", "List implemented name source adapters.");
        sources.SetAction(_ => { foreach (var adapter in NameImportEngine.Sources) Console.WriteLine($"{adapter.SourceId} {adapter.Version}: {adapter.Description}"); });
        root.Subcommands.Add(sources);

        var import = new Command("import", "Stage a local source file; never approves names or grants redistribution rights.");
        var source = new Argument<string>("source"); import.Arguments.Add(source);
        var input = Required(import, "--input", "Uncompressed local source file.");
        var version = Required(import, "--source-version", "Exact upstream release or snapshot version.");
        var url = Required(import, "--source-url", "Official source URL, recorded without making requests.");
        var license = Required(import, "--license", "Original data license identifier; not a relicense.");
        var licenseFile = Required(import, "--license-file", "Local file with the full original license terms or notices.");
        var attribution = Required(import, "--attribution", "Required source attribution or citation.");
        var importedAt = Optional(import, "--imported-at", "ISO-8601 timestamp with offset; supply for reproducible re-runs.");
        var ns = Optional(import, "--namespace", "Package namespace.", "names");
        var birthYear = new Option<int?>("--birth-year") { Description = "Birth year of a single-year newborn file (us-ssa)." }; import.Options.Add(birthYear);
        var max = new Option<long>("--max-input-bytes") { DefaultValueFactory = _ => 256L * 1024 * 1024 }; import.Options.Add(max);
        var importWorkspace = Workspace(import);
        import.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var batch = await new NameImportEngine().ReadAsync(parse.GetValue(input)!, new()
            {
                Source = parse.GetValue(source)!, SourceVersion = parse.GetValue(version)!, SourceUrl = parse.GetValue(url)!,
                License = parse.GetValue(license)!, LicenseText = await File.ReadAllTextAsync(parse.GetValue(licenseFile)!, ct),
                Attribution = parse.GetValue(attribution)!, Namespace = parse.GetValue(ns)!, BirthYear = parse.GetValue(birthYear),
                ImportedAt = parse.GetValue(importedAt) is { Length: > 0 } timestamp ? Timestamp(timestamp) : DateTimeOffset.UtcNow, MaxInputBytes = parse.GetValue(max)
            }, ct);
            await new NameWorkspaceStore(parse.GetValue(importWorkspace)!).AddAsync(batch, ct);
            Print(new { batch.Id, batch.InputSha256, batch.InputBytes, batch.AdapterVersion, Records = batch.Records.Count,
                Diagnostics = batch.Diagnostics.GroupBy(d => (d.Severity, d.Code)).Select(g => new { g.Key.Severity, g.Key.Code, Count = g.Count() }) });
            return batch.Diagnostics.Any(x => x.Severity == NameDiagnosticSeverity.Error) ? 2 : 0;
        }));
        root.Subcommands.Add(import);

        var validate = new Command("validate", "Validate the workspace and list import errors.");
        var validationWorkspace = Workspace(validate);
        validate.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var diagnostics = NameWorkspaceValidation.Validate(await ExistingStore(parse.GetValue(validationWorkspace)!).ReadAsync(ct));
            Print(diagnostics); return diagnostics.Count > 0 ? 2 : 0;
        })); root.Subcommands.Add(validate);

        var status = new Command("status", "Summarize batches, review states and redistribution decisions.");
        var statusWorkspace = Workspace(status);
        var listRecords = new Option<bool>("--records") { Description = "Also list every record with its current review status." }; status.Options.Add(listRecords);
        status.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var workspace = await ExistingStore(parse.GetValue(statusWorkspace)!).ReadAsync(ct);
            Print(workspace.Batches.Select(b => new
            {
                b.Id, b.Options.Source, b.Options.SourceVersion, b.Options.License, Redistribution = NameWorkspaceValidation.Rights(workspace, b.Id),
                Review = b.Records.GroupBy(r => NameWorkspaceValidation.Status(workspace, b.Id, r.Id)).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                Records = parse.GetValue(listRecords) ? b.Records.Select(r => new { r.Id, r.Country, r.Part, r.Localized, r.Gender, Status = NameWorkspaceValidation.Status(workspace, b.Id, r.Id) }) : null
            })); return 0;
        })); root.Subcommands.Add(status);

        var review = new Command("review", "Append a review decision for listed records, or for a whole batch with --all.");
        var reviewWorkspace = Workspace(review);
        var reviewBatch = Required(review, "--batch", "Batch ID.");
        var reviewIds = new Option<string[]>("--id") { Description = "Record ID; repeat for several records.", AllowMultipleArgumentsPerToken = true }; review.Options.Add(reviewIds);
        var all = new Option<bool>("--all") { Description = "Apply to every record of the batch; later record decisions still override it." }; review.Options.Add(all);
        var reviewStatus = Required(review, "--status", "unreviewed, inReview, approved or rejected.");
        var reviewer = Required(review, "--reviewer", "Local operator label (not authentication).");
        var reviewAt = Required(review, "--at", "ISO-8601 timestamp with offset.");
        var reviewNote = Required(review, "--note", "Reason and evidence for the decision.");
        review.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var ids = parse.GetValue(reviewIds) ?? [];
            if (parse.GetValue(all) == ids.Length > 0) throw new ArgumentException("Use either --all or one or more --id values.");
            await ExistingStore(parse.GetValue(reviewWorkspace)!).UpdateAsync(workspace =>
            {
                var batch = workspace.Batches.SingleOrDefault(x => x.Id == parse.GetValue(reviewBatch)) ?? throw new ArgumentException("Unknown batch ID.");
                IEnumerable<string?> targets = ids.Length == 0 ? new string?[] { null } : ids.Distinct(StringComparer.Ordinal).Select(id => (string?)id);
                foreach (var id in targets)
                    workspace.Reviews.Add(new()
                    {
                        BatchId = batch.Id, RecordId = id, Status = ParseEnum<ReviewStatus>(parse.GetValue(reviewStatus)!), Reviewer = parse.GetValue(reviewer)!,
                        At = Timestamp(parse.GetValue(reviewAt)!), Note = parse.GetValue(reviewNote)!
                    });
            }, ct); return 0;
        })); root.Subcommands.Add(review);

        var rights = new Command("rights", "Append a batch redistribution decision; permission needs evidence for this exact source release.");
        var rightsWorkspace = Workspace(rights);
        var rightsBatch = Required(rights, "--batch", "Batch ID.");
        var decision = Required(rights, "--decision", "undecided, permitted or blocked.");
        var evidence = Optional(rights, "--permission-evidence", "Terms or permission covering this exact release and intended use.");
        var rightsReviewer = Required(rights, "--reviewer", "Local operator label (not authentication).");
        var rightsAt = Required(rights, "--at", "ISO-8601 timestamp with offset.");
        var rightsNote = Required(rights, "--note", "Basis and restrictions of the decision.");
        rights.SetAction((parse, ct) => GuardAsync(async () =>
        {
            await ExistingStore(parse.GetValue(rightsWorkspace)!).UpdateAsync(workspace =>
            {
                var batch = workspace.Batches.SingleOrDefault(x => x.Id == parse.GetValue(rightsBatch)) ?? throw new ArgumentException("Unknown batch ID.");
                workspace.Rights.Add(new()
                {
                    BatchId = batch.Id, Decision = ParseEnum<RedistributionDecision>(parse.GetValue(decision)!), PermissionEvidence = parse.GetValue(evidence),
                    Reviewer = parse.GetValue(rightsReviewer)!, At = Timestamp(parse.GetValue(rightsAt)!), Note = parse.GetValue(rightsNote)!
                });
            }, ct); return 0;
        })); root.Subcommands.Add(rights);

        var export = new Command("export", "Export approved records of permitted batches as an SDK name pool package with en/de labels and notices.");
        var exportWorkspace = Workspace(export);
        var output = Required(export, "--output", "New output directory; existing paths are never overwritten.");
        var packageVersion = Required(export, "--version", "Package version (SemVer).");
        export.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var report = await new NameExporter().ExportAsync(await ExistingStore(parse.GetValue(exportWorkspace)!).ReadAsync(ct), parse.GetValue(output)!, parse.GetValue(packageVersion)!, ct);
            Print(report); return 0;
        })); root.Subcommands.Add(export);
        return root;
    }

    private static Option<string> Required(Command command, string name, string description)
    { var option = new Option<string>(name) { Required = true, Description = description }; command.Options.Add(option); return option; }
    private static Option<string> Optional(Command command, string name, string description, string? defaultValue = null)
    { var option = new Option<string>(name) { Description = description, DefaultValueFactory = _ => defaultValue! }; command.Options.Add(option); return option; }
    private static Option<string> Workspace(Command command) => Optional(command, "--workspace", "Local atomic JSON workspace.", ".iryomera/names-workspace.json");
    private static NameWorkspaceStore ExistingStore(string path) => File.Exists(path) ? new(path) : throw new FileNotFoundException("Workspace does not exist. Import source data first.");
    private static DateTimeOffset Timestamp(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) &&
        value.Contains('T') && (value.EndsWith('Z') || value.LastIndexOf('+') > value.IndexOf('T') || value.LastIndexOf('-') > value.IndexOf('T')) && timestamp != default
        ? timestamp : throw new ArgumentException("Use a nonzero ISO-8601 timestamp with an explicit UTC offset.");
    private static T ParseEnum<T>(string value) where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _)
        ? parsed : throw new ArgumentException($"Unknown {typeof(T).Name}: {value}.");
    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, ContentJson.Options));
    private static async Task<int> GuardAsync(Func<Task<int>> action)
    {
        try { return await action(); }
        catch (OperationCanceledException) { Console.Error.WriteLine("Canceled; no partial workspace or release was committed."); return 130; }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or CsvHelper.CsvHelperException or FormatException)
        { Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}"); return 1; }
    }
}
