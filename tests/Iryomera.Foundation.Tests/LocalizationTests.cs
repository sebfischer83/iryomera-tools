using System.Globalization;
using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.Localization;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class LocalizationTests
{
    private static readonly string[] UiKeys = ["synthetic.ui.patients", "synthetic.ui.cost", "synthetic.ui.welcome", "synthetic.ui.positional", "synthetic.mod.name"];
    private static ContentLocalizer Localizer() => new([Fixtures.Catalog("en"), Fixtures.Catalog("de")]);

    [Fact]
    public async Task LanguagesAreCompleteAndSupportAsyncImportExport()
    {
        var en = await Fixtures.Load<LocalizationDefinition>("en");
        var de = await Fixtures.Load<LocalizationDefinition>("de");
        Assert.Empty(LocalizationValidator.Validate([en, de], LocalizationValidator.GetContentKeys(Fixtures.Package()).Concat(UiKeys)));
        await using var stream = new MemoryStream();
        await ContentJson.WriteAsync(stream, de);
        stream.Position = 0;
        var roundTrip = await ContentJson.ReadAsync<LocalizationDefinition>(stream);
        Assert.Equal(JsonSerializer.Serialize(de, ContentJson.Options), JsonSerializer.Serialize(roundTrip, ContentJson.Options));
    }

    [Theory]
    [InlineData("en", 1, "One demo patient")]
    [InlineData("en", 2, "2 demo patients")]
    [InlineData("de", 1, "Ein Demo-Patient")]
    [InlineData("de-DE", 2, "2 Demo-Patienten")]
    public void PluralFormsRespectCulture(string language, int count, string expected) =>
        Assert.Equal(expected, Localizer().Format("synthetic.ui.patients", CultureInfo.GetCultureInfo(language), new Dictionary<string, object?> { ["count"] = count }));

    [Theory]
    [InlineData("en-US", "Simulation cost: 1,234.50")]
    [InlineData("de-DE", "Simulationskosten: 1.234,50")]
    public void NumericFormattingRespectsCulture(string language, string expected) =>
        Assert.Equal(expected, Localizer().Format("synthetic.ui.cost", CultureInfo.GetCultureInfo(language), new Dictionary<string, object?> { ["amount"] = 1234.5m }));

    [Fact]
    public void MissingLanguageAndMissingKeysUseEnglishThenKeyFallback()
    {
        var de = Fixtures.Catalog("de");
        de.Entries.Remove("synthetic.ui.welcome");
        var localizer = new ContentLocalizer([Fixtures.Catalog("en"), de]);
        var args = new Dictionary<string, object?> { ["name"] = "Ada" };
        Assert.Equal("Hello Ada", localizer.Format("synthetic.ui.welcome", CultureInfo.GetCultureInfo("de-AT"), args));
        Assert.Equal("Hello Ada", localizer.Format("synthetic.ui.welcome", CultureInfo.GetCultureInfo("fr"), args));
        Assert.Equal("synthetic.missing.key", localizer.Format("synthetic.missing.key", CultureInfo.GetCultureInfo("de")));
    }

    [Fact]
    public void MissingUnusedAndMismatchedKeysAreReported()
    {
        var en = Fixtures.Catalog("en");
        var de = Fixtures.Catalog("de");
        de.Entries.Remove("synthetic.ui.cost");
        de.Entries["synthetic.ui.welcome"] = "Hallo {person}";
        var issues = LocalizationValidator.Validate([en, de], ["synthetic.ui.welcome", "synthetic.missing.key"]);
        foreach (var code in new[] { "missing-translation", "unused-key", "placeholder-mismatch", "missing-key" })
            Assert.Contains(issues, x => x.Code == code);
    }

    [Theory]
    [InlineData("Broken {name")]
    [InlineData("Unsafe {name.Property}")]
    [InlineData("Unsupported {name:unknown:}")]
    public void InvalidTranslationFormatsAreDetected(string value)
    {
        var en = Fixtures.Catalog("en");
        en.Entries["synthetic.ui.welcome"] = value;
        Assert.Contains(LocalizationValidator.Validate([en, Fixtures.Catalog("de")]), x => x.Code == "text-format");
    }

    [Fact]
    public void NestedPluralPlaceholdersAreCompared()
    {
        var de = Fixtures.Catalog("de");
        de.Entries["synthetic.ui.patients"] = "{count:plural:Ein Patient|{other} Patienten}";
        Assert.Contains(LocalizationValidator.Validate([Fixtures.Catalog("en"), de]), x => x.Code == "placeholder-mismatch");
    }

    [Fact]
    public void NullTranslationAndDuplicateCatalogAreRejected()
    {
        var en = Fixtures.Catalog("en");
        en.Entries["synthetic.ui.welcome"] = null!;
        Assert.Contains(LocalizationValidator.Validate([en]), x => x.Code == "required");
        Assert.Contains(LocalizationValidator.Validate([Fixtures.Catalog("en"), Fixtures.Catalog("en")]), x => x.Code == "duplicate-catalog");
    }

    [Fact]
    public void EveryNamespaceNeedsEnglishAndGerman()
    {
        Assert.Contains(LocalizationValidator.Validate([Fixtures.Catalog("de")]), x => x.Code == "missing-reference-language");
        Assert.Contains(LocalizationValidator.Validate([Fixtures.Catalog("en")]), x => x.Code == "missing-language");
    }

    [Fact]
    public void ModCatalogsAreIndependentAndDoNotChangeIds()
    {
        var p = Fixtures.Package();
        var ids = p.Definitions.Select(x => x.Id).ToArray();
        var mod = new LocalizationDefinition { Namespace = "author.demo", Language = "en", Entries = new() { ["author.demo.disease.name"] = "Mod disease" } };
        var localizer = new ContentLocalizer([Fixtures.Catalog("en"), Fixtures.Catalog("de"), mod]);
        Assert.Equal("Mod disease", localizer.Format("author.demo.disease.name", CultureInfo.GetCultureInfo("de")));
        Assert.Equal("Synthetisches Bernsteinsyndrom", localizer.Format(p.Definitions[0].NameKey, CultureInfo.GetCultureInfo("de")));
        Assert.Equal(ids, p.Definitions.Select(x => x.Id));
    }

    [Fact]
    public void DotNetAdapterSupportsCurrentCultureAndPositionalVariables()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var adapter = new ContentStringLocalizer(Localizer());
            Assert.Equal("Demo-Wert: 1.234,50", adapter["synthetic.ui.positional", 1234.5m].Value);
            Assert.False(adapter["synthetic.ui.welcome"].ResourceNotFound);
            Assert.True(adapter["synthetic.no.key"].ResourceNotFound);
            Assert.NotEmpty(adapter.GetAllStrings(true));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
