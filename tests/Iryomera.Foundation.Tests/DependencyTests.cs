using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class DependencyTests
{
    private static ContentPackage Root(string range = "[1.0.0,2.0.0)") => new()
    {
        Manifest = new ContentPackageManifest
        {
            Id = "addon:package", Namespace = "addon", Version = "1.0.0", License = "MIT",
            Dependencies = [new() { PackageId = "synthetic:foundation", VersionRange = range }]
        }
    };

    [Fact]
    public void DependenciesAreExplicitAndVersioned()
    {
        Assert.Contains(ContentValidator.Validate(Root()), x => x.Code == "missing-dependency");
        Assert.Empty(ContentValidator.Validate(Root(), [Fixtures.Package()]));
        Assert.Contains(ContentValidator.Validate(Root("[2.0.0,3.0.0)"), [Fixtures.Package()]), x => x.Code == "dependency-version");
        Assert.Contains(ContentValidator.Validate(Root("invalid range"), [Fixtures.Package()]), x => x.Code == "version-range");
    }

    [Fact]
    public void ReferencesRequireDeclaredDependencies()
    {
        var root = Root();
        root.Relations.Add(Fixtures.Package().Relations[0] with { Id = "addon:relation" });
        Assert.Empty(ContentValidator.Validate(root, [Fixtures.Package()]));
        root.Manifest.Dependencies.Clear();
        Assert.Contains(ContentValidator.Validate(root, [Fixtures.Package()]), x => x.Code == "undeclared-dependency");
    }

    [Fact]
    public void CyclesAndDuplicatePackagesAreDetected()
    {
        var root = Root();
        var dependency = Fixtures.Package();
        dependency.Manifest.Dependencies.Add(new() { PackageId = root.Manifest.Id, VersionRange = "[1.0.0]" });
        Assert.Contains(ContentValidator.Validate(root, [dependency]), x => x.Code == "dependency-cycle");
        Assert.Contains(ContentValidator.Validate(root, [root]), x => x.Code == "duplicate-package");
    }

    [Theory]
    [InlineData("../content.json")]
    [InlineData("/content.json")]
    [InlineData("C:/content.json")]
    [InlineData("data\\content.json")]
    [InlineData("data/../content.json")]
    public async Task ManifestRejectsUnsafePaths(string path)
    {
        var mod = await Fixtures.Load<ModManifest>("mod-manifest");
        mod.ContentFiles.Add(path);
        Assert.Contains(ManifestValidator.Validate(mod), x => x.Code == "content-path");
    }

    [Fact]
    public async Task ExampleModManifestIsAValidMetadataContract()
    {
        var mod = await Fixtures.Load<ModManifest>("mod-manifest");
        Assert.Empty(ManifestValidator.Validate(mod));
    }
}
