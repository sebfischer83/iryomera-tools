using System.Text;
using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.ModKit.Tests;

public sealed class ProjectTests
{
    [Fact]
    public async Task SyntheticScaffoldActuallySupportsAllDefinitionKindsAssetsAndBilingualContent()
    {
        using var data = new TestMod(); await data.Create(); var result = await new ModValidator().ValidateAsync(data.Project, gameVersion: "0.2.0");
        Assert.Empty(result.Issues); Assert.Equal(14, result.Content.Definitions.Count); Assert.Equal(2, result.Catalogs.Count);
        Assert.All(result.Content.Definitions, d => Assert.Equal(ReviewStatus.Unreviewed, d.Provenance.ReviewStatus));
        var furniture = result.Content.Definitions.OfType<FurnitureDefinition>().Single(f => f.Assets is not null); Assert.Equal(3, furniture.Assets!.Count); Assert.Single(furniture.Placement!.InteractionPoints);
        var wallItem = result.Content.Definitions.OfType<FurnitureDefinition>().Single(f => f.CatalogAssets is not null);
        Assert.Null(wallItem.Assets); Assert.Equal(FurnitureMount.Wall, wallItem.Placement!.Mount);
        Assert.DoesNotContain(result.Project.Files, f => f.Path.Contains("notice_board", StringComparison.Ordinal));
        Assert.Contains(result.Content.Relations, r => r.Kind == RelationKind.RoomFurniture);
        Assert.Contains((await new ModValidator().ValidateAsync(data.Project, gameVersion: "2.0.0")).Issues, i => i.Code == "game-version");
    }
    [Fact]
    public async Task EditIsValidatedAtomicAndPreservesUnknownFieldsAndOriginalSource()
    {
        using var data = new TestMod(); await data.Create(); var before = await data.Read<ContentPackage>("content.json"); var old = before.Definitions[0];
        before.Definitions[0] = old with { Extensions = new() { ["futureField"] = JsonSerializer.SerializeToElement(42) } };
        var replacement = data.PathFor("replacement.json"); await File.WriteAllTextAsync(replacement, JsonSerializer.Serialize(before, ContentJson.Options));
        await new ModProjects().EditAsync(data.Project, "content.json", replacement);
        var saved = await data.Read<ContentPackage>("content.json"); Assert.Equal(42, saved.Definitions[0].Extensions!["futureField"].GetInt32()); Assert.Equal(JsonSerializer.Serialize(old.Provenance, ContentJson.Options), JsonSerializer.Serialize(saved.Definitions[0].Provenance, ContentJson.Options));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(data.Project, "content.json"));
        saved.Definitions.Add(saved.Definitions[0]); await File.WriteAllTextAsync(replacement, JsonSerializer.Serialize(saved, ContentJson.Options));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModProjects().EditAsync(data.Project, "content.json", replacement));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(data.Project, "content.json")));
    }
    [Fact]
    public async Task ExplicitDependenciesResolveSharedExaminationsAndMissingVersionsFail()
    {
        using var data = new TestMod(); await data.Create(); var package = await data.Read<ContentPackage>("content.json"); var project = await data.Read<ModProject>("mod.json");
        var exam = package.Definitions.OfType<ExaminationDefinition>().Single() with { Id = "shared:examination" };
        var dependency = new ContentPackage { Manifest = new() { Id = "shared:mod", Namespace = "shared", Version = "1.0.0", License = "MIT" }, Definitions = [exam] };
        var requirements = new List<ContentDependency> { new() { PackageId = dependency.Manifest.Id, VersionRange = "[1.0.0,2.0.0)" } };
        project = project with { Manifest = project.Manifest with { Dependencies = requirements } }; package = package with { Manifest = package.Manifest with { Dependencies = requirements } };
        package.Relations.Add(new() { Id = "author.example:shared_test", Kind = RelationKind.DiseaseExamination, Version = "1.0.0", FromId = package.Definitions.OfType<DiseaseDefinition>().Single().Id, ToId = exam.Id, Provenance = exam.Provenance });
        await data.Write("mod.json", project); await data.Write("content.json", package);
        Assert.Contains((await new ModValidator().ValidateAsync(data.Project)).Issues, i => i.Code == "missing-dependency");
        Assert.Empty((await new ModValidator().ValidateAsync(data.Project, [dependency])).Issues);
        Assert.Contains((await new ModValidator().ValidateAsync(data.Project, [dependency with { Manifest = dependency.Manifest with { Version = "2.0.0" } }])).Issues, i => i.Code == "dependency-version");
        var archive = data.PathFor("dependent.zip"); await new ModArchive().PackAsync(data.Project, archive, [dependency]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModArchive().InspectAsync(archive));
        Assert.True((await new ModArchive().InspectAsync(archive, [dependency])).IsValid);
    }
    [Fact]
    public async Task OptionalListsFromSeveralContentFilesAreMerged()
    {
        using var data = new TestMod(); await data.Create(); var package = await data.Read<ContentPackage>("content.json"); var project = await data.Read<ModProject>("mod.json");
        var provenance = package.Definitions[0].Provenance;
        var names = new ContentPackage
        {
            Manifest = package.Manifest,
            NamePools = [new() { Id = "author.example:names.given", Version = "1.0.0", NameKey = "author.example.pool.given.name", Country = "DE", Part = NamePart.Given, Provenance = provenance,
                Sources = [new() { Key = "own", Provenance = provenance }], Entries = [new() { Key = "talvine", Localized = "Talvine", Gender = NameGender.Female, Attestations = [new() { SourceKey = "own" }] }] }],
            NameFormats = [new() { Id = "author.example:names.format_de", Version = "1.0.0", Country = "DE", Order = NameOrder.GivenFamily, Provenance = provenance }]
        };
        await data.Write("names.json", names);
        foreach (var language in new[] { "en", "de" })
        {
            var catalog = await data.Read<LocalizationDefinition>("localization/" + language + ".json"); catalog.Entries["author.example.pool.given.name"] = language == "en" ? "Synthetic given names" : "Synthetische Vornamen";
            await data.Write("localization/" + language + ".json", catalog);
        }
        project = project with { Manifest = project.Manifest with { ContentFiles = [.. project.Manifest.ContentFiles, "names.json"] }, Files = [.. project.Files, new() { Path = "names.json", Kind = ModFileKind.Content, License = "MIT" }] };
        await data.Write("mod.json", project);
        var result = await new ModValidator().ValidateAsync(data.Project);
        Assert.Empty(result.Issues);
        Assert.Equal("author.example:names.given", result.Content.NamePools!.Single().Id);
        Assert.Single(result.Content.NameFormats!);
        Assert.Null(result.Content.GameplayOverrides);
    }
    [Theory]
    [InlineData("missing-language", "missing-language")]
    [InlineData("placeholder", "placeholder-mismatch")]
    [InlineData("asset", "asset-reference")]
    [InlineData("identity", "package-manifest")]
    public async Task CatalogAssetAndIdentityProblemsAreNotValidPackages(string change, string code)
    {
        using var data = new TestMod(); await data.Create();
        if (change is "missing-language" or "placeholder")
        {
            var de = await data.Read<LocalizationDefinition>("localization/de.json");
            if (change == "missing-language") de = de with { Language = "fr" }; else de.Entries["author.example.demo_disease.name"] = "{unmatched}";
            await data.Write("localization/de.json", de);
        }
        else
        {
            var p = await data.Read<ContentPackage>("content.json");
            if (change == "identity") p = p with { Manifest = p.Manifest with { Version = "2.0.0" } };
            else p.Definitions.OfType<FurnitureDefinition>().Single(f => f.Assets is not null).Assets![0] = p.Definitions.OfType<FurnitureDefinition>().Single(f => f.Assets is not null).Assets![0] with { License = "Wrong" };
            await data.Write("content.json", p);
        }
        Assert.Contains((await new ModValidator().ValidateAsync(data.Project)).Issues, i => i.Code == code);
    }
    [Fact]
    public async Task OriginalDocumentVersionsAndDuplicatePropertiesAreRequiredBeforeDefaults()
    {
        await using var missing = new MemoryStream(Encoding.UTF8.GetBytes("{\"files\":[]}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => ModDocuments.ReadAsync<ModProject>(missing));
        await using var duplicate = new MemoryStream(Encoding.UTF8.GetBytes("{\"schemaVersion\":\"1.0.0\",\"schemaVersion\":\"1.0.0\",\"files\":[]}"));
        await Assert.ThrowsAsync<JsonException>(() => ModDocuments.ReadAsync<ModArchiveIndex>(duplicate));
    }
    [Fact]
    public async Task MultipleContentFilesShareOneReferenceGraph()
    {
        using var data = new TestMod(); await data.Create(); var p = await data.Read<ContentPackage>("content.json"); var m = await data.Read<ModProject>("mod.json");
        await data.Write("part.json", p with { Definitions = p.Definitions.Skip(6).ToList() });
        await data.Write("content.json", p with { Definitions = p.Definitions.Take(6).ToList(), Relations = [], Gameplay = [] });
        m.Files.Add(new() { Path = "part.json", Kind = ModFileKind.Content, License = "MIT" }); m.Manifest.ContentFiles.Add("part.json"); await data.Write("mod.json", m);
        var result = await new ModValidator().ValidateAsync(data.Project); Assert.Empty(result.Issues);
        Assert.Equal(p.Extensions!["examplePurpose"].GetString(), result.Content.Extensions!["examplePurpose"].GetString());
        var second = await data.Read<ContentPackage>("part.json"); second.Extensions!["examplePurpose"] = JsonSerializer.SerializeToElement("Conflicting metadata"); await data.Write("part.json", second);
        Assert.Contains((await new ModValidator().ValidateAsync(data.Project)).Issues, i => i.Code == "extension-conflict");
    }
    [Fact]
    public async Task ModResourceIdsMustResolveWhileSymbolicResourceNamesRemainSupported()
    {
        using var data = new TestMod(); await data.Create(); var content = await data.Read<ContentPackage>("content.json");
        content.Gameplay[0].RequiredResources["author.example:missing_resource"] = 1; await data.Write("content.json", content);
        Assert.Contains((await new ModValidator().ValidateAsync(data.Project)).Issues, i => i.Code == "resource-reference");
        content.Gameplay[0].RequiredResources.Remove("author.example:missing_resource"); content.Gameplay[0].RequiredResources["power_supply"] = 1; await data.Write("content.json", content);
        Assert.Empty((await new ModValidator().ValidateAsync(data.Project)).Issues);
    }
    [Fact]
    public async Task CatalogOnlyModsDoNotNeedDummyMedicalDefinitions()
    {
        using var data = new TestMod(); await data.Create(); var project = await data.Read<ModProject>("mod.json");
        project.Files.RemoveAll(f => f.Kind is not (ModFileKind.Localization or ModFileKind.Notice));
        project.Manifest.ContentFiles.Clear(); project.Manifest.ContentFiles.AddRange(project.Files.Where(f => f.Kind == ModFileKind.Localization).Select(f => f.Path)); await data.Write("mod.json", project);
        var result = await new ModValidator().ValidateAsync(data.Project); Assert.Empty(result.Issues); Assert.Empty(result.Content.Definitions);
        Assert.True((await new ModArchive().InspectAsync(await data.Pack())).IsValid);
    }
    [Fact]
    public async Task BalancingOnlyModsCanReferenceDependencyDefinitions()
    {
        using var data = new TestMod(); await data.Create(); var content = await data.Read<ContentPackage>("content.json"); var project = await data.Read<ModProject>("mod.json");
        var exam = content.Definitions.OfType<ExaminationDefinition>().Single() with { Id = "shared:exam" };
        var dependency = new ContentPackage { Manifest = new() { Id = "shared:package", Namespace = "shared", Version = "1.0.0", License = "MIT" }, Definitions = [exam] };
        var required = new ContentDependency { PackageId = dependency.Manifest.Id, VersionRange = "[1.0.0]" };
        content = content with { Definitions = [], Relations = [], Gameplay = [new() { DefinitionId = exam.Id, ExaminationCost = 5 }], Manifest = content.Manifest with { Dependencies = [required] } };
        project = project with { Manifest = project.Manifest with { Dependencies = [required] } };
        await data.Write("mod.json", project); await data.Write("content.json", content);
        Assert.Empty((await new ModValidator().ValidateAsync(data.Project, [dependency])).Issues);
    }
}
