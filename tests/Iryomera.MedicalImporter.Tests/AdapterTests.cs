using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.MedicalImporter.Tests;

public sealed class AdapterTests
{
    [Theory]
    [InlineData("hpo", "hpo.json", "json", 2)]
    [InlineData("hpo", "hpo.obo", "obo", 3)]
    [InlineData("maxo", "maxo.obo", "obo", 1)]
    [InlineData("maxo", "maxo.json", "json", 1)]
    [InlineData("hpo-annotations", "hpoa.tsv", "tsv", 1)]
    [InlineData("maxo-annotations", "maxo-annotations.tsv", "tsv", 1)]
    [InlineData("icd-10-gm", "icd.xml", "xml", 1)]
    [InlineData("ops", "ops.xml", "xml", 1)]
    [InlineData("loinc", "loinc.csv", "csv", 2)]
    public async Task EachAdapterReadsItsPublishedFormatAndRetainsLineage(string source, string file, string format, int count)
    {
        var batch = await TestData.Import(source, file);
        Assert.Equal(format, batch.DetectedFormat); Assert.Equal(count, batch.Terms.Count);
        Assert.Equal(64, batch.InputSha256.Length); Assert.True(batch.InputBytes > 0);
        Assert.All(batch.Terms, t =>
        {
            Assert.Equal(ReviewStatus.Unreviewed, t.Provenance.ReviewStatus);
            Assert.Equal("MIT", t.Provenance.License); Assert.Equal("synthetic-1", t.Provenance.SourceVersion);
            Assert.Equal(TestData.ImportTime, t.Provenance.ImportedAt); Assert.NotEmpty(t.SourceFields);
            Assert.True(ContentFormat.IsValidId(t.Id));
        });
    }

    [Fact]
    public async Task OntologyHierarchyNeverBecomesADiseaseAssociation()
    {
        var batch = await TestData.Import("hpo", "hpo.json");
        Assert.All(batch.Terms, t => Assert.Equal(ImportedTermKind.Finding, t.Kind));
        Assert.Single(batch.Assertions); Assert.Equal(SourceAssertionKind.OntologyHierarchy, batch.Assertions[0].Kind);
        Assert.Contains("HP:9999091", batch.Terms.Single(t => t.ExternalId == "HP:9999001").AlternateIds);
        Assert.Contains("Synthetic amber feature", batch.Terms.Single(t => t.ExternalId == "HP:9999001").Synonyms);
    }

    [Fact]
    public async Task MaxoPreservesTernaryContextAndDoesNotInventTreatments()
    {
        var ontology = await TestData.Import("maxo", "maxo.obo");
        Assert.Equal(ImportedTermKind.MedicalProcedure, Assert.Single(ontology.Terms).Kind);
        var batch = await TestData.Import("maxo-annotations", "maxo-annotations.tsv");
        var assertion = Assert.Single(batch.Assertions);
        Assert.Equal("TREATS", assertion.Predicate); Assert.Equal("SYNTHETIC:azure", assertion.DiseaseExternalId);
        Assert.Equal("SYNTHETIC:extension", assertion.ExtensionExternalId);
        Assert.Equal("PCS", Assert.Single(assertion.Provenance.Evidence).EvidenceCode);
        Assert.Equal(15, assertion.SourceFields.Count);
    }

    [Fact]
    public async Task LoincRetainsExactFieldNamesValuesAndMultilineCsv()
    {
        var batch = await TestData.Import("loinc", "loinc.csv");
        var term = batch.Terms.Single(t => t.ExternalId == "LOINC:X99991-1");
        Assert.Equal("Synthetic reading, amber", term.Label);
        Assert.Contains("\n", term.Description!); Assert.Contains("\"amber\"", term.Description!);
        Assert.Equal("X99991-1", term.SourceFields["LOINC_NUM"].GetString());
        Assert.True(batch.Terms.Single(t => t.ExternalId == "LOINC:X99992-2").Obsolete);
    }

    [Fact]
    public async Task ClamlRetainsClassificationRubricsWithoutInventingClinicalLinks()
    {
        var batch = await TestData.Import("icd-10-gm", "icd.xml");
        var term = Assert.Single(batch.Terms);
        Assert.Equal("de", term.Language); Assert.Equal("Synthetische Diagnose Ä", term.Label);
        Assert.Contains("inclusion", term.SourceFields["classXml"].GetString()!);
        Assert.Contains(batch.Diagnostics, d => d.Code == "classification-group"); Assert.Empty(batch.Assertions);
        Assert.Equal("synthetic-1", batch.ObservedSourceVersion);
    }

    [Theory]
    [InlineData("hpo", "{\"terms\":[]}")]
    [InlineData("hpo", "format-version: 9.0\n[Term]\nid: HP:9999001\nname: Synthetic\n")]
    [InlineData("loinc", "CODE,LABEL\nX1,Synthetic\n")]
    [InlineData("hpo-annotations", "database_id\tdisease_name\nSYNTHETIC:a\tSynthetic\n")]
    [InlineData("ops", "<notClaml/>")]
    public async Task MalformedAndUnsupportedFormatsFailClearly(string source, string contents)
    {
        using var data = new TestData();
        var path = data.PathFor(source == "ops" ? "bad.xml" : source == "loinc" ? "bad.csv" : source == "hpo-annotations" ? "bad.tsv" : contents.StartsWith('{') ? "bad.json" : "bad.obo");
        await File.WriteAllTextAsync(path, contents);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ImportEngine().ReadAsync(path, TestData.Options(source)));
    }

    [Fact]
    public async Task XmlNeverResolvesExternalEntities()
    {
        using var data = new TestData();
        await File.WriteAllTextAsync(data.PathFor("secret.txt"), "must-not-appear-in-import");
        var uri = new Uri(data.PathFor("secret.txt")).AbsoluteUri;
        var xml = $"<!DOCTYPE ClaML [<!ENTITY external SYSTEM '{uri}'>]><ClaML><Class kind='category' code='X99'><Rubric kind='preferred'><Label>&external;</Label></Rubric></Class></ClaML>";
        await File.WriteAllTextAsync(data.PathFor("unsafe.xml"), xml);
        await Assert.ThrowsAsync<System.Xml.XmlException>(() => new ImportEngine().ReadAsync(data.PathFor("unsafe.xml"), TestData.Options("ops")));
    }

    [Fact]
    public async Task DuplicateTermsAndAssertionsAreVisible()
    {
        using var data = new TestData();
        var text = await File.ReadAllTextAsync(TestData.Fixture("hpoa.tsv"));
        await File.WriteAllTextAsync(data.PathFor("duplicate.tsv"), text + text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1] + "\n");
        var batch = await new ImportEngine().ReadAsync(data.PathFor("duplicate.tsv"), TestData.Options("hpo-annotations"));
        Assert.Single(batch.Terms); Assert.Single(batch.Assertions);
        Assert.Contains(batch.Diagnostics, x => x.Code == "duplicate-assertion");
    }

    [Fact]
    public async Task InputLimitsAndCancellationAreEnforced()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => new ImportEngine().ReadAsync(TestData.Fixture("hpo.json"), TestData.Options("hpo") with { MaxInputBytes = 2 }));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ImportEngine().ReadAsync(TestData.Fixture("hpo.json"), TestData.Options("hpo"), cts.Token));
    }

    [Fact]
    public void IdentifierEncodingIsStableAndCollisionFreeForPunctuation()
    {
        Assert.NotEqual(ImportIdentity.ContentId("base", "A:B-C"), ImportIdentity.ContentId("base", "A:B_C"));
        Assert.Equal(ImportIdentity.ContentId("base", "HP:9999001"), ImportIdentity.ContentId("base", "HP:9999001"));
    }
}
