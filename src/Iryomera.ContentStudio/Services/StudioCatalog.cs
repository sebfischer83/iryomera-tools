using System.Text.Json;
using Iryomera.Content.SDK;
using Json.Schema;

namespace Iryomera.ContentStudio.Services;

public sealed record CatalogAssetIndexEntry(string Id, AssetKind Kind, string Category, FurnitureMount? Mount, int? FootprintWidth, int? FootprintDepth,
    FurnitureMobility? Mobility = null, IReadOnlyList<string>? InteractionPointKinds = null);

/// <summary>
/// Optional local, unversioned folder written by the game's private asset plugin (Studio:CatalogPreviewDirectory):
/// catalog-assets.json for the selection list and &lt;namespace&gt;/&lt;local_name&gt;.png previews. Read-only; nothing from
/// it is stored in the database, audit, exports or packages. Purchased assets never enter this repository (ADR 0010/0013).
/// </summary>
public sealed class StudioCatalog(IConfiguration configuration)
{
    public const long MaxPreviewBytes = 2 * 1024 * 1024;
    public const long MaxIndexBytes = 8 * 1024 * 1024;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly Lazy<JsonSchema> Bundle = new(() =>
    {
        using var resource = typeof(StudioCatalog).Assembly.GetManifestResourceStream("Iryomera.ContentStudio.Schemas.studio.v1.json")
            ?? throw new InvalidOperationException("Missing Studio schemas.");
        using var reader = new StreamReader(resource);
        return JsonSchema.FromText(reader.ReadToEnd());
    });
    private readonly object _gate = new();
    private (DateTime Stamp, IReadOnlyList<CatalogAssetIndexEntry> Entries, string? Error)? _cache;

    public string? Directory => configuration["Studio:CatalogPreviewDirectory"] is { Length: > 0 } path ? Path.GetFullPath(path) : null;
    public bool Configured => Directory is { } d && System.IO.Directory.Exists(d);

    /// <summary>Entries of catalog-assets.json, or an error message when the file is invalid. Empty when not configured.</summary>
    public (IReadOnlyList<CatalogAssetIndexEntry> Entries, string? Error) Index()
    {
        if (!Configured) return ([], null);
        var file = Path.Combine(Directory!, "catalog-assets.json");
        if (!File.Exists(file)) return ([], null);
        var info = new FileInfo(file);
        lock (_gate)
        {
            if (_cache is { } cached && cached.Stamp == info.LastWriteTimeUtc) return (cached.Entries, cached.Error);
            var result = Load(info);
            _cache = (info.LastWriteTimeUtc, result.Entries, result.Error);
            return result;
        }
    }

    internal static (IReadOnlyList<CatalogAssetIndexEntry> Entries, string? Error) Load(FileInfo info)
    {
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length > MaxIndexBytes) return ([], "catalog-assets.json is a link or exceeds the size limit.");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(info.FullName), new JsonDocumentOptions { AllowDuplicateProperties = false });
            return Parse(document.RootElement);
        }
        catch (JsonException e) { return ([], "catalog-assets.json is not valid JSON: " + e.Message); }
    }

    /// <summary>Schema validation on the original JSON first; unknown fields are tolerated.</summary>
    public static (IReadOnlyList<CatalogAssetIndexEntry> Entries, string? Error) Parse(JsonElement root)
    {
        var options = new EvaluationOptions { RequireFormatValidation = true };
        options.SchemaRegistry.Register(Bundle.Value);
        var schema = JsonSchema.FromText("{\"$ref\":\"https://schemas.iryomera.org/studio/v1/contracts.schema.json#/$defs/catalogAssetIndex\"}");
        if (!schema.Evaluate(root, options).IsValid) return ([], "catalog-assets.json does not match the catalogAssetIndex schema v1.");
        var entries = new List<CatalogAssetIndexEntry>();
        var ids = new HashSet<(string, AssetKind)>();
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var kind = JsonSerializer.Deserialize<AssetKind>(asset.GetProperty("kind").GetRawText(), ContentJson.Options);
            var id = asset.GetProperty("id").GetString()!;
            if (!ids.Add((id, kind))) return ([], $"catalog-assets.json lists {id} more than once.");
            FurnitureMount? mount = asset.TryGetProperty("mount", out var m) && m.ValueKind == JsonValueKind.String ? JsonSerializer.Deserialize<FurnitureMount>(m.GetRawText(), ContentJson.Options) : null;
            int? width = null, depth = null;
            if (asset.TryGetProperty("footprint", out var f) && f.ValueKind == JsonValueKind.Object) { width = f.GetProperty("width").GetInt32(); depth = f.GetProperty("depth").GetInt32(); }
            FurnitureMobility? mobility = asset.TryGetProperty("mobility", out var mo) && mo.ValueKind == JsonValueKind.String ? JsonSerializer.Deserialize<FurnitureMobility>(mo.GetRawText(), ContentJson.Options) : null;
            IReadOnlyList<string>? kinds = asset.TryGetProperty("interactionPointKinds", out var k) && k.ValueKind == JsonValueKind.Array ? k.EnumerateArray().Select(x => x.GetString()!).ToArray() : null;
            entries.Add(new(id, kind, asset.GetProperty("category").GetString()!, mount, width, depth, mobility, kinds));
        }
        return (entries.OrderBy(e => e.Id, StringComparer.Ordinal).ThenBy(e => e.Kind).ToArray(), null);
    }

    /// <summary>
    /// Reads &lt;directory&gt;/&lt;namespace&gt;/&lt;local_name&gt;.png for a valid catalog ID only. The path is built from the
    /// validated ID, must stay inside the directory, and links, oversized files and non-PNG content are refused.
    /// </summary>
    public async Task<byte[]?> PreviewAsync(string catalogId, CancellationToken ct = default)
    {
        if (!Configured || !ContentFormat.IsValidId(catalogId)) return null;
        var root = Directory!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var separator = catalogId.IndexOf(':');
        var path = Path.GetFullPath(Path.Combine(root, catalogId[..separator], catalogId[(separator + 1)..] + ".png"));
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return null;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxPreviewBytes || info.Length < PngSignature.Length) return null;
        for (var current = info.Directory; current is not null && current.FullName.Length >= root.Length - 1; current = current.Parent)
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
        var bytes = await File.ReadAllBytesAsync(path, ct);
        return bytes.Length <= MaxPreviewBytes && bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature) ? bytes : null;
    }
}
