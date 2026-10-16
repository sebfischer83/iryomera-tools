using System.Text.Json.Serialization;
using Iryomera.Content.SDK;

namespace Iryomera.ModKit;

public enum ModFileKind { Content, Localization, ModelGlb, Texture, Icon, Notice }
public sealed record ModFile : ExtensibleRecord
{
    public required string Path { get; init; }
    public required ModFileKind Kind { get; init; }
    public required string License { get; init; }
    public string? Attribution { get; init; }
}
public sealed record ModProject : ExtensibleRecord
{
    [JsonRequired] public string SchemaVersion { get; init; } = "1.0.0";
    public required ModManifest Manifest { get; init; }
    public required List<ModFile> Files { get; init; }
}
public sealed record ModFileDigest : ExtensibleRecord
{
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
}
public sealed record ModArchiveIndex : ExtensibleRecord
{
    [JsonRequired] public string SchemaVersion { get; init; } = "1.0.0";
    public required List<ModFileDigest> Files { get; init; }
}
public sealed record PackageLimits
{
    public long MaxArchiveBytes { get; init; } = 128L * 1024 * 1024;
    public long MaxFileBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxExpandedBytes { get; init; } = 256L * 1024 * 1024;
    public int MaxEntries { get; init; } = 2048;
    public long MaxJsonBytes { get; init; } = 8L * 1024 * 1024;
    public decimal MaxCompressionRatio { get; init; } = 200;
    internal void EnsureValid()
    {
        if (MaxArchiveBytes <= 0 || MaxFileBytes <= 0 || MaxExpandedBytes <= 0 || MaxJsonBytes <= 0 || MaxEntries <= 1 || MaxCompressionRatio < 1)
            throw new ArgumentOutOfRangeException(nameof(PackageLimits), "All limits must be positive; at least two entries and ratio >= 1 are required.");
    }
}
public sealed record ModValidationResult(ModProject Project, ContentPackage Content, IReadOnlyList<LocalizationDefinition> Catalogs, IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
