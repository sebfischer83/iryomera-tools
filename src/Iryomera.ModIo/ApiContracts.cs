using System.Text.Json;
using System.Text.Json.Serialization;

namespace Iryomera.ModIo;

/// <summary>Upstream REST v1 page; extension fields preserve additive upstream metadata.</summary>
public sealed class ModIoPage<T>
{
    public required List<T> Data { get; init; }
    public required int ResultCount { get; init; }
    public required int ResultOffset { get; init; }
    public required int ResultLimit { get; init; }
    public required long ResultTotal { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
}
public sealed class ModIoMod
{
    public required long Id { get; init; }
    public required long GameId { get; init; }
    public required string Name { get; init; }
    public string? Summary { get; init; }
    public long DateUpdated { get; init; }
    public ModIoFile? Modfile { get; init; }
    public bool Dependencies { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
    public override string ToString() => $"mod.io mod {Id}";
}
public sealed class ModIoFile
{
    public required long Id { get; init; }
    public required long ModId { get; init; }
    public required long Filesize { get; init; }
    public required ModIoFileHash Filehash { get; init; }
    public string? Version { get; init; }
    public string? Filename { get; init; }
    public ModIoDownload? Download { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
    public override string ToString() => $"mod.io file {Id}";
}
public sealed class ModIoFileHash { public required string Md5 { get; init; } }
public sealed class ModIoDownload
{
    public required Uri BinaryUrl { get; init; }
    public long? DateExpires { get; init; }
    public override string ToString() => "mod.io download [transient URL redacted]";
}
public sealed record ModSearch(string? Text = null, int Limit = 100, int Offset = 0);
public sealed record NewMod(string Name, string Summary, string Description, string LogoFilename, IReadOnlyList<string>? Tags = null);
public sealed record ModUpdate(long ModId, long InstalledFileId, long AvailableFileId, string? AvailableVersion)
{
    public bool IsAvailable => InstalledFileId != AvailableFileId;
}

public interface IModIoClient
{
    Task RequestEmailCodeAsync(string email, CancellationToken ct = default);
    Task<ModIoAccessToken> ExchangeEmailCodeAsync(string securityCode, CancellationToken ct = default);
    Task<ModIoPage<ModIoMod>> SearchAsync(ModSearch search, CancellationToken ct = default);
    Task<ModIoMod> GetModAsync(long modId, CancellationToken ct = default);
    Task<ModIoFile> GetFileAsync(long modId, long fileId, CancellationToken ct = default);
    Task SubscribeAsync(long modId, bool includeDependencies = false, CancellationToken ct = default);
    Task UnsubscribeAsync(long modId, CancellationToken ct = default);
    Task<ModIoPage<ModIoMod>> GetDependenciesAsync(long modId, int offset = 0, CancellationToken ct = default);
    Task<ModIoMod> PublishAsync(NewMod mod, Stream logo, CancellationToken ct = default);
    Task<ModIoFile> UploadAsync(long modId, Stream archive, string version, string? changelog = null, CancellationToken ct = default);
}
