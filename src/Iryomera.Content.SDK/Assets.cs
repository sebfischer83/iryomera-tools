namespace Iryomera.Content.SDK;

public enum AssetKind { ModelGlb, Texture, Icon }

/// <summary>A package-relative data asset. Licensing is independent of the tool's MIT license.</summary>
public sealed record AssetReference : ExtensibleRecord
{
    public required string Path { get; init; }
    public required AssetKind Kind { get; init; }
    public required string License { get; init; }
    public string? Attribution { get; init; }
}
public sealed record FootprintCell : ExtensibleRecord
{
    public required int X { get; init; }
    public required int Z { get; init; }
}
/// <summary>Grid cells in the local positive X/Z rectangle. Empty occupancy means the whole rectangle.</summary>
public sealed record FurnitureFootprint : ExtensibleRecord
{
    public required int Width { get; init; }
    public required int Depth { get; init; }
    public decimal CellSizeMeters { get; init; } = 1;
    public List<FootprintCell> OccupiedCells { get; init; } = [];
}
/// <summary>Local meters relative to the footprint's lower X/Z corner, Y up. Points may lie outside occupied cells.</summary>
public sealed record InteractionPoint : ExtensibleRecord
{
    public required string Key { get; init; }
    public required decimal X { get; init; }
    public required decimal Y { get; init; }
    public required decimal Z { get; init; }
    public decimal? HeadingDegrees { get; init; }
    public string? RequiredQualificationId { get; init; }
}
public sealed record FurniturePlacement : ExtensibleRecord
{
    public required FurnitureFootprint Footprint { get; init; }
    public List<InteractionPoint> InteractionPoints { get; init; } = [];
}

internal static class AssetValidator
{
    internal static IReadOnlyList<ValidationIssue> Validate(ContentPackage package, Func<string, ContentDefinition?> resolve)
    {
        var issues = new List<ValidationIssue>();
        foreach (var definition in package.Definitions)
        {
            var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in definition.Assets ?? [])
            {
                if (!Enum.IsDefined(asset.Kind) || string.IsNullOrWhiteSpace(asset.License) || !SafePath(asset.Path))
                    issues.Add(new("asset", definition.Id, "Use a known asset kind, original license and safe relative path."));
                if (!assets.Add(asset.Path)) issues.Add(new("duplicate-asset", definition.Id, "Asset paths must be unique within a definition."));
            }
            if (definition is not FurnitureDefinition { Placement: { } placement }) continue;
            var footprint = placement.Footprint;
            if (footprint.Width <= 0 || footprint.Depth <= 0 || footprint.CellSizeMeters <= 0)
                issues.Add(new("footprint", definition.Id, "Footprint dimensions and cell size must be positive."));
            var cells = new HashSet<(int, int)>();
            foreach (var cell in footprint.OccupiedCells)
                if (cell.X < 0 || cell.Z < 0 || cell.X >= footprint.Width || cell.Z >= footprint.Depth || !cells.Add((cell.X, cell.Z)))
                    issues.Add(new("footprint-cell", definition.Id, "Occupied cells must be unique and inside the footprint."));
            var points = new HashSet<string>(StringComparer.Ordinal);
            foreach (var point in placement.InteractionPoints)
            {
                if (!ContentFormat.IsValidNamespace(point.Key) || !points.Add(point.Key) || point.HeadingDegrees is < 0 or >= 360)
                    issues.Add(new("interaction-point", definition.Id, "Use unique stable point keys and headings in [0,360)."));
                if (point.RequiredQualificationId is { } id && resolve(id) is { } target && target is not StaffQualificationDefinition)
                    issues.Add(new("interaction-qualification", definition.Id, "An interaction point requires a staff qualification reference."));
            }
        }
        return issues;
    }

    private static bool SafePath(string path) => !string.IsNullOrWhiteSpace(path) && !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':') &&
        !path.Any(char.IsControl) && !path.Split('/').Any(s => s is "" or "." or "..");
}
