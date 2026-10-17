using System.Text.Json;
using System.Text.Json.Nodes;
using Iryomera.Content.SDK;
using Iryomera.Localization;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class NameTests
{
    private static ContentPackage Names() => JsonSerializer.Deserialize<ContentPackage>(File.ReadAllText(Fixtures.Example("names")), ContentJson.Options)!;
    private static NamePool Pool(ContentPackage package, string id) => package.NamePools!.Single(p => p.Id == id);
    private static void Replace(ContentPackage package, NamePool pool) => package.NamePools![package.NamePools.FindIndex(p => p.Id == pool.Id)] = pool;

    private static ContentPackage Mod(params NameFormat[] formats)
    {
        var names = Names();
        var source = Pool(names, "name_demo:de.family");
        return new()
        {
            Manifest = new() { Id = "author.names:mod", Namespace = "author.names", Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = "name_demo:names", VersionRange = "[1.0.0,2.0.0)" }] },
            NamePools = [source with { Id = "author.names:de.family", NameKey = "author.names.pool.de_family.name", Entries = [source.Entries[0], source.Entries[0] with { Key = "aaa_first", Localized = "Aschtal" }] }],
            NameFormats = [.. formats]
        };
    }

    [Fact]
    public async Task ExampleValidatesAgainstSchemasAndSemanticsOnBothRuntimes()
    {
        await using var input = File.OpenRead(Fixtures.Example("names"));
        var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(input);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        var package = result.Value!;
        Assert.Equal(5, package.NamePools!.Count);
        foreach (var pool in package.NamePools)
            Assert.True(Fixtures.Evaluate("namePool", JsonSerializer.SerializeToNode(pool, ContentJson.Options)).IsValid, pool.Id);
        foreach (var format in package.NameFormats!)
            Assert.True(Fixtures.Evaluate("nameFormat", JsonSerializer.SerializeToNode(format, ContentJson.Options)).IsValid, format.Id);
        var keys = package.NamePools.Select(p => p.NameKey).Order(StringComparer.Ordinal).ToArray();
        foreach (var language in new[] { "names.en", "names.de" })
            Assert.Equal(keys, Fixtures.Catalog(language).Entries.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(keys, LocalizationValidator.GetContentKeys(package).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task RoundTripPreservesOriginalScriptAndLegacyPackagesOmitNameFields()
    {
        var package = Names();
        await using var bytes = new MemoryStream();
        await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var reloaded = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(reloaded.IsValid, string.Join(';', reloaded.Issues));
        var entry = Pool(reloaded.Value!, "name_demo:jp.given").Entries.Single(e => e.Key == "haruto_demo");
        Assert.Equal(("晴燈", "Harutō"), (entry.Localized, entry.Romanized));
        var legacy = JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options);
        Assert.DoesNotContain("namePools", legacy); Assert.DoesNotContain("nameFormats", legacy);
    }

    [Fact]
    public void SelectionOrderIsIndependentOfPackageAndEntryOrder()
    {
        var names = Names(); var mod = Mod();
        var expected = NamePools.Select([names, mod], "DE", NamePart.Family, "DE-BY").Select(e => (e.Pool.Id, e.Entry.Key)).ToArray();
        Assert.Equal(new[]
        {
            ("author.names:de.family", "aaa_first"), ("author.names:de.family", "brennwald"),
            ("name_demo:de.family", "brennwald"), ("name_demo:de.family", "quellmar"), ("name_demo:de_by.family", "almbrunner")
        }, expected);
        names.NamePools!.Reverse();
        foreach (var pool in names.NamePools.ToArray()) Replace(names, pool with { Entries = [.. Enumerable.Reverse(pool.Entries)] });
        Assert.Equal(expected, NamePools.Select([mod, names], "DE", NamePart.Family, "DE-BY").Select(e => (e.Pool.Id, e.Entry.Key)).ToArray());
        Assert.DoesNotContain(NamePools.Select([names], "DE", NamePart.Family), e => e.Pool.Region is not null);
        Assert.Empty(ContentValidator.Validate(mod, [names]));
    }

    [Fact]
    public void FormatsResolveRegionThenCountry()
    {
        var names = Names();
        Assert.Equal("", NamePools.ResolveFormat([names], "JP")!.LocalizedSeparator);
        Assert.Equal(NameOrder.FamilyGiven, NamePools.ResolveFormat([names], "JP")!.Order);
        Assert.Equal("name_demo:format.de", NamePools.ResolveFormat([names], "DE", "DE-BY")!.Id);
        Assert.Equal(2, NamePools.ResolveFormat([names], "ES")!.FamilyNameParts);
        Assert.Null(NamePools.ResolveFormat([names], "PL"));
    }

    [Fact]
    public void CompetingFormatsAreAnErrorEvenWithoutADependency()
    {
        var names = Names();
        var de = names.NameFormats![0];
        var mod = Mod(de with { Id = "author.names:format.de" }) with { Manifest = Mod().Manifest with { Dependencies = [] }, NamePools = null };
        var conflicts = ContentValidator.Validate(mod, [names]).Where(i => i.Code == "name-format-conflict").Select(i => i.Path).ToArray();
        Assert.Equal(new[] { "author.names:format.de", "name_demo:format.de" }, conflicts);
        Assert.Throws<InvalidOperationException>(() => NamePools.ResolveFormat([names, mod], "DE"));
        var regional = Mod(de with { Id = "author.names:format.de_by", Region = "DE-BY" });
        Assert.DoesNotContain(ContentValidator.Validate(regional, [names]), i => i.Code == "name-format-conflict");
        Assert.Equal("author.names:format.de_by", NamePools.ResolveFormat([names, regional], "DE", "DE-BY")!.Id);
    }

    [Theory]
    [InlineData("bidi", "name-text")]
    [InlineData("control", "name-text")]
    [InlineData("nfd", "name-text")]
    [InlineData("surrogate", "name-text")]
    [InlineData("spaces", "name-text")]
    [InlineData("length", "name-text")]
    [InlineData("romanized-script", "name-romanized")]
    [InlineData("romanization-source", "name-source")]
    [InlineData("duplicate-key", "name-duplicate-key")]
    [InlineData("duplicate-entry", "name-duplicate-entry")]
    [InlineData("unknown-source", "name-source")]
    [InlineData("no-attestation", "name-attestation")]
    [InlineData("gender", "name-gender")]
    [InlineData("rank", "name-statistic")]
    [InlineData("proportion", "name-statistic")]
    [InlineData("cohort-order", "name-cohort")]
    [InlineData("cohort-bound", "name-cohort")]
    [InlineData("country", "name-country")]
    [InlineData("region", "name-region")]
    [InlineData("empty", "name-empty-pool")]
    [InlineData("separator", "name-format")]
    [InlineData("parts", "name-format")]
    [InlineData("pool-key", "localization-key")]
    [InlineData("pool-owner", "namespace-owner")]
    public void InvalidPoolsAndFormatsAreRejected(string change, string code)
    {
        var package = Names();
        var pool = Pool(package, "name_demo:de.given");
        var first = pool.Entries[0];
        var changed = change switch
        {
            "bidi" => pool with { Entries = [first with { Localized = "Tal‮vine" }] },
            "control" => pool with { Entries = [first with { Localized = "Tal\u0007vine" }] },
            "nfd" => pool with { Entries = [first with { Localized = "José" }] },
            "surrogate" => pool with { Entries = [first with { Localized = "Tal\uD800vine" }] },
            "spaces" => pool with { Entries = [first with { Localized = "Tal  vine" }] },
            "length" => pool with { Entries = [first with { Localized = new string('a', 101) }] },
            "romanized-script" => pool with { Entries = [first with { Romanized = "Тальвина" }] },
            "romanization-source" => pool with { Entries = [first with { RomanizationSourceKey = "synthetic" }] },
            "duplicate-key" => pool with { Entries = [first, pool.Entries[1] with { Key = first.Key }] },
            "duplicate-entry" => pool with { Entries = [first, first with { Key = "other" }] },
            "unknown-source" => pool with { Entries = [first with { Attestations = [first.Attestations[0] with { SourceKey = "missing" }] }] },
            "no-attestation" => pool with { Entries = [first with { Attestations = [] }] },
            "gender" => pool with { Entries = [first with { Gender = null }] },
            "rank" => pool with { Entries = [first with { Attestations = [first.Attestations[0] with { Rank = 0 }] }] },
            "proportion" => pool with { Entries = [first with { Attestations = [first.Attestations[0] with { Proportion = 1.5m }] }] },
            "cohort-order" => pool with { Entries = [first with { Attestations = [first.Attestations[0] with { BirthYearFrom = 1960, BirthYearTo = 1950 }] }] },
            "cohort-bound" => pool with { Entries = [first with { Attestations = [first.Attestations[0] with { BirthYearTo = null }] }] },
            "country" => pool with { Country = "de" },
            "region" => pool with { Region = "AT-9" },
            "empty" => pool with { Entries = [] },
            "pool-key" => pool with { NameKey = "bad key" },
            "pool-owner" => pool with { Id = "other:de.given" },
            _ => pool
        };
        package.NamePools![package.NamePools.IndexOf(pool)] = changed;
        if (change is "separator" or "parts")
            package.NameFormats![0] = change == "separator" ? package.NameFormats[0] with { LocalizedSeparator = "-" } : package.NameFormats[0] with { FamilyNameParts = 4 };
        Assert.Contains(ContentValidator.Validate(package), issue => issue.Code == code);
    }

    [Fact]
    public void JoinersInNonLatinScriptsAndLatinDiacriticsRemainValid()
    {
        var package = Names(); var pool = Pool(package, "name_demo:de.given"); var first = pool.Entries[0];
        Replace(package, pool with { Entries = [first with { Localized = "می‌نا", Romanized = "Mīnā" }, pool.Entries[1] with { Localized = "María José", Romanized = "Kaʻiulani O'Neill-Smith" }] });
        Assert.Empty(ContentValidator.Validate(package));
    }

    [Theory]
    [InlineData("gender")]
    [InlineData("attestations")]
    [InlineData("country")]
    [InlineData("localized")]
    [InlineData("sources")]
    public void SchemasRejectMalformedPools(string change)
    {
        var node = JsonSerializer.SerializeToNode(Pool(Names(), "name_demo:de.given"), ContentJson.Options)!;
        var entry = node["entries"]![0]!.AsObject();
        switch (change)
        {
            case "gender": entry.Remove("gender"); break;
            case "attestations": entry["attestations"] = new JsonArray(); break;
            case "country": node["country"] = "DEU"; break;
            case "localized": entry["localized"] = " Talvine"; break;
            case "sources": node["sources"] = new JsonArray(); break;
        }
        Assert.False(Fixtures.Evaluate("namePool", node).IsValid);
    }
}
