using System.Text;
using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.NameImporter.Tests;

/// <summary>All names and statistics below are invented test values; no real dataset is included.</summary>
public sealed class NameImporterTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "iryomera-names-tests-" + Guid.NewGuid().ToString("N"));
    public NameImporterTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private string PathFor(string name) => Path.Combine(_root, name);

    private string File(string name, string content) { var path = PathFor(name); System.IO.File.WriteAllText(path, content, new UTF8Encoding(false)); return path; }
    private static NameImportOptions Options(string source, int? year = null, string version = "synthetic-1") => new()
    {
        Source = source, SourceVersion = version, SourceUrl = "https://example.org/iryomera/synthetic-names/" + source, License = "CC0-1.0",
        LicenseText = "Synthetic test license text.", Attribution = "Iryomera synthetic name fixture", ImportedAt = At, BirthYear = year
    };
    private Task<NameBatch> Ssa(string content, int year = 2020, string file = "yob.txt") => new NameImportEngine().ReadAsync(File(file, content), Options("us-ssa", year));
    private const string Forenames = "Country,Country Group,Region,Population,Note,Year,Romanization,Index,Name Group,Gender,Localized Name,Romanized Name\n";
    private const string Surnames = "Country,Rank,Index,Name Group,Localized Name,Romanized Name,Count,Percent\n";

    [Fact]
    public async Task SsaImportIsDeterministicAndKeepsOriginalFields()
    {
        const string input = "Talvina,F,120\nOrwen,M,95\nKaelo,F,7\nKaelo,M,6\n";
        var first = await Ssa(input); var second = await Ssa(input, file: "copy.txt");
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(JsonSerializer.Serialize(first, ContentJson.Options), JsonSerializer.Serialize(second, ContentJson.Options));
        Assert.Equal(4, first.Records.Count); Assert.Empty(first.Diagnostics);
        var kaelo = first.Records.Where(r => r.Localized == "Kaelo").OrderBy(r => r.Gender).ToArray();
        Assert.Equal([NameGender.Female, NameGender.Male], kaelo.Select(r => r.Gender!.Value));
        Assert.All(first.Records, r => Assert.Equal(("US", NamePart.Given, 2020, 2020), (r.Country, r.Part, r.BirthYearFrom!.Value, r.BirthYearTo!.Value)));
        Assert.Equal("120", first.Records.Single(r => r.Localized == "Talvina").SourceFields["count"].GetString());
        Assert.NotEqual(first.Id, (await Ssa(input, 2021)).Id);
    }

    [Fact]
    public async Task SsaRequiresABirthYearAndReportsBadRows()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new NameImportEngine().ReadAsync(File("x.txt", "Talvina,F,5\n"), Options("us-ssa")));
        var batch = await Ssa("Bad,X,5\nGood,F,abc\nShort,F\nOkay,F,5\nTal‮vine,F,5\n");
        Assert.Equal(["Okay"], batch.Records.Select(r => r.Localized));
        Assert.Equal(["count", "gender", "name-text", "row-shape"], batch.Diagnostics.Where(d => d.Severity == NameDiagnosticSeverity.Error).Select(d => d.Code).Order());
    }

    [Fact]
    public async Task NormalizationIsReportedAndOriginalBytesStayInSourceFields()
    {
        var batch = await Ssa("José,M,5\n Spaced,F,6\n");
        Assert.Equal(["José", "Spaced"], batch.Records.Select(r => r.Localized).Order(StringComparer.Ordinal));
        Assert.Equal("José", batch.Records.Single(r => r.Localized == "José").SourceFields["name"].GetString());
        Assert.Contains(batch.Diagnostics, d => d.Code == "name-normalized"); Assert.Contains(batch.Diagnostics, d => d.Code == "name-trimmed");
    }

    [Fact]
    public async Task InvalidUtf8IsRejectedWithoutABatch()
    {
        var path = PathFor("invalid.txt"); await System.IO.File.WriteAllBytesAsync(path, [0x41, 0xFF, 0x2C, 0x46, 0x2C, 0x35, 0x0A]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new NameImportEngine().ReadAsync(path, Options("us-ssa", 2020)));
    }

    [Fact]
    public async Task SigpwnedForenamesAndSurnamesKeepScriptsRanksAndCohorts()
    {
        var given = await new NameImportEngine().ReadAsync(File("given.csv", "﻿" + Forenames +
            "JP,1,,,,2022,Y,1,N-JP-1-F-1,F,澄音,Sumine\nDE,1,Nord,,,2022,N,2,N-DE-1-M-2,M,Orwen,Orwen\nES,1,,,,2021,N,3,N-ES-1-U-3,U,Ariel,Ариэль\n"), Options("names-sigpwned"));
        var sumine = given.Records.Single(r => r.Country == "JP");
        Assert.Equal(("澄音", "Sumine", NameGender.Female, 1, 2022), (sumine.Localized, sumine.Romanized!, sumine.Gender!.Value, sumine.Rank!.Value, sumine.BirthYearFrom!.Value));
        Assert.NotNull(sumine.RomanizationOrigin);
        Assert.Null(given.Records.Single(r => r.Country == "DE").Romanized);
        var ariel = given.Records.Single(r => r.Country == "ES");
        Assert.Equal((NameGender.Unknown, (string?)null), (ariel.Gender!.Value, ariel.Romanized));
        Assert.Equal(["gender-unknown", "region-not-mapped", "romanized-dropped"], given.Diagnostics.Select(d => d.Code).Order());
        var family = await new NameImportEngine().ReadAsync(File("family.csv", Surnames + "JP,1,1,JP-1,霞森,Kasumori,1000,0.0100\nES,2,2,ES-2,Brenal,Brenal,500,3.5\n"), Options("names-sigpwned"));
        var kasumori = family.Records.Single(r => r.Country == "JP");
        Assert.Equal((NamePart.Family, (NameGender?)null, 1, 1000L, 0.0100m), (kasumori.Part, kasumori.Gender, kasumori.Rank!.Value, kasumori.Count!.Value, kasumori.Proportion!.Value));
        Assert.Contains(family.Diagnostics, d => d.Code == "proportion");
        await Assert.ThrowsAsync<InvalidDataException>(() => new NameImportEngine().ReadAsync(File("other.csv", "a,b\n1,2\n"), Options("names-sigpwned")));
    }

    [Fact]
    public async Task WorkspaceIsStrictAndRightsNeedEvidence()
    {
        var store = new NameWorkspaceStore(PathFor("ws.json"));
        var batch = await Ssa("Talvina,F,5\n"); await store.AddAsync(batch); await store.AddAsync(batch);
        Assert.Single((await store.ReadAsync()).Batches);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync(w => w.Rights.Add(new() { BatchId = batch.Id, Decision = RedistributionDecision.Permitted, Reviewer = "Test", At = At, Note = "No evidence" })));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync(w => w.Reviews.Add(new() { BatchId = batch.Id, RecordId = "names:name_missing", Status = ReviewStatus.Approved, Reviewer = "Test", At = At, Note = "Unknown" })));
        var json = System.IO.File.ReadAllText(store.Path).Replace("\"reviews\"", "\"unexpected\": 1, \"reviews\"");
        System.IO.File.WriteAllText(store.Path, json);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync());
    }

    [Fact]
    public async Task DateTimeFormatToleratesRegexTimeoutsButStaysStrict()
    {
        var store = new NameWorkspaceStore(PathFor("dates.json"));
        await store.AddAsync(await Ssa("Talvina,F,5\n"));
        Assert.IsType<TimeoutTolerantFormat>(Json.Schema.Formats.Get("date-time"));
        foreach (var value in new[] { "2026-10-18T19:00:00+00:00", "2026-10-18T19:00:00Z", "2026-10-18T19:00Z", "not-a-date" })
            Assert.Equal(Json.Schema.Formats.DateTime.Validate(System.Text.Json.Nodes.JsonValue.Create(value), out _),
                Json.Schema.Formats.Get("date-time").Validate(System.Text.Json.Nodes.JsonValue.Create(value), out _));
        var json = System.IO.File.ReadAllText(store.Path);
        System.IO.File.WriteAllText(store.Path, json.Replace("\"importedAt\": \"2026-10-17T12:00:00+00:00\"", "\"importedAt\": \"2026-10-17\"", StringComparison.Ordinal));
        Assert.NotEqual(json, System.IO.File.ReadAllText(store.Path));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync());

        var flaky = new FlakyFormat(failures: 2);
        Assert.True(new TimeoutTolerantFormat(flaky).Validate(System.Text.Json.Nodes.JsonValue.Create("x"), out _));
        Assert.Equal(3, flaky.Calls);
        var broken = new FlakyFormat(failures: int.MaxValue);
        Assert.Throws<System.Text.RegularExpressions.RegexMatchTimeoutException>(() => new TimeoutTolerantFormat(broken).Validate(System.Text.Json.Nodes.JsonValue.Create("x"), out _));
        Assert.Equal(5, broken.Calls);
    }

    private sealed class FlakyFormat(int failures) : Json.Schema.Format("test-flaky")
    {
        public int Calls { get; private set; }
        public override bool Validate(System.Text.Json.Nodes.JsonNode? node, out string? errorMessage)
        {
            errorMessage = null;
            if (++Calls <= failures) throw new System.Text.RegularExpressions.RegexMatchTimeoutException("x", "y", TimeSpan.FromMilliseconds(250));
            return true;
        }
    }

    private async Task<NameWorkspace> Approved(params NameBatch[] batches)
    {
        var store = new NameWorkspaceStore(PathFor("approved-" + Guid.NewGuid().ToString("N") + ".json"));
        foreach (var batch in batches) await store.AddAsync(batch);
        await store.UpdateAsync(w =>
        {
            foreach (var batch in batches)
            {
                w.Reviews.Add(new() { BatchId = batch.Id, Status = ReviewStatus.Approved, Reviewer = "Synthetic reviewer", At = At, Note = "Synthetic test approval" });
                w.Rights.Add(new() { BatchId = batch.Id, Decision = RedistributionDecision.Permitted, PermissionEvidence = "Synthetic CC0 test terms", Reviewer = "Synthetic reviewer", At = At, Note = "Synthetic test rights" });
            }
        });
        return await store.ReadAsync();
    }

    [Fact]
    public async Task ExportMergesBatchesIntoAValidSdkPackageWithPerSourceStatistics()
    {
        var recent = await Ssa("Talvina,F,120\nOrwen,M,95\n", 2020); var older = await Ssa("Talvina,F,40\nBrannoc,M,30\n", 1950, "old.txt");
        var workspace = await Approved(recent, older);
        var output = PathFor("release");
        var report = await new NameExporter().ExportAsync(workspace, output, "1.0.0");
        await using (var content = System.IO.File.OpenRead(Path.Combine(output, "content.json")))
        {
            var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(content);
            Assert.True(result.IsValid, string.Join(';', result.Issues));
            var pool = Assert.Single(result.Value!.NamePools!);
            Assert.Equal(("names:us.given", "US", NamePart.Given, "names.pool.us_given.name"), (pool.Id, pool.Country, pool.Part, pool.NameKey));
            var talvina = pool.Entries.Single(e => e.Localized == "Talvina");
            Assert.Equal(NameIdentity.EntryKey("Talvina", NameGender.Female), talvina.Key);
            Assert.Equal([1950, 2020], talvina.Attestations.Select(a => a.BirthYearFrom!.Value).Order());
            Assert.Equal([40L, 120L], talvina.Attestations.Select(a => a.Count!.Value).Order());
            Assert.Equal(2, pool.Sources.Count); Assert.All(pool.Sources, s => Assert.Equal(ReviewStatus.Approved, s.Provenance.ReviewStatus));
            Assert.Equal(pool.Entries.Select(e => e.Key).Order(StringComparer.Ordinal), pool.Entries.Select(e => e.Key));
        }
        foreach (var language in new[] { "en", "de" })
            Assert.Contains("names.pool.us_given.name", System.IO.File.ReadAllText(Path.Combine(output, "localization", language + ".json")));
        Assert.Contains("Synthetic test license text.", System.IO.File.ReadAllText(Path.Combine(output, "NOTICE.md")));
        await using (var stored = System.IO.File.OpenRead(Path.Combine(output, "release-report.json")))
            Assert.Equal(report.Pools[0].Entries, (await NameDocuments.ReadAsync<NameReleaseReport>(stored)).Pools[0].Entries);
        Assert.Equal(3, report.Pools[0].Entries);
        await Assert.ThrowsAsync<IOException>(() => new NameExporter().ExportAsync(workspace, output, "1.0.0"));
    }

    [Fact]
    public async Task CapKeepsTheMostFrequentEntriesWithinOneDatasetAndIsDeterministic()
    {
        // Same SSA release, two birth years: counts are summed within the dataset (Talvina 150, Orwen 95, Brannoc 30, Kaelo 7).
        var recent = await Ssa("Talvina,F,120\nOrwen,M,95\nKaelo,F,7\n", 2020); var older = await Ssa("Talvina,F,30\nBrannoc,M,30\n", 1950, "old.txt");
        var workspace = await Approved(recent, older);
        var report = await new NameExporter().ExportAsync(workspace, PathFor("capped"), "1.0.0", maxEntriesPerPool: 2);
        await using (var content = System.IO.File.OpenRead(Path.Combine(PathFor("capped"), "content.json")))
        {
            var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(content);
            Assert.True(result.IsValid, string.Join(';', result.Issues));
            var pool = Assert.Single(result.Value!.NamePools!);
            Assert.Equal(["Orwen", "Talvina"], pool.Entries.Select(e => e.Localized).Order(StringComparer.Ordinal));
            Assert.Equal([1950, 2020], pool.Entries.Single(e => e.Localized == "Talvina").Attestations.Select(a => a.BirthYearFrom!.Value).Order());
        }
        Assert.Equal(2, report.Pools[0].Entries);
        Assert.Equal(3, report.Batches.Sum(b => b.ExportedRecords));
        Assert.Contains("at most 2 entries per pool", System.IO.File.ReadAllText(Path.Combine(PathFor("capped"), "NOTICE.md")));
        Assert.Equal(4, (await new NameExporter().ExportAsync(workspace, PathFor("uncapped"), "1.0.0", maxEntriesPerPool: 10)).Pools[0].Entries);
        await Assert.ThrowsAsync<ArgumentException>(() => new NameExporter().ExportAsync(workspace, PathFor("zero"), "1.0.0", maxEntriesPerPool: 0));

        // Without counts the order is the SHA-256 of the entry key: stable across runs and input order.
        NameEntry Entry(string name) => new() { Key = NameIdentity.EntryKey(name, NameGender.Unknown), Localized = name, Gender = NameGender.Unknown, Attestations = [new() { SourceKey = "b_000000000000" }] };
        var names = new[] { "Aldo", "Bryn", "Cato", "Dara", "Emer", "Fenn" };
        var batch = recent with { Id = "batch_" + new string('0', 64) };
        var first = NameExporter.Cap(names.Select(Entry).ToList(), 3, [batch]).Select(e => e.Localized).ToArray();
        Assert.Equal(first, NameExporter.Cap(names.Reverse().Select(Entry).ToList(), 3, [batch]).Select(e => e.Localized));
        Assert.Equal(3, first.Length);
    }

    [Fact]
    public async Task OnlyApprovedRecordsOfPermittedBatchesAreExported()
    {
        var batch = await Ssa("Talvina,F,120\nOrwen,M,95\n"); var blocked = await Ssa("Brannoc,M,30\n", 1950, "blocked.txt");
        var store = new NameWorkspaceStore(PathFor("gate.json"));
        await store.AddAsync(batch); await store.AddAsync(blocked);
        var orwen = batch.Records.Single(r => r.Localized == "Orwen").Id;
        await store.UpdateAsync(w =>
        {
            foreach (var b in new[] { batch, blocked }) w.Reviews.Add(new() { BatchId = b.Id, Status = ReviewStatus.Approved, Reviewer = "Test", At = At, Note = "Approve batch" });
            w.Reviews.Add(new() { BatchId = batch.Id, RecordId = orwen, Status = ReviewStatus.Rejected, Reviewer = "Test", At = At, Note = "Reject one record" });
            w.Rights.Add(new() { BatchId = batch.Id, Decision = RedistributionDecision.Permitted, PermissionEvidence = "Synthetic terms", Reviewer = "Test", At = At, Note = "Permit" });
            w.Rights.Add(new() { BatchId = blocked.Id, Decision = RedistributionDecision.Blocked, Reviewer = "Test", At = At, Note = "Block" });
        });
        var workspace = await store.ReadAsync();
        Assert.Equal(ReviewStatus.Rejected, NameWorkspaceValidation.Status(workspace, batch.Id, orwen));
        var output = PathFor("gated");
        var report = await new NameExporter().ExportAsync(workspace, output, "1.0.0");
        var exported = string.Concat(Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(System.IO.File.ReadAllText));
        Assert.Contains("Talvina", exported); Assert.DoesNotContain("Orwen", exported); Assert.DoesNotContain("Brannoc", exported);
        Assert.Equal(1, Assert.Single(report.Batches).ExportedRecords);
        await store.UpdateAsync(w => w.Reviews.Add(new() { BatchId = batch.Id, Status = ReviewStatus.Rejected, Reviewer = "Test", At = At, Note = "Reject batch" }));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new NameExporter().ExportAsync(await store.ReadAsync(), PathFor("empty"), "1.0.0"));
    }

    [Fact]
    public async Task DifferingRomanizationsBlockTheExport()
    {
        // No safe adapter produces romanizations yet; build two staged batches with differing values directly.
        NameBatch Romanized(NameBatch batch, string romanized) => batch with { Records = [batch.Records[0] with { Localized = "霞森", Romanized = romanized, RomanizationOrigin = "Synthetic" }] };
        var a = Romanized(await Ssa("Kasumori,F,5\n", 2020), "Kasumori"); var b = Romanized(await Ssa("Kasumori,F,6\n", 1950, "b.txt"), "Kasumory");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await new NameExporter().ExportAsync(await Approved(a, b), PathFor("conflict"), "1.0.0"));
        Assert.Contains("romanization", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(PathFor("conflict")));
    }

    [Fact]
    public async Task UnsafeSourcesStayLocalAndCanNeverBePermitted()
    {
        var batch = await new NameImportEngine().ReadAsync(File("s.csv", Surnames + "JP,1,1,JP-1,霞森,Kasumori,1000,0.01\n"), Options("names-sigpwned"));
        Assert.False(NameWorkspaceValidation.Redistributable(batch));
        await Assert.ThrowsAsync<InvalidDataException>(() => Approved(batch));
        Assert.All(NameImportEngine.Sources.Where(a => a.SourceId != "names-sigpwned"), a => Assert.True(a.Redistributable));
    }

    [Fact]
    public async Task CensusSurnamesDropEthnicityColumnsAndCapitalizeNames()
    {
        var batch = await new NameImportEngine().ReadAsync(File("census.csv",
            "name,rank,count,prop100k,cum_prop100k,pctwhite,pctblack,pctapi,pctaian,pct2prace,pcthispanic\n" +
            "BRENNWALD,1,2000,828.19,828.19,70.9,23.11,0.5,0.89,2.19,2.4\nO'QUELLAN,2,150,50.5,878.69,(S),(S),1,1,1,1\nALL OTHER NAMES,0,10,1,1,1,1,1,1,1,1\n"), Options("us-census-surnames"));
        Assert.Equal(["Brennwald", "O'Quellan"], batch.Records.Select(r => r.Localized).Order(StringComparer.Ordinal));
        var first = batch.Records.Single(r => r.Localized == "Brennwald");
        Assert.Equal(("US", NamePart.Family, 1, 2000L, 0.0082819m), (first.Country, first.Part, first.Rank!.Value, first.Count!.Value, first.Proportion!.Value));
        Assert.Equal(["count", "name", "prop100k", "rank"], first.SourceFields.Keys);
        Assert.Equal("BRENNWALD", first.SourceFields["name"].GetString());
        Assert.Contains(batch.Diagnostics, d => d.Code == "case-normalized"); Assert.Contains(batch.Diagnostics, d => d.Code == "aggregate-row");
        Assert.DoesNotContain("pctwhite", JsonSerializer.Serialize(batch, ContentJson.Options));
    }

    [Fact]
    public async Task StatistikAustriaRowsAreSummedPerYearSexAndName()
    {
        var batch = await new NameImportEngine().ReadAsync(File("at.csv",
            "C-JAHR-0;C-WOHNBEZIRK-0;C-GESCHLECHT-0;F-VORNAME_NORMALISIERT;F-ANZAHL_LGEB\r\n1984;101;1;Orwen;4\r\n1984;102;1;Orwen;3\r\n1984;101;1;Orwen;9\r\n" +
            "1985;101;1;Orwen;2\r\n1984;0;2;Talvina;2\r\n1984;101;3;Kaelo;1\r\n"), Options("at-statistik"));
        var orwen = batch.Records.Single(r => r.Localized == "Orwen" && r.BirthYearFrom == 1984);
        Assert.Equal(("AT", NameGender.Male, 7L), (orwen.Country, orwen.Gender!.Value, orwen.Count!.Value));
        Assert.Equal(2, orwen.SourceFields["districtRows"].GetArrayLength());
        Assert.Equal(3, batch.Records.Count);
        Assert.Equal(["duplicate-row", "gender"], batch.Diagnostics.Select(d => d.Code).Order());
        Assert.Equal(NameGender.Female, batch.Records.Single(r => r.Localized == "Talvina").Gender);
    }

    private static string Binding(string item, string type, string rank, string name, string lang, string? romanized = null, bool withRank = true) =>
        "{\"item\":{\"type\":\"uri\",\"value\":\"http://www.wikidata.org/entity/" + item + "\"},\"type\":{\"type\":\"uri\",\"value\":\"http://www.wikidata.org/entity/" + type + "\"}," +
        "\"statement\":{\"type\":\"uri\",\"value\":\"http://www.wikidata.org/entity/statement/" + item + "-1\"}," +
        (withRank ? "\"rank\":{\"type\":\"uri\",\"value\":\"http://wikiba.se/ontology#" + rank + "\"}," : "") +
        "\"name\":{\"type\":\"literal\",\"xml:lang\":\"" + lang + "\",\"value\":\"" + name + "\"}" +
        (romanized is null ? "" : ",\"romanized\":{\"type\":\"literal\",\"xml:lang\":\"en\",\"value\":\"" + romanized + "\"}") + "}";

    [Fact]
    public async Task WikidataNamesUseOnlyTheOperatorLanguageMapping()
    {
        // Invented Q IDs and names; no real Wikidata content.
        var json = "{\"head\":{\"vars\":[\"item\",\"type\",\"statement\",\"rank\",\"name\",\"romanized\"]},\"results\":{\"bindings\":[" + string.Join(",",
            Binding("Q90000001", "Q11879590", "NormalRank", "澄音", "ja", "Sumine"), Binding("Q90000002", "Q101352", "PreferredRank", "霞森", "ja", "Kasumori"),
            Binding("Q90000003", "Q202444", "NormalRank", "晴灯", "zh"), Binding("Q90000004", "Q12308941", "DeprecatedRank", "晴燈", "ja"),
            Binding("Q90000005", "Q5", "NormalRank", "某", "ja"), Binding("Q90000006", "Q12308941", "NormalRank", "灯真", "ja", withRank: false)) + "]}}";
        var input = File("wikidata.json", json);
        var options = Options("wikidata-names") with { RetrievedAt = At, LanguageCountries = new(StringComparer.Ordinal) { ["ja"] = "JP" } };
        var batch = await new NameImportEngine().ReadAsync(input, options);
        Assert.Equal("json", batch.DetectedFormat);
        Assert.Equal(["Q90000001", "Q90000002"], batch.Records.Select(r => r.ExternalId!).Order(StringComparer.Ordinal));
        var sumine = batch.Records.Single(r => r.ExternalId == "Q90000001");
        Assert.Equal(("JP", NamePart.Given, NameGender.Female, "Sumine", (long?)null), (sumine.Country, sumine.Part, sumine.Gender!.Value, sumine.Romanized!, sumine.Count));
        Assert.True(sumine.SourceFields.ContainsKey("statement"));
        Assert.Equal(["binding-missing", "deprecated-statement", "language-not-mapped", "name-type"], batch.Diagnostics.Select(d => d.Code).Order());
        await Assert.ThrowsAsync<ArgumentException>(() => new NameImportEngine().ReadAsync(input, options with { RetrievedAt = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => new NameImportEngine().ReadAsync(input, options with { LanguageCountries = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => new NameImportEngine().ReadAsync(input, options with { License = "CC-BY-4.0" }));
        Assert.NotEqual(batch.Id, (await new NameImportEngine().ReadAsync(input, options with { LanguageCountries = new(StringComparer.Ordinal) { ["ja"] = "JP", ["zh"] = "CN" } })).Id);
        await new NameExporter().ExportAsync(await Approved(batch), PathFor("wikidata-release"), "1.0.0");
        await using var content = System.IO.File.OpenRead(Path.Combine(PathFor("wikidata-release"), "content.json"));
        var package = (await ContentDocuments.ReadValidatedAsync<ContentPackage>(content)).Value!;
        var entry = package.NamePools!.Single(p => p.Id == "names:jp.given").Entries.Single();
        Assert.Equal(("澄音", "Sumine"), (entry.Localized, entry.Romanized!));
        Assert.Equal("Q90000001", entry.Attestations.Single().ExternalId);
    }

    [Fact]
    public async Task WikidataEnglishLabelsAreNotRomanizationsOfLatinNames()
    {
        // Invented items: a feminine surname form whose English label is the base name, and a name whose label is identical.
        var json = "{\"head\":{\"vars\":[\"item\",\"type\",\"statement\",\"rank\",\"name\",\"romanized\"]},\"results\":{\"bindings\":[" + string.Join(",",
            Binding("Q90000011", "Q101352", "NormalRank", "Talwinówna", "pl", "Talwin"), Binding("Q90000012", "Q12308941", "NormalRank", "Orwen", "pl", "Orwen"),
            Binding("Q90000013", "Q101352", "NormalRank", "霞森", "ja", "Kasumori")) + "]}}";
        var options = Options("wikidata-names") with { RetrievedAt = At, LanguageCountries = new(StringComparer.Ordinal) { ["pl"] = "PL", ["ja"] = "JP" } };
        var batch = await new NameImportEngine().ReadAsync(File("latin.json", json), options);
        Assert.Null(batch.Records.Single(r => r.ExternalId == "Q90000011").Romanized);
        Assert.Null(batch.Records.Single(r => r.ExternalId == "Q90000012").Romanized);
        Assert.Equal("Kasumori", batch.Records.Single(r => r.ExternalId == "Q90000013").Romanized);
        Assert.Equal("Talwin", batch.Records.Single(r => r.ExternalId == "Q90000011").SourceFields["romanized"].GetProperty("value").GetString());
        Assert.Equal(["romanized-not-applicable"], batch.Diagnostics.Select(d => d.Code));
        Assert.Equal("1.1.0", batch.AdapterVersion);
    }

    [Fact]
    public async Task CommandLineRunsImportReviewRightsAndExport()
    {
        var input = File("yob2020.txt", "Talvina,F,120\nOrwen,M,95\n"); var license = File("license.txt", "Synthetic test license text.");
        var workspace = PathFor("cli.json");
        async Task<int> Run(params string[] args) => await Program.CreateCommand().Parse(args).InvokeAsync();
        Assert.Equal(0, await Run("import", "us-ssa", "--input", input, "--source-version", "synthetic-1", "--source-url", "https://example.org/iryomera/synthetic-names",
            "--license", "CC0-1.0", "--license-file", license, "--attribution", "Synthetic fixture", "--imported-at", "2026-10-17T12:00:00Z", "--birth-year", "2020", "--workspace", workspace));
        var batch = (await new NameWorkspaceStore(workspace).ReadAsync()).Batches.Single().Id;
        Assert.Equal(1, await Run("review", "--workspace", workspace, "--batch", batch, "--status", "approved", "--reviewer", "Test", "--at", "2026-10-17T12:00:00Z", "--note", "Missing scope"));
        Assert.Equal(0, await Run("review", "--workspace", workspace, "--batch", batch, "--all", "--status", "approved", "--reviewer", "Test", "--at", "2026-10-17T12:00:00Z", "--note", "Synthetic"));
        Assert.Equal(1, await Run("rights", "--workspace", workspace, "--batch", batch, "--decision", "permitted", "--reviewer", "Test", "--at", "2026-10-17T12:00:00Z", "--note", "No evidence"));
        Assert.Equal(0, await Run("rights", "--workspace", workspace, "--batch", batch, "--decision", "permitted", "--permission-evidence", "Synthetic CC0 terms",
            "--reviewer", "Test", "--at", "2026-10-17T12:00:00Z", "--note", "Synthetic"));
        Assert.Equal(0, await Run("validate", "--workspace", workspace));
        Assert.Equal(0, await Run("status", "--workspace", workspace, "--records"));
        Assert.Equal(0, await Run("export", "--workspace", workspace, "--output", PathFor("cli-release"), "--version", "1.0.0"));
        Assert.True(System.IO.File.Exists(Path.Combine(PathFor("cli-release"), "content.json")));
        Assert.Equal(0, await Run("sources"));
    }
}
