using Xunit;

namespace Iryomera.ModKit.Tests;

public sealed class CliTests
{
    [Fact]
    public async Task RealCommandTreeCreatesValidatesPacksInspectsAndUnpacks()
    {
        using var data = new TestMod();
        async Task Run(params string[] args) => Assert.Equal(0, await Program.CreateCommand().Parse(args).InvokeAsync());
        await Run("new", "--output", data.Project, "--namespace", "author.example", "--author", "Own test", "--name", "Own fixture");
        await Run("validate", "--input", data.Project, "--game-version", "0.2.0");
        var dependencyPath = data.PathFor("dependency with spaces.json");
        var dependency = new Iryomera.Content.SDK.ContentPackage { Manifest = new() { Id = "extra:package", Namespace = "extra", Version = "1.0.0", License = "MIT" } };
        await File.WriteAllTextAsync(dependencyPath, System.Text.Json.JsonSerializer.Serialize(dependency, Iryomera.Content.SDK.ContentJson.Options));
        await Run("validate", "--input", data.Project, "--dependency", dependencyPath);
        var replacement = data.PathFor("notice.txt"); await File.WriteAllTextAsync(replacement, "Own fixture license comment");
        await Run("edit", "--input", data.Project, "--path", "LICENSE.txt", "--replacement", replacement);
        var zip = data.PathFor("cli.zip"); await Run("pack", "--input", data.Project, "--output", zip);
        await Run("inspect", "--input", zip); await Run("unpack", "--input", zip, "--output", data.PathFor("unpacked"));
        Assert.True(File.Exists(Path.Combine(data.PathFor("unpacked"), "assets/cube.glb")));
        Assert.Equal(2, await Program.CreateCommand().Parse(["validate", "--input", data.Project, "--game-version", "2.0.0"]).InvokeAsync());
        Assert.Equal(1, await Program.CreateCommand().Parse(["unpack", "--input", zip, "--output", data.PathFor("unpacked")]).InvokeAsync());
    }
}
