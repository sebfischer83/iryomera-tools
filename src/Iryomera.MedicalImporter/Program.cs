using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text.Json;
using Iryomera.Content.SDK;

namespace Iryomera.MedicalImporter;

public static class Program
{
    public static Task<int> Main(string[] args) => CreateCommand().Parse(args).InvokeAsync();

    /// <summary>Builds the same command tree used by the installed tool and offline integration tests.</summary>
    public static RootCommand CreateCommand()
    {
        var root = new RootCommand("Iryomera medical importer — local inputs, explicit source rights, no automatic medical approval.");
        var sources = new Command("sources", "Inspect supported adapters.");
        var list = new Command("list", "List implemented source formats.");
        list.SetAction(_ =>
        {
            foreach (var adapter in ImportEngine.Sources) Console.WriteLine($"{adapter.SourceId}: {string.Join(", ", adapter.Formats)}");
        });
        sources.Subcommands.Add(list); root.Subcommands.Add(sources);

        var import = new Command("import", "Stage source data; never grants medical approval or redistribution rights.");
        var source = new Argument<string>("source"); import.Arguments.Add(source);
        var input = Required(import, "--input", "Uncompressed local source file.");
        var version = Required(import, "--source-version", "Exact upstream release version.");
        var url = Required(import, "--source-url", "Official source release URL, recorded without making requests.");
        var license = Required(import, "--license", "Original data license identifier; not a request to relicense it.");
        var licenseFile = Required(import, "--license-file", "Local file containing full original license terms/notices.");
        var attribution = Required(import, "--attribution", "Required source attribution/citation notice.");
        var importedAt = Optional(import, "--imported-at", "ISO-8601 UTC timestamp; supply for reproducible re-runs.");
        var retrievedAt = Optional(import, "--retrieved-at", "Actual source snapshot retrieval timestamp; required for Wikidata.");
        var ns = Optional(import, "--namespace", "Package namespace.", "medical");
        var format = Optional(import, "--format", "auto, json, obo, tsv, csv or xml.", "auto");
        var max = new Option<long>("--max-input-bytes") { DefaultValueFactory = _ => 512 * 1024 * 1024 }; import.Options.Add(max);
        var importWorkspace = Workspace(import);
        import.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var batch = await new ImportEngine().ReadAsync(parse.GetValue(input)!, new()
            {
                Source = parse.GetValue(source)!, SourceVersion = parse.GetValue(version)!, SourceUrl = parse.GetValue(url)!,
                License = parse.GetValue(license)!, LicenseText = await File.ReadAllTextAsync(parse.GetValue(licenseFile)!, ct),
                Attribution = parse.GetValue(attribution)!, Namespace = parse.GetValue(ns)!, Format = parse.GetValue(format)!,
                RetrievedAt = parse.GetValue(retrievedAt) is { Length: > 0 } retrieval ? Timestamp(retrieval) : null,
                ImportedAt = parse.GetValue(importedAt) is { Length: > 0 } timestamp ? Timestamp(timestamp) : DateTimeOffset.UtcNow,
                MaxInputBytes = parse.GetValue(max)
            }, ct);
            await new WorkspaceStore(parse.GetValue(importWorkspace)!).AddAsync(batch, ct);
            Print(new { batch.Id, batch.InputSha256, batch.InputBytes, batch.DetectedFormat, batch.ObservedSourceVersion,
                Definitions = batch.Terms.Count, Assertions = batch.Assertions.Count, batch.Diagnostics });
            return batch.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error) ? 2 : 0;
        }));
        root.Subcommands.Add(import);

        var validate = new Command("validate", "Validate staged definitions, source assertions, reviews and references.");
        var validationWorkspace = Workspace(validate);
        validate.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var workspace = await ExistingStore(parse.GetValue(validationWorkspace)!).ReadAsync(ct);
            var diagnostics = WorkspaceValidation.Validate(workspace); Print(diagnostics);
            return diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error) ? 2 : 0;
        })); root.Subcommands.Add(validate);

        var status = new Command("status", "List batch/record IDs, source labels and current review states.");
        var statusWorkspace = Workspace(status);
        status.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var workspace = await ExistingStore(parse.GetValue(statusWorkspace)!).ReadAsync(ct);
            var reviewIndex = WorkspaceValidation.ReviewIndex(workspace);
            ReviewStatus Current(string batch, string id) => reviewIndex.GetValueOrDefault((batch, id))?.Status ?? ReviewStatus.Unreviewed;
            Print(workspace.Batches.Select(b => new
            {
                b.Id, b.Options.Source, b.Options.SourceVersion,
                Redistribution = workspace.Rights.LastOrDefault(x => x.BatchId == b.Id && x.RecordId is null)?.Decision ?? RedistributionDecision.Undecided,
                Terms = b.Terms.Select(t => new { t.Id, t.ExternalId, t.Label, t.Kind, t.Obsolete, Status = Current(b.Id, t.Id) }),
                Assertions = b.Assertions.Select(a => new { a.Id, a.Kind, a.Predicate, a.SubjectExternalId, a.ObjectExternalId, Status = Current(b.Id, a.Id) })
            })); return 0;
        })); root.Subcommands.Add(status);

        var matches = new Command("matches", "Find label/synonym candidates for human review; does not merge or infer relationships.");
        var matchWorkspace = Workspace(matches);
        var identifiersOnly = new Option<bool>("--identifiers") { Description = "Compare explicit external identifiers only; never merges or infers equivalence." }; matches.Options.Add(identifiersOnly);
        var limit = new Option<int>("--limit") { DefaultValueFactory = _ => 1000 }; matches.Options.Add(limit);
        matches.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var workspace = await ExistingStore(parse.GetValue(matchWorkspace)!).ReadAsync(ct);
            if (parse.GetValue(identifiersOnly)) Print(IdentifierMatching.Candidates(workspace, parse.GetValue(limit)));
            else Print(TermMatching.Candidates(workspace, parse.GetValue(limit))); return 0;
        })); root.Subcommands.Add(matches);

        var review = new Command("review", "Record an explicit operator review; import itself never approves records.");
        var reviewWorkspace = Workspace(review);
        var reviewBatch = Required(review, "--batch", "Batch ID from status output.");
        var recordId = Optional(review, "--id", "One record ID (mutually exclusive with --all).");
        var all = new Option<bool>("--all") { Description = "Explicitly review every term/assertion in the selected batch." }; review.Options.Add(all);
        var reviewStatus = Required(review, "--status", "unreviewed, inReview, approved or rejected.");
        var reviewer = Required(review, "--reviewer", "Reviewer identity label (local trust, no authentication).");
        var reviewAt = Required(review, "--at", "ISO-8601 timestamp.");
        var reviewNote = Required(review, "--note", "Reason/evidence for the decision.");
        review.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var id = parse.GetValue(recordId); var every = parse.GetValue(all);
            if (every == !string.IsNullOrEmpty(id)) throw new ArgumentException("Specify exactly one of --id or --all.");
            await ExistingStore(parse.GetValue(reviewWorkspace)!).UpdateAsync(workspace =>
            {
                var batch = workspace.Batches.SingleOrDefault(x => x.Id == parse.GetValue(reviewBatch)) ?? throw new ArgumentException("Unknown batch ID.");
                if (every) WorkspaceValidation.ReviewBatch(workspace, batch.Id, ParseEnum<ReviewStatus>(parse.GetValue(reviewStatus)!), parse.GetValue(reviewer)!, Timestamp(parse.GetValue(reviewAt)!), parse.GetValue(reviewNote)!);
                else WorkspaceValidation.Review(workspace, batch.Id, id!, ParseEnum<ReviewStatus>(parse.GetValue(reviewStatus)!), parse.GetValue(reviewer)!, Timestamp(parse.GetValue(reviewAt)!), parse.GetValue(reviewNote)!);
                return Task.CompletedTask;
            }, ct); return 0;
        })); root.Subcommands.Add(review);

        var bulk = new Command("review-bulk", "Preview/apply controlled review; ineligible/excluded records are audited and rejected on approval runs.");
        var bulkWorkspace = Workspace(bulk);
        var bulkBatch = Required(bulk, "--batch", "Batch ID.");
        var bulkStatus = Required(bulk, "--status", "Requested review status.");
        var bulkReviewer = Required(bulk, "--reviewer", "Reviewer identity.");
        var bulkAt = Required(bulk, "--at", "ISO-8601 timestamp.");
        var bulkNote = Required(bulk, "--note", "Basis for this controlled review.");
        var exclusionsFile = Optional(bulk, "--exclusions", "JSON object mapping excluded record IDs to nonempty reasons.");
        var apply = new Option<bool>("--apply") { Description = "Commit the preview; omission is read-only." }; bulk.Options.Add(apply);
        bulk.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var exclusions = parse.GetValue(exclusionsFile) is { Length: > 0 } path
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(path, ct), ContentJson.Options)
                    ?? throw new ArgumentException("Exclusions must be a JSON object.") : null;
            BulkReviewAudit Run(ImportWorkspace workspace, bool commit) => commit
                ? BulkReview.Apply(workspace, parse.GetValue(bulkBatch)!, ParseEnum<ReviewStatus>(parse.GetValue(bulkStatus)!), parse.GetValue(bulkReviewer)!, Timestamp(parse.GetValue(bulkAt)!), parse.GetValue(bulkNote)!, exclusions)
                : BulkReview.Preview(workspace, parse.GetValue(bulkBatch)!, ParseEnum<ReviewStatus>(parse.GetValue(bulkStatus)!), parse.GetValue(bulkReviewer)!, Timestamp(parse.GetValue(bulkAt)!), parse.GetValue(bulkNote)!, exclusions);
            var store = ExistingStore(parse.GetValue(bulkWorkspace)!);
            if (parse.GetValue(apply)) await store.UpdateAsync(w => { Print(Run(w, true)); return Task.CompletedTask; }, ct);
            else Print(Run(await store.ReadAsync(ct), false));
            return 0;
        })); root.Subcommands.Add(bulk);

        var rights = new Command("rights", "Record a reviewed source-specific redistribution decision; does not change source licenses.");
        var rightsWorkspace = Workspace(rights);
        var rightsBatch = Required(rights, "--batch", "Batch ID.");
        var decision = Required(rights, "--decision", "undecided, permitted or blocked.");
        var rightsReviewer = Required(rights, "--reviewer", "Rights reviewer.");
        var rightsAt = Required(rights, "--at", "ISO-8601 timestamp.");
        var rightsRecord = Optional(rights, "--id", "Optional record ID; batch decisions never override additional record terms.");
        var permission = Optional(rights, "--permission-evidence", "Reference to the permission/terms and intended use covering this exact record; avoid confidential documents in public releases.");
        var rightsNote = Required(rights, "--note", "Basis, restrictions and extra permissions for this decision.");
        rights.SetAction((parse, ct) => GuardAsync(async () =>
        {
            await ExistingStore(parse.GetValue(rightsWorkspace)!).UpdateAsync(workspace =>
            {
                var batch = workspace.Batches.SingleOrDefault(x => x.Id == parse.GetValue(rightsBatch)) ?? throw new ArgumentException("Unknown batch ID.");
                RecordRights.Decide(workspace, new()
                {
                    BatchId = batch.Id, RecordId = parse.GetValue(rightsRecord), PermissionEvidence = parse.GetValue(permission),
                    Decision = ParseEnum<RedistributionDecision>(parse.GetValue(decision)!), Reviewer = parse.GetValue(rightsReviewer)!,
                    At = Timestamp(parse.GetValue(rightsAt)!), Note = parse.GetValue(rightsNote)!
                });
                return Task.CompletedTask;
            }, ct); return 0;
        })); root.Subcommands.Add(rights);

        var export = new Command("export", "Export an approved, reference-consistent JSON release and original license notices.");
        var exportWorkspace = Workspace(export);
        var output = Required(export, "--output", "New output directory; existing paths are never overwritten.");
        var requiredLanguages = Optional(export, "--require-languages", "Optional comma-separated translation release gate, e.g. en,de.");
        export.SetAction((parse, ct) => GuardAsync(async () =>
        {
            var workspace = await ExistingStore(parse.GetValue(exportWorkspace)!).ReadAsync(ct);
            await new ReleaseExporter().ExportAsync(workspace, parse.GetValue(output)!, parse.GetValue(requiredLanguages)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ct);
            Console.WriteLine("Approved release exported. Original source licenses and medical review remain distinct."); return 0;
        })); root.Subcommands.Add(export);
        return root;
    }

    private static Option<string> Required(Command command, string name, string description)
    { var option = new Option<string>(name) { Required = true, Description = description }; command.Options.Add(option); return option; }
    private static Option<string> Optional(Command command, string name, string description, string? defaultValue = null)
    { var option = new Option<string>(name) { Description = description, DefaultValueFactory = _ => defaultValue! }; command.Options.Add(option); return option; }
    private static Option<string> Workspace(Command command) => Optional(command, "--workspace", "Local atomic JSON workspace.", ".iryomera/workspace.json");
    private static WorkspaceStore ExistingStore(string path) => File.Exists(path) ? new(path) : throw new FileNotFoundException("Workspace does not exist. Import source data first.");
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
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or JsonException or System.Xml.XmlException or CsvHelper.CsvHelperException or KeyNotFoundException or FormatException)
        { Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}"); return 1; }
    }
}
