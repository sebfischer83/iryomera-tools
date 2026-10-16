using System.Text;
using System.Text.Json;
using Iryomera.Content.SDK;

namespace Iryomera.ModKit.Tests;

internal sealed class TestMod : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "iryomera-modkit-tests-" + Guid.NewGuid().ToString("N"));
    internal string PathFor(string name) => Path.Combine(Root, name);
    internal string Project => PathFor("project");
    internal TestMod() => Directory.CreateDirectory(Root);
    internal Task Create() => new ModProjects().CreateAsync(Project, "author.example", "Synthetic test author", "Own synthetic fixture");
    internal async Task<T> Read<T>(string relative) => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(Path.Combine(Project, relative)), ContentJson.Options)!;
    internal Task Write<T>(string relative, T data) => File.WriteAllTextAsync(Path.Combine(Project, relative), JsonSerializer.Serialize(data, ContentJson.Options), new UTF8Encoding(false));
    internal async Task<string> Pack()
    { var path = PathFor("mod.zip"); await new ModArchive().PackAsync(Project, path); return path; }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
