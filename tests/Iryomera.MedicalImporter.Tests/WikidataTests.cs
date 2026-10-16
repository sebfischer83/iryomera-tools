using System.Text.Json;
using System.Text.Json.Nodes;
using Iryomera.Content.SDK;
using Iryomera.MedicalImporter.Adapters;
using Xunit;

namespace Iryomera.MedicalImporter.Tests;

public sealed class WikidataTests
{
    private static ImportOptions Options => TestData.Options("wikidata") with { License = "CC0-1.0", LicenseText = "Own synthetic source-format fixture; CC0 metadata for the test only.", RetrievedAt = TestData.ImportTime.AddDays(-1) };
    private static Task<ImportBatch> Import(string format) => new ImportEngine().ReadAsync(TestData.Fixture("wikidata." + format), Options);
    [Theory]
    [InlineData("json")]
    [InlineData("csv")]
    public async Task SavedResultsRetainStableQIdsLanguagesAliasesAndMedicalStatements(string format)
    {
        var batch = await Import(format);
        Assert.Equal("1.2.0", batch.AdapterVersion); Assert.Equal(4, batch.Terms.Count); Assert.Equal(4, batch.Assertions.Count);
        var disease = batch.Terms.Single(t => t.Kind == ImportedTermKind.Disease);
        Assert.Equal("WIKIDATA:Q999990001", disease.ExternalId);
        Assert.Equal(ImportIdentity.ContentId(Options.Namespace, disease.ExternalId), disease.Id);
        Assert.Equal(2, disease.Translations!.Count); Assert.Equal(2, disease.Translations["en"].Aliases.Count); Assert.Equal(2, disease.Translations["de"].Aliases.Count);
        Assert.Contains("Bernstein", disease.Translations["de"].Label); Assert.NotNull(disease.Translations["en"].Description);
        Assert.All(batch.Terms, t => Assert.Equal(ReviewStatus.Unreviewed, t.Provenance.ReviewStatus));
        Assert.All(batch.Assertions, a => { Assert.Equal(SourceAssertionKind.WikidataStatement, a.Kind); Assert.Equal(ReviewStatus.Unreviewed, a.Provenance.ReviewStatus); Assert.Equal("CC0-1.0", a.Provenance.License); Assert.True(a.Provenance.Extensions!.ContainsKey("retrievedAt")); });
        var symptom = batch.Assertions.Single(a => a.Predicate == "P780");
        Assert.Equal(2, symptom.SourceFields["references"].GetArrayLength()); Assert.True(symptom.SourceFields["qualifiers"].GetArrayLength() > 0);
        Assert.Equal("normal", symptom.SourceFields["rank"].GetString()); Assert.Contains("/statement/", symptom.Provenance.ExternalId);
        Assert.Contains(WorkspaceValidation.Validate(new() { Batches = [batch] }), d => d.Code == "unresolved-reference");
        Assert.DoesNotContain(batch.Terms, t => t.ExternalId == "WIKIDATA:Q999990005"); // No guessed type for a cause entity.
    }
    [Fact]
    public async Task RepeatedOfflineImportsAreByteIdenticalAndOldAdaptersKeepTheirVersions()
    {
        Assert.Equal(TestData.Json(await Import("json")), TestData.Json(await Import("json")));
        Assert.Equal("1.1.0", (await TestData.Import("hpo", "hpo.json")).AdapterVersion);
        Assert.Contains(ImportEngine.Sources, source => source.SourceId == "wikidata");
    }
    [Fact]
    public async Task IdentifierCandidatesPreserveAuthorityAndNeverCreateAliasesOrMergeByLabels()
    {
        var wiki = await Import("json"); var hpo = await TestData.Import("hpo", "hpo.json"); var maxo = await TestData.Import("maxo", "maxo.obo"); var icd = await TestData.Import("icd-10-gm", "icd.xml");
        var workspace = new ImportWorkspace { Batches = [wiki, hpo, maxo, icd] };
        var before = TestData.Json(workspace); var candidates = IdentifierMatching.Candidates(workspace);
        Assert.Contains(candidates, c => c.System == "HP" && c.Kind == "explicitIdentifier"); Assert.Contains(candidates, c => c.System == "MAXO"); Assert.Contains(candidates, c => c.System == "ICD10" && c.Kind == "classificationFamily");
        Assert.Equal(before, TestData.Json(workspace)); Assert.All(wiki.Terms, t => Assert.Empty(t.AlternateIds));
    }
    [Fact]
    public async Task ReviewedWikidataExportsBothRealLanguagesAndAliasesWithoutGameplayOrUnconditionalRelations()
    {
        using var data = new TestData(); var batch = await Import("json"); var workspace = new ImportWorkspace { Batches = [batch] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReleaseExporter().ExportAsync(workspace, data.PathFor("not-approved")));
        BulkReview.Apply(workspace, batch.Id, ReviewStatus.Approved, "Synthetic reviewer", TestData.ReviewTime, "Own fixture only");
        RecordRights.Decide(workspace, new() { BatchId = batch.Id, Decision = RedistributionDecision.Permitted, Reviewer = "Synthetic rights", At = TestData.ReviewTime, Note = "Own synthetic input only" });
        await new ReleaseExporter().ExportAsync(workspace, data.PathFor("out"), ["en", "de"]);
        var package = await TestData.Read<ContentPackage>(data.PathFor("out/content.json")); Assert.Empty(package.Relations); Assert.Empty(package.Gameplay); Assert.Null(package.DiagnosticSimulation);
        Assert.Equal(3, (await TestData.Read<SourceAssertionDocument>(data.PathFor("out/source-assertions.json"))).Assertions.Count);
        var en = await TestData.Read<LocalizationDefinition>(data.PathFor("out/localization/en.json")); var de = await TestData.Read<LocalizationDefinition>(data.PathFor("out/localization/de.json"));
        var disease = package.Definitions.OfType<DiseaseDefinition>().Single(); Assert.Contains("Fictional", en.Entries[disease.NameKey]); Assert.Contains("Fiktiver", de.Entries[disease.NameKey]);
        Assert.DoesNotContain("statementId", TestData.Json(disease));
        Assert.DoesNotContain("referenceValue", TestData.Json(disease));
        Assert.Equal(2, disease.Extensions!["localizedAliasKeys"].GetProperty("de").GetArrayLength()); Assert.Empty(disease.SynonymKeys);
        Assert.Contains(disease.Classifications, c => c.System == "HP");
        await using var wire = File.OpenRead(data.PathFor("out/content.json")); Assert.True((await ContentDocuments.ReadValidatedAsync<ContentPackage>(wire)).IsValid);
    }
    private static async Task<ImportBatch> Change(TestData data, Action<JsonObject> mutation)
    {
        var doc = JsonNode.Parse(await File.ReadAllTextAsync(TestData.Fixture("wikidata.json")))!.AsObject(); mutation(doc);
        await File.WriteAllTextAsync(data.PathFor("changed.json"), doc.ToJsonString()); return await new ImportEngine().ReadAsync(data.PathFor("changed.json"), Options);
    }
    [Theory]
    [InlineData("deprecated", "wikidata-deprecated")]
    [InlineData("unknown", "wikidata-incomplete-statement")]
    [InlineData("unreferenced", "wikidata-unreferenced-statement")]
    public async Task MissingMetadataAndDeprecatedStatementsAreExcludedFromBulkApproval(string change, string concern)
    {
        using var data = new TestData();
        var batch = await Change(data, doc =>
        {
            foreach (var node in doc["results"]!["bindings"]!.AsArray())
            {
                if (change == "unreferenced") { node!.AsObject().Remove("reference"); node.AsObject().Remove("referenceValue"); }
                else if (change == "unknown") node!.AsObject().Remove("rank");
                else node!["rank"]!["value"] = "http://wikiba.se/ontology#DeprecatedRank";
            }
        });
        Assert.Contains(batch.Diagnostics, d => d.Code == concern);
        var workspace = new ImportWorkspace { Batches = [batch] };
        var audit = BulkReview.Apply(workspace, batch.Id, ReviewStatus.Approved, "Synthetic", TestData.ReviewTime, "Test only");
        Assert.All(batch.Assertions, a => Assert.Contains(audit.Exceptions, e => e.RecordId == a.Id && e.Reason == concern));
        if (change == "deprecated") Assert.Throws<InvalidOperationException>(() => WorkspaceValidation.Review(workspace, batch.Id, batch.Assertions[0].Id, ReviewStatus.Approved, "Synthetic", TestData.ReviewTime, "Test"));
    }
    [Fact]
    public async Task BindingLanguageDatatypeAndQualifierIdentityRemainInOriginalRows()
    {
        var a = (await Import("json")).Assertions.First(); var row = a.SourceFields["rows"][0];
        Assert.Equal("de", row.GetProperty("itemLabelDe").GetProperty("xml:lang").GetString()); Assert.Equal("uri", row.GetProperty("qualifierValue").GetProperty("type").GetString());
    }
    [Theory]
    [InlineData("item", "not-a-Q-id")]
    [InlineData("property", "P999999")]
    [InlineData("rank", "invented-rank")]
    [InlineData("property", "https://unrelated.example/entity/P780")]
    public async Task MalformedIdentifiersUnsupportedPropertiesAndRanksFailClearly(string column, string value)
    {
        using var data = new TestData(); await Assert.ThrowsAsync<InvalidDataException>(() => Change(data, doc => { doc["results"]!["bindings"]![0]![column]!["value"] = value; }));
    }
    [Fact]
    public async Task MissingLabelsAreNotInventedAndConflictingLanguageLabelsFail()
    {
        using var data = new TestData();
        await Assert.ThrowsAsync<InvalidDataException>(() => Change(data, doc => { doc["results"]!["bindings"]![0]!["itemLabelEn"]!["value"] = "Conflicting synthetic label"; }));
        var batch = await Change(data, doc => { foreach (var row in doc["results"]!["bindings"]!.AsArray()) { row!.AsObject().Remove("itemLabelEn"); row.AsObject().Remove("itemLabelDe"); } });
        Assert.DoesNotContain(batch.Terms, t => t.Kind == ImportedTermKind.Disease); Assert.Contains(batch.Diagnostics, d => d.Code == "wikidata-label-missing");
    }
    [Fact]
    public async Task ActualRetrievalTimeAndCc0AreMandatoryAndNoNetworkIsUsed()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new ImportEngine().ReadAsync(TestData.Fixture("wikidata.json"), Options with { RetrievedAt = null }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ImportEngine().ReadAsync(TestData.Fixture("wikidata.csv"), Options with { License = "MIT" }));
    }
    [Fact]
    public async Task WikidataWorkspacePassesTheBundledSchemaBeforeDefaults()
    {
        var workspace = new ImportWorkspace { Batches = [await Import("json")] };
        await using var input = new MemoryStream(); await ContentJson.WriteAsync(input, workspace); input.Position = 0;
        Assert.Equal("wikidata", (await ImportDocuments.ReadAsync<ImportWorkspace>(input)).Batches[0].Options.Source);
    }
    [Fact]
    public async Task UnboundMedicalValueAndDiseaseOnlyRowsStayHonest()
    {
        using var data = new TestData();
        var batch = await Change(data, doc => { foreach (var row in doc["results"]!["bindings"]!.AsArray()) row!.AsObject().Remove("value"); });
        Assert.Contains(batch.Diagnostics, d => d.Code == "wikidata-incomplete-statement");
        Assert.All(batch.Assertions, a => Assert.StartsWith("WIKIDATA_VALUE:", a.ObjectExternalId));
        var rootOnly = await Change(data, doc => { foreach (var row in doc["results"]!["bindings"]!.AsArray()) row!.AsObject().Remove("property"); });
        Assert.Single(rootOnly.Terms); Assert.Empty(rootOnly.Assertions);
    }

    [Fact]
    public async Task ReferenceIdentityAloneDoesNotCountAsSupportingEvidence()
    {
        using var data = new TestData();
        var batch = await Change(data, doc => { foreach (var row in doc["results"]!["bindings"]!.AsArray()) row!.AsObject().Remove("referenceValue"); });
        Assert.All(batch.Assertions, a => Assert.Equal("wikidata-unreferenced-statement", WikidataAdapter.ReviewConcern(a)));
    }

    [Fact]
    public async Task AStatementCannotSilentlySplitIntoConflictingMedicalClaims()
    {
        using var data = new TestData();
        await Assert.ThrowsAsync<InvalidDataException>(() => Change(data, doc =>
        {
            var rows = doc["results"]!["bindings"]!.AsArray();
            var contradictory = rows[0]!.DeepClone(); contradictory["value"]!["value"] = "http://www.wikidata.org/entity/Q999990099";
            rows.Add(contradictory);
        }));
    }

}
