using System.Text.Json;
using System.Text.Json.Nodes;
using Iryomera.Content.SDK;
using Iryomera.MedicalImporter.Integration;
using Xunit;

namespace Iryomera.MedicalImporter.Tests;

public sealed class ReviewRemediationTests
{
    private static RightsDecision Rights(ImportBatch batch, string? id = null, RedistributionDecision decision = RedistributionDecision.Permitted, string? evidence = null) => new()
    { BatchId = batch.Id, RecordId = id, Decision = decision, Reviewer = "Synthetic rights reviewer", At = TestData.ReviewTime, Note = "Own synthetic test input only", PermissionEvidence = evidence };

    private static async Task<ImportBatch> RestrictedBatch(TestData data, string database = "OMIM:999999", string evidence = "IEA")
    {
        var lines = await File.ReadAllLinesAsync(TestData.Fixture("hpoa.tsv"));
        var row = lines[^1].Split('\t'); row[0] = database; row[5] = evidence; lines[^1] = string.Join('\t', row);
        await File.WriteAllLinesAsync(data.PathFor("restricted.tsv"), lines);
        return await new ImportEngine().ReadAsync(data.PathFor("restricted.tsv"), TestData.Options("hpo-annotations"));
    }

    [Theory]
    [InlineData("OMIM:999999", "PCS")]
    [InlineData("SYNTHETIC:amber", "IEA")]
    public async Task BatchPermissionCannotReleaseAdditionalTermsInLegacyWorkspaces(string disease, string evidence)
    {
        using var data = new TestData(); var restricted = await RestrictedBatch(data, disease, evidence);
        var workspace = new ImportWorkspace { Batches = [await TestData.Import("hpo", "hpo.json"), restricted] };
        TestData.Approve(workspace); // Legacy global permission; no new flags are required on raw records.
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReleaseExporter().ExportAsync(workspace, data.PathFor("out")));
        Assert.False(Directory.Exists(data.PathFor("out")));
        foreach (var id in restricted.Terms.Select(t => t.Id).Concat(restricted.Assertions.Select(a => a.Id)))
        {
            Assert.Throws<ArgumentException>(() => RecordRights.Decide(workspace, Rights(restricted, id)));
            RecordRights.Decide(workspace, Rights(restricted, id, evidence: "SYNTHETIC:permission-for-test-record-only"));
        }
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        var report = await TestData.Read<ReleaseReport>(data.PathFor("out/report.json"));
        Assert.Equal(2, report.Rights.Count(r => r.RecordId is not null));
        Assert.All(report.Rights.Where(r => r.RecordId is not null), r => Assert.NotNull(r.PermissionEvidence));
    }

    [Fact]
    public async Task RecordDenialOverridesBatchAndLaterRevocationBlocksExport()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace(); TestData.Approve(workspace);
        var batch = workspace.Batches[0]; var id = batch.Terms[0].Id;
        RecordRights.Decide(workspace, Rights(batch, id, RedistributionDecision.Blocked));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReleaseExporter().ExportAsync(workspace, data.PathFor("out")));
        RecordRights.Decide(workspace, Rights(batch, id) with { At = TestData.ReviewTime.AddHours(1) });
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        Assert.Throws<ArgumentException>(() => RecordRights.Decide(workspace, Rights(batch, id)));
    }

    [Fact]
    public async Task InvalidOrBackdatedRightsDecisionsFailValidation()
    {
        var batch = await TestData.Import("hpo", "hpo.json"); var workspace = new ImportWorkspace { Batches = [batch] };
        Assert.Throws<ArgumentException>(() => RecordRights.Decide(workspace, Rights(batch, "unknown")));
        workspace.Rights.Add(Rights(batch) with { At = TestData.ImportTime.AddDays(-1) });
        Assert.Contains(WorkspaceValidation.Validate(workspace), d => d.Code == "rights-audit");
    }

    [Fact]
    public async Task BulkPreviewAndApplyExcludeObsoleteTermsAndAuditEveryException()
    {
        var batch = await TestData.Import("hpo", "hpo.obo"); var workspace = new ImportWorkspace { Batches = [batch] };
        var audit = BulkReview.Preview(workspace, batch.Id, ReviewStatus.Approved, "Synthetic", TestData.ReviewTime, "Test only");
        Assert.Empty(workspace.Reviews); Assert.Empty(workspace.BulkReviews);
        Assert.Contains(audit.Exceptions, e => e.Reason == "obsolete-term");
        Assert.NotEmpty(audit.SelectedIds);
        var result = BulkReview.Apply(workspace, batch.Id, ReviewStatus.Approved, "Synthetic", TestData.ReviewTime, "Test only");
        Assert.Equal(TestData.Json(audit), TestData.Json(result)); Assert.Single(workspace.BulkReviews);
        Assert.All(result.Exceptions, e => Assert.Equal(ReviewStatus.Rejected, WorkspaceValidation.Status(workspace, batch.Id, e.RecordId)));
        Assert.All(result.SelectedIds, id => Assert.Equal(ReviewStatus.Approved, WorkspaceValidation.Status(workspace, batch.Id, id)));
    }

    [Fact]
    public async Task BulkExplicitExclusionRevokesEarlierApprovalAndInvalidExclusionsAreAtomic()
    {
        var batch = await TestData.Import("hpo", "hpo.json"); var workspace = new ImportWorkspace { Batches = [batch] }; TestData.Approve(workspace);
        var before = TestData.Json(workspace);
        Assert.Throws<ArgumentException>(() => BulkReview.Apply(workspace, batch.Id, ReviewStatus.Approved, "Synthetic", TestData.ReviewTime, "Test", new Dictionary<string, string> { ["unknown"] = "Not present" }));
        Assert.Equal(before, TestData.Json(workspace));
        var id = batch.Terms[0].Id;
        var audit = BulkReview.Apply(workspace, batch.Id, ReviewStatus.Approved, "Synthetic", TestData.ReviewTime, "Test", new Dictionary<string, string> { [id] = "Outside selected scope" });
        Assert.Contains(audit.Exceptions, e => e.RecordId == id && e.Reason == "Outside selected scope");
        Assert.Equal(ReviewStatus.Rejected, WorkspaceValidation.Status(workspace, batch.Id, id));
    }

    [Fact]
    public async Task BulkReviewExcludesUnresolvedAssertionsAndAdditionalRights()
    {
        using var data = new TestData(); var batch = await RestrictedBatch(data);
        var workspace = new ImportWorkspace { Batches = [batch] };
        var audit = BulkReview.Apply(workspace, batch.Id, ReviewStatus.Approved, "Synthetic", TestData.ReviewTime, "Test");
        Assert.Empty(audit.SelectedIds);
        Assert.Contains(audit.Exceptions, e => e.Reason.Contains("additional-rights", StringComparison.Ordinal));
        Assert.Contains(audit.Exceptions, e => e.Reason.Contains("unresolved-reference", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IdenticalAssertionsAcrossBatchesExportOnceWithEveryApprovedClaim()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace();
        var repeat = await new ImportEngine().ReadAsync(TestData.Fixture("hpoa.tsv"), TestData.Options("hpo-annotations") with { SourceUrl = "https://example.org/another-batch" });
        workspace.Batches.Add(repeat);
        Assert.Contains(WorkspaceValidation.Validate(workspace), d => d.Code == "duplicate-assertion-claim");
        Assert.DoesNotContain(WorkspaceValidation.Validate(workspace), d => d.Code == "duplicate-id");
        TestData.Approve(workspace); await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        var sidecar = await TestData.Read<SourceAssertionDocument>(data.PathFor("out/source-assertions.json"));
        var assertion = sidecar.Assertions.Single(a => a.Source.Kind == SourceAssertionKind.HpoAssociation);
        Assert.Equal(2, assertion.SourceClaims.Count);
        Assert.Equal(2, assertion.SourceClaims.Select(c => c.Provenance.Source).Distinct().Count());
        var package = await TestData.Read<ContentPackage>(data.PathFor("out/content.json"));
        Assert.Single(package.Relations);
        Assert.Equal(2, package.MedicalAnnotations.Single(a => a.Kind == MedicalAnnotationKind.DiseasePhenotype).Extensions!["sourceClaims"].GetArrayLength());
    }

    [Fact]
    public async Task ConflictingAssertionContextIsNeverCoalesced()
    {
        var first = await TestData.Import("hpo-annotations", "hpoa.tsv");
        var second = await new ImportEngine().ReadAsync(TestData.Fixture("hpoa.tsv"), TestData.Options("hpo-annotations") with { ImportedAt = TestData.ImportTime.AddHours(1) });
        second.Assertions[0] = second.Assertions[0] with { DiseaseExternalId = "SYNTHETIC:conflict" };
        var workspace = new ImportWorkspace { Batches = [first, second] };
        Assert.Contains(WorkspaceValidation.Validate(workspace), d => d.Code == "duplicate-id");
    }

    [Fact]
    public async Task UnapprovedDuplicateDoesNotLeakItsSourceOrRightsIntoRelease()
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace(); TestData.Approve(workspace);
        var repeat = await new ImportEngine().ReadAsync(TestData.Fixture("hpoa.tsv"), TestData.Options("hpo-annotations") with { SourceUrl = "https://example.org/UNAPPROVED-SECRET" });
        workspace.Batches.Add(repeat);
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        foreach (var path in Directory.GetFiles(data.PathFor("out"), "*", SearchOption.AllDirectories))
            Assert.DoesNotContain("UNAPPROVED-SECRET", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task LoincObservationIsNotAPlayableExaminationAndLegacyRecordsStillLoad()
    {
        using var data = new TestData(); var batch = await TestData.Import("loinc", "loinc.csv");
        Assert.All(batch.Terms, t => Assert.Equal(ImportedTermKind.Observation, t.Kind));
        var workspace = new ImportWorkspace { Batches = [batch] };
        // The existing synthetic LOINC fixture deliberately includes a per-record copyright notice.
        var eligible = batch.Terms.Single(t => !t.Obsolete);
        RecordRights.Decide(workspace, Rights(batch, eligible.Id, evidence: "Own synthetic fixture permission"));
        BulkReview.Apply(workspace, batch.Id, ReviewStatus.Approved, "Synthetic", TestData.ReviewTime, "Test");
        RecordRights.Decide(workspace, Rights(batch));
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        await using var file = File.OpenRead(data.PathFor("out/content.json"));
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(file);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        Assert.IsType<ObservationDefinition>(Assert.Single(result.Value!.Definitions));
        Assert.Empty(result.Value.Relations); Assert.Empty(result.Value.Gameplay);
        var old = batch.Terms[0] with { Kind = ImportedTermKind.Examination };
        Assert.Equal(ImportedTermKind.Examination, JsonSerializer.Deserialize<ImportedTerm>(TestData.Json(old), ContentJson.Options)!.Kind);
    }

    [Fact]
    public async Task ContextualSourceAnnotationsExportThroughTheCanonicalSdkContract()
    {
        using var data = new TestData(); var workspace = new ImportWorkspace { Batches = [await TestData.Import("hpo", "hpo.json"), await TestData.Import("hpo-annotations", "hpoa-negative.tsv")] };
        TestData.Approve(workspace); await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
        await using var file = File.OpenRead(data.PathFor("out/content.json")); var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(file);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        var annotation = Assert.Single(result.Value!.MedicalAnnotations);
        Assert.True(annotation.IsNegated); Assert.Contains("NOT", annotation.Qualifiers);
        Assert.Equal("NOT", annotation.SourceFields["qualifier"].GetString()); Assert.Empty(result.Value.Relations);
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("licenseText")]
    public async Task OriginalWorkspaceJsonIsValidatedBeforeDefaults(string missing)
    {
        using var data = new TestData(); var workspace = await TestData.CompleteWorkspace();
        var node = JsonSerializer.SerializeToNode(workspace, ContentJson.Options)!;
        if (missing == "schemaVersion") node.AsObject().Remove(missing);
        else node["batches"]![0]!["options"]!.AsObject().Remove(missing);
        await File.WriteAllTextAsync(data.PathFor("workspace.json"), node.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => new WorkspaceStore(data.PathFor("workspace.json")).ReadAsync());
    }

    [Fact]
    public async Task OldWorkspaceWithoutAdditiveFieldsRemainsValid()
    {
        var workspace = await TestData.CompleteWorkspace();
        var node = JsonSerializer.SerializeToNode(workspace, ContentJson.Options)!; node.AsObject().Remove("bulkReviews");
        await using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString()));
        Assert.Empty((await ImportDocuments.ReadAsync<ImportWorkspace>(input)).BulkReviews);
    }

    [Fact]
    public async Task FullDatasetHarnessPinsChecksumsCountsErrorsAndRepeatedBytes()
    {
        var input = TestData.Fixture("hpo.json");
        var test = new DatasetCase { Input = input, Sha256 = ImportIdentity.Hash(await File.ReadAllBytesAsync(input)), Options = TestData.Options("hpo"), ExpectedTerms = 2, ExpectedAssertions = 1, ExpectedErrors = new() };
        var report = await FullDatasetRun.ExecuteAsync(test);
        Assert.True(report.Reproducible); Assert.True(report.ExpectationsMatch); Assert.Empty(report.ErrorCounts);
        Assert.True(report.PeakProcessWorkingSetBytes > 0);
        Assert.False((await FullDatasetRun.ExecuteAsync(test with { ExpectedTerms = 999 })).ExpectationsMatch);
        await Assert.ThrowsAsync<InvalidDataException>(() => FullDatasetRun.ExecuteAsync(test with { Sha256 = new string('0', 64) }));
    }

    [Fact]
    public async Task CliBulkReviewDefaultsToReadOnlyAndCommitsOnlyWithApply()
    {
        using var data = new TestData(); var store = new WorkspaceStore(data.PathFor("workspace.json")); var batch = await TestData.Import("hpo", "hpo.obo"); await store.AddAsync(batch);
        string[] args = ["review-bulk", "--workspace", store.Path, "--batch", batch.Id, "--status", "approved", "--reviewer", "Synthetic", "--at", "2026-01-02T00:00:00Z", "--note", "Test only"];
        Assert.Equal(0, await global::Iryomera.MedicalImporter.Program.Main(args)); Assert.Empty((await store.ReadAsync()).Reviews);
        Assert.Equal(0, await global::Iryomera.MedicalImporter.Program.Main([.. args, "--apply"])); Assert.Single((await store.ReadAsync()).BulkReviews);
    }
    [Fact]
    public async Task ObsoletePrimaryIdentityIsNotSilentlyReplacedByAnActiveAlias()
    {
        var batch = await TestData.Import("hpo", "hpo.json");
        var obsolete = batch.Terms[0] with { Obsolete = true };
        batch.Terms[0] = obsolete;
        batch.Terms[1] = batch.Terms[1] with { AlternateIds = [obsolete.ExternalId] };
        var workspace = new ImportWorkspace { Batches = [batch] };
        Assert.Contains(WorkspaceValidation.Validate(workspace), d => d.Code == "shadowed-alternate-id" && d.Severity == DiagnosticSeverity.Warning);
        Assert.DoesNotContain(WorkspaceValidation.Validate(workspace), d => d.Code == "ambiguous-external-id");
        var audit = BulkReview.Apply(workspace, batch.Id, ReviewStatus.Approved, "Synthetic", TestData.ReviewTime, "Test");
        Assert.Contains(audit.Exceptions, e => e.RecordId == obsolete.Id && e.Reason == "obsolete-term");
        Assert.Contains(audit.Exceptions, e => e.Reason == "endpoint-not-selected-or-approved");
    }

    [Fact]
    public async Task AnAliasClaimingAnActivePrimaryIdStillFails()
    {
        var batch = await TestData.Import("hpo", "hpo.json");
        batch.Terms[1] = batch.Terms[1] with { AlternateIds = [batch.Terms[0].ExternalId] };
        Assert.Contains(WorkspaceValidation.Validate(new() { Batches = [batch] }), d => d.Code == "ambiguous-external-id");
    }

    [Fact]
    public async Task UnlabelledObsoleteNodesKeepOriginalDataWithoutInventingLabels()
    {
        using var data = new TestData();
        var node = JsonNode.Parse(await File.ReadAllTextAsync(TestData.Fixture("maxo.json")))!;
        var term = node["graphs"]![0]!["nodes"]![0]!; term.AsObject().Remove("lbl"); term["meta"] = new JsonObject { ["deprecated"] = true };
        await File.WriteAllTextAsync(data.PathFor("obsolete.json"), node.ToJsonString());
        var batch = await new ImportEngine().ReadAsync(data.PathFor("obsolete.json"), TestData.Options("maxo"));
        Assert.Empty(batch.Terms); Assert.Empty(batch.Assertions);
        Assert.Contains(batch.Diagnostics, d => d.Code == "unlabelled-obsolete-term");
        Assert.Contains(batch.SourceMetadata.Keys, k => k.StartsWith("unlabelledObsolete_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LegacyAdapterBatchIdentityRemainsValidAndNewImportsHaveANewVersion()
    {
        var batch = await TestData.Import("hpo", "hpo.json");
        Assert.Equal("1.1.0", batch.AdapterVersion);
        var legacyId = "batch_" + ImportIdentity.Hash(JsonSerializer.SerializeToUtf8Bytes(new { hash = batch.InputSha256, options = batch.Options, adapterVersion = "1.0.0" }, ContentJson.Options));
        var legacy = batch with { Id = legacyId, AdapterVersion = "1.0.0" };
        Assert.NotEqual(legacy.Id, batch.Id);
        Assert.DoesNotContain(WorkspaceValidation.Validate(new() { Batches = [legacy] }), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task AdditionalSourceTermsAndLoincThirdPartyNoticesNeedScopedPermission()
    {
        using var data = new TestData(); var batch = await TestData.Import("loinc", "loinc.csv");
        batch.Terms.RemoveAll(t => t.Obsolete);
        var term = batch.Terms[0]; term.SourceFields["EXTERNAL_COPYRIGHT_NOTICE"] = ImportIdentity.Json("Synthetic third-party terms");
        var workspace = new ImportWorkspace { Batches = [batch] }; TestData.Approve(workspace);
        Assert.NotNull(RecordRights.AdditionalTerms(batch, term.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReleaseExporter().ExportAsync(workspace, data.PathFor("out")));
        RecordRights.Decide(workspace, Rights(batch, term.Id, evidence: "Synthetic exact-record permission"));
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"));
    }

    [Fact]
    public async Task DuplicateAssertionsInsideOneBatchStillFail()
    {
        var batch = await TestData.Import("hpo-annotations", "hpoa.tsv");
        batch.Assertions.Add(batch.Assertions[0]);
        Assert.Contains(WorkspaceValidation.Validate(new() { Batches = [batch] }), d => d.Code == "duplicate-id");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task LegacyPlatformSpecificBatchAndAssertionHashesRemainReadable(string newLine)
    {
        var batch = await TestData.Import("hpo-annotations", "hpoa.tsv");
        var options = new JsonSerializerOptions(ContentJson.Options) { NewLine = newLine };
        var legacyId = "batch_" + ImportIdentity.Hash(JsonSerializer.SerializeToUtf8Bytes(new { hash = batch.InputSha256, options = batch.Options, adapterVersion = "1.0.0" }, options));
        var assertion = batch.Assertions[0];
        var legacyAssertion = batch.Options.Namespace + ":assertion_" + ImportIdentity.Hash(JsonSerializer.SerializeToUtf8Bytes(new
        { source = batch.Options.Source, kind = assertion.Kind, subject = assertion.SubjectExternalId, predicate = assertion.Predicate, target = assertion.ObjectExternalId, fields = assertion.SourceFields }, options));
        batch.Assertions[0] = assertion with { Id = legacyAssertion, Provenance = assertion.Provenance with { ExternalId = legacyAssertion } };
        var legacy = batch with { Id = legacyId, AdapterVersion = "1.0.0" };
        Assert.DoesNotContain(WorkspaceValidation.Validate(new() { Batches = [legacy] }), d => d.Code is "batch-integrity" or "assertion");
        var badModern = legacy with { AdapterVersion = "1.1.0" };
        Assert.Contains(WorkspaceValidation.Validate(new() { Batches = [badModern] }), d => d.Code == "batch-integrity");
    }

}
