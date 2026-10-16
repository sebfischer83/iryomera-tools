using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.ModKit.Tests;

public sealed class ArchiveTests
{
    [Fact]
    public async Task PackInspectUnpackAreReproducibleAndPreserveEveryDeclaredByte()
    {
        using var data = new TestMod(); await data.Create(); var zip = await data.Pack(); var second = data.PathFor("again.zip");
        await new ModArchive().PackAsync(data.Project, second); Assert.Equal(await File.ReadAllBytesAsync(zip), await File.ReadAllBytesAsync(second));
        var result = await new ModArchive().InspectAsync(zip); Assert.True(result.IsValid);
        var output = data.PathFor("unpacked"); await new ModArchive().UnpackAsync(zip, output);
        foreach (var file in result.Project.Files.Select(f => f.Path).Append("mod.json")) Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(data.Project, file)), await File.ReadAllBytesAsync(Path.Combine(output, file)));
        await Assert.ThrowsAsync<IOException>(() => new ModArchive().UnpackAsync(zip, output));
        await Assert.ThrowsAsync<IOException>(() => new ModArchive().PackAsync(data.Project, zip));
        await File.WriteAllTextAsync(Path.Combine(data.Project, "unlisted-secret.txt"), "Not declared; do not package");
        var third = data.PathFor("declared-only.zip"); await new ModArchive().PackAsync(data.Project, third);
        using var check = ZipFile.OpenRead(third); Assert.DoesNotContain(check.Entries, e => e.FullName == "unlisted-secret.txt");
    }

    private static async Task<List<(string Name, byte[] Bytes, int Attributes)>> Entries(string path)
    {
        using var zip = ZipFile.OpenRead(path); var result = new List<(string, byte[], int)>();
        foreach (var entry in zip.Entries)
        { await using var stream = entry.Open(); using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes); result.Add((entry.FullName, bytes.ToArray(), entry.ExternalAttributes)); }
        return result;
    }
    private static void Write(string path, IEnumerable<(string Name, byte[] Bytes, int Attributes)> entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in entries)
        { var entry = zip.CreateEntry(item.Name); entry.ExternalAttributes = item.Attributes; using var output = entry.Open(); output.Write(item.Bytes); }
    }
    [Theory]
    [InlineData("../escape.json")]
    [InlineData("/escape.json")]
    [InlineData("C:/escape.json")]
    [InlineData("assets\\escape.png")]
    [InlineData("assets/../escape.json")]
    [InlineData("assets/CON.png")]
    [InlineData("assets/escape.png.")]
    [InlineData("assets/escape.png ")]
    [InlineData("assets/icon.png/child.json")]
    [InlineData("assets/ICON.png")]
    [InlineData("mod.json/child.json")]
    public async Task UnsafeOrCollidingZipPathsNeverPublishADestination(string name)
    {
        using var data = new TestMod(); await data.Create(); var entries = await Entries(await data.Pack()); entries.Add((name, "{}"u8.ToArray(), 0));
        var bad = data.PathFor("bad.zip"); Write(bad, entries); var output = data.PathFor("output");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModArchive().UnpackAsync(bad, output)); Assert.False(Directory.Exists(output));
        Assert.False(File.Exists(data.PathFor("escape.json"))); Assert.Empty(Directory.GetDirectories(data.Root, ".iryomera-unpack-*"));
    }
    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("link")]
    [InlineData("directory")]
    [InlineData("special")]
    [InlineData("reparse")]
    [InlineData("corrupt-index")]
    public async Task TamperingAndSpecialEntriesAreRejected(string change)
    {
        using var data = new TestMod(); await data.Create(); var entries = await Entries(await data.Pack());
        var item = entries.FindIndex(e => e.Name == "LICENSE.txt"); var original = entries[item];
        switch (change)
        {
            case "hash": entries[item] = original with { Bytes = Enumerable.Repeat((byte)'X', original.Bytes.Length).ToArray() }; break;
            case "size": entries[item] = original with { Bytes = [1, 2, 3] }; break;
            case "extra": entries.Add(("payload.dll", "MZ"u8.ToArray(), 0)); break;
            case "missing": entries.RemoveAt(item); break;
            case "link": entries[item] = original with { Attributes = unchecked((int)0xa1ff0000) }; break;
            case "directory": entries[item] = original with { Attributes = unchecked((int)0x41ed0000) }; break;
            case "special": entries[item] = original with { Attributes = unchecked((int)0x61ff0000) }; break;
            case "reparse": entries[item] = original with { Attributes = (int)FileAttributes.ReparsePoint }; break;
            default: var index = entries.FindIndex(e => e.Name == "checksums.json"); entries[index] = entries[index] with { Bytes = "{}"u8.ToArray() }; break;
        }
        var bad = data.PathFor("bad.zip"); Write(bad, entries);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModArchive().UnpackAsync(bad, data.PathFor("output"))); Assert.False(Directory.Exists(data.PathFor("output")));
    }
    [Theory]
    [InlineData("archive")]
    [InlineData("file")]
    [InlineData("total")]
    [InlineData("entries")]
    [InlineData("json")]
    [InlineData("ratio")]
    public async Task EveryConfiguredArchiveLimitIsEnforced(string which)
    {
        using var data = new TestMod(); await data.Create(); var zip = await data.Pack();
        var limits = which switch { "archive" => new() { MaxArchiveBytes = 20 }, "file" => new() { MaxFileBytes = 20 }, "total" => new() { MaxExpandedBytes = 20 }, "entries" => new() { MaxEntries = 2 }, "json" => new() { MaxJsonBytes = 10 }, _ => new PackageLimits { MaxCompressionRatio = 1 } };
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModArchive().UnpackAsync(zip, data.PathFor("output"), limits: limits));
        Assert.False(Directory.Exists(data.PathFor("output")));
    }
    [Fact]
    public async Task RenamingAnExecutableAsModelOrImageDoesNotPassAssetChecks()
    {
        using var data = new TestMod(); await data.Create();
        foreach (var path in new[] { "assets/cube.glb", "assets/icon.png" })
        {
            var original = await File.ReadAllBytesAsync(Path.Combine(data.Project, path)); await File.WriteAllBytesAsync(Path.Combine(data.Project, path), "MZfake executable file"u8.ToArray());
            await Assert.ThrowsAsync<InvalidDataException>(() => new ModArchive().PackAsync(data.Project, data.PathFor("output.zip")));
            Assert.False(File.Exists(data.PathFor("output.zip"))); await File.WriteAllBytesAsync(Path.Combine(data.Project, path), original);
        }
    }
    [Fact]
    public async Task ARehashedExternalGlbUriIsStillRejectedByContentValidation()
    {
        using var data = new TestMod(); await data.Create();
        var model = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":1,\"uri\":\"https://untrusted.example/code.bin\"}]}";
        var json = Encoding.UTF8.GetBytes(model.PadRight((model.Length + 3) / 4 * 4)); var bytes = new byte[20 + json.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46546c67); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)json.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4e4f534a); json.CopyTo(bytes, 20);
        var entries = await Entries(await data.Pack()); var modelIndex = entries.FindIndex(e => e.Name == "assets/cube.glb"); entries[modelIndex] = entries[modelIndex] with { Bytes = bytes };
        var indexIndex = entries.FindIndex(e => e.Name == "checksums.json"); var index = JsonSerializer.Deserialize<ModArchiveIndex>(entries[indexIndex].Bytes, ContentJson.Options)!;
        var digestIndex = index.Files.FindIndex(e => e.Path == "assets/cube.glb"); index.Files[digestIndex] = index.Files[digestIndex] with { SizeBytes = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
        entries[indexIndex] = entries[indexIndex] with { Bytes = JsonSerializer.SerializeToUtf8Bytes(index, ContentJson.Options) };
        var bad = data.PathFor("external.zip"); Write(bad, entries);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModArchive().UnpackAsync(bad, data.PathFor("output"))); Assert.False(Directory.Exists(data.PathFor("output")));
    }
    [Fact]
    public async Task SourceSymlinksAndLinkedDestinationParentsAreRejectedOnLinux()
    {
        if (OperatingSystem.IsWindows()) return; // Windows ZIP link/reparse attacks are tested above without symlink privileges.
        using var data = new TestMod(); await data.Create(); var original = Path.Combine(data.Project, "LICENSE.txt"); var external = data.PathFor("external.txt"); File.Move(original, external); File.CreateSymbolicLink(original, external);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModArchive().PackAsync(data.Project, data.PathFor("bad.zip")));
        File.Delete(original); File.Move(external, original); var zip = await data.Pack();
        var outside = data.PathFor("outside"); Directory.CreateDirectory(outside); Directory.CreateSymbolicLink(data.PathFor("linked"), outside);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModArchive().UnpackAsync(zip, data.PathFor("linked/new"))); Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }
    [Fact]
    public async Task CancellationLeavesNoPublishedDirectoryOrArchive()
    {
        using var data = new TestMod(); await data.Create(); var zip = await data.Pack(); var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModArchive().UnpackAsync(zip, data.PathFor("output"), ct: token)); Assert.False(Directory.Exists(data.PathFor("output")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModArchive().PackAsync(data.Project, data.PathFor("canceled.zip"), ct: token)); Assert.False(File.Exists(data.PathFor("canceled.zip")));
    }
}
