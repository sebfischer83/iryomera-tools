using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Iryomera.Content.SDK;

namespace Iryomera.ModIo;

public sealed record ModOrigin : ExtensibleRecord
{
    public required string Provider { get; init; }
    public long? GameId { get; init; }
    public long? ModId { get; init; }
    public long? FileId { get; init; }
}
/// <summary>Safe host-owned exchange receipt. Contains neither secrets nor download URLs; keep outside the mod inventory.</summary>
public sealed record ModInstallReceipt : ExtensibleRecord
{
    [JsonRequired] public string SchemaVersion { get; init; } = "1.0.0";
    public required string PackageId { get; init; }
    public required string Namespace { get; init; }
    public required string Version { get; init; }
    public required string ArchiveSha256 { get; init; }
    public required ModOrigin Origin { get; init; }
}
public sealed record InstalledMod(string Directory, ContentPackage Content, ModInstallReceipt Receipt);
public interface IModSource
{
    Task<VerifiedArchive> FetchAsync(string sourceId, string output, CancellationToken ct = default);
}
public interface IModInstaller
{
    Task<InstalledMod> InstallAsync(VerifiedArchive archive, string destination, IEnumerable<ContentPackage>? dependencies = null, string? gameVersion = null, CancellationToken ct = default);
}
/// <summary>Explicit delivery/installation orchestration only. Does not activate mods, execute scripts or resolve medical facts.</summary>
public sealed class ModRuntime(IModInstaller installer)
{
    public async Task<InstalledMod> InstallAsync(IModSource source, string sourceId, string destination, IEnumerable<ContentPackage>? dependencies = null, string? gameVersion = null, CancellationToken ct = default)
    {
        var temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "iryomera-delivery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            ct.ThrowIfCancellationRequested();
            var archive = await source.FetchAsync(sourceId, System.IO.Path.Combine(temporary, "mod.zip"), ct);
            return await installer.InstallAsync(archive, destination, dependencies, gameVersion, ct);
        }
        finally { Directory.Delete(temporary, true); }
    }
}
/// <summary>Explicit ID-to-archive mapping; independent of mod.io configuration, discovery or network.</summary>
public sealed class LocalArchiveSource : IModSource
{
    private readonly IReadOnlyDictionary<string, string> archives;
    private readonly long maximum;
    public LocalArchiveSource(IReadOnlyDictionary<string, string> archives, long maxBytes = 128L * 1024 * 1024)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        this.archives = archives.ToDictionary(x => x.Key, x => System.IO.Path.GetFullPath(x.Value), StringComparer.Ordinal); maximum = maxBytes;
    }
    public async Task<VerifiedArchive> FetchAsync(string sourceId, string output, CancellationToken ct = default)
    {
        if (!archives.TryGetValue(sourceId, out var inputPath)) throw new KeyNotFoundException("Unknown local mod identifier.");
        output = System.IO.Path.GetFullPath(output); SafeHttp.NewTarget(output); var temporary = output + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            long count;
            await using (var input = File.OpenRead(inputPath))
            await using (var captured = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                count = await SafeHttp.CopyAsync(input, captured, maximum, ct);
            await using var read = File.OpenRead(temporary); var sha = Convert.ToHexString(await SHA256.HashDataAsync(read, ct)).ToLowerInvariant();
            await read.DisposeAsync(); ct.ThrowIfCancellationRequested(); File.Move(temporary, output);
            return new(output, sha, count, new() { Provider = "local" });
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
public sealed class ModIoSource(IModIoClient api, ModDownloadManager downloads) : IModSource
{
    public async Task<VerifiedArchive> FetchAsync(string sourceId, string output, CancellationToken ct = default)
    {
        if (!long.TryParse(sourceId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var modId) || modId <= 0)
            throw new ArgumentException("A positive numeric mod.io source ID is required.");
        var mod = await api.GetModAsync(modId, ct);
        if (mod.Modfile is null) throw new InvalidDataException("Mod has no current downloadable file.");
        var file = await api.GetFileAsync(modId, mod.Modfile.Id, ct); return await downloads.DownloadAsync(file, output, ct: ct);
    }
}
