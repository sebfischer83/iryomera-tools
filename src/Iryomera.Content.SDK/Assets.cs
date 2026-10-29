namespace Iryomera.Content.SDK;

public enum AssetKind { ModelGlb, Texture, Icon }
public enum FurnitureMount { Floor, Wall, Ceiling, Surface }
/// <summary>
/// Movement during play, not the player rearranging items (the game allows that for every item). Fixed stays where it was
/// placed; Movable is carried by staff to where it is needed, without a patient on it; Pushable is pushed by one person with
/// a patient on it and is the only mobility that allows transport.
/// </summary>
public enum FurnitureMobility { Fixed, Movable, Pushable }
public enum TransportPosture { Seated, Lying }

/// <summary>Standard interaction point kinds (ADR 0016). Other stable keys are allowed for mods.</summary>
public static class InteractionPointKinds
{
    public const string Seat = "seat", Lying = "lying", Handle = "handle", Operator = "operator", Entry = "entry";
}

/// <summary>Simulation and game rules for furniture that carries patients; no medical statement.</summary>
public sealed record FurnitureTransport : ExtensibleRecord
{
    public required int PatientCapacity { get; init; }
    public required TransportPosture Posture { get; init; }
    public string? RequiredQualificationId { get; init; }
}

/// <summary>A package-relative data asset. Licensing is independent of the tool's MIT license.</summary>
public sealed record AssetReference : ExtensibleRecord
{
    public required string Path { get; init; }
    public required AssetKind Kind { get; init; }
    public required string License { get; init; }
    public string? Attribution { get; init; }
}
/// <summary>
/// An opaque asset ID resolved by the game's private catalog, for assets that are never shipped in a package.
/// Has no path, license or inventory entry; the SDK validates its format only and never resolves it. See ADR 0013.
/// </summary>
public sealed record CatalogAssetReference : ExtensibleRecord
{
    public required string CatalogId { get; init; }
    public required AssetKind Kind { get; init; }
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
/// <summary>
/// Local meters relative to the footprint's lower X/Z corner, Y up. Points may lie outside occupied cells. HeadingDegrees is
/// the facing of the person at the point: 0° looks along local +Z, increasing counterclockwise seen from above, so 90° looks
/// along +X, 180° along -Z and 270° along -X (a right-handed rotation about +Y). For a lying point it is the direction from
/// feet to head. Kind is optional: seat, lying, handle,
/// operator, entry or another stable key.
/// </summary>
public sealed record InteractionPoint : ExtensibleRecord
{
    public required string Key { get; init; }
    public string? Kind { get; init; }
    public required decimal X { get; init; }
    public required decimal Y { get; init; }
    public required decimal Z { get; init; }
    public decimal? HeadingDegrees { get; init; }
    public string? RequiredQualificationId { get; init; }
}
/// <summary>A horizontal area other items can stand on; local meters from the footprint's lower X/Z corner, Y up.</summary>
public sealed record FurnitureSurface : ExtensibleRecord
{
    public required string Key { get; init; }
    public required decimal HeightMeters { get; init; }
    public required decimal X { get; init; }
    public required decimal Z { get; init; }
    public required decimal Width { get; init; }
    public required decimal Depth { get; init; }
    /// <summary>Optional, omitted when null: categories of surface-mounted items this surface takes; empty takes any item that fits.</summary>
    public List<string>? Accepts { get; init; }
}
/// <summary>
/// Absent mount means floor. Wall: X along the wall, Z into the room (+Z), origin on the wall surface, height from the storey
/// floor to the origin. Ceiling: origin at the ceiling, the model hangs towards -Y. Surface: origin on another item's surface plane, the model stands
/// towards +Y; the height comes from that surface. Placement legality (fit, stacking, clearance) is game logic.
/// </summary>
public sealed record FurniturePlacement : ExtensibleRecord
{
    public required FurnitureFootprint Footprint { get; init; }
    public List<InteractionPoint> InteractionPoints { get; init; } = [];
    public FurnitureMount? Mount { get; init; }
    public decimal? MountHeightMeters { get; init; }
    /// <summary>
    /// Optional, omitted when null: how tall the item is in metres, from its base up (a floor item's top above the floor). The
    /// game uses it for layout rules, for example no wall decor above a tall cabinet. Must be positive; any mount.
    /// </summary>
    public decimal? HeightMeters { get; init; }
    /// <summary>Optional, omitted when null; surfaces lie inside the footprint rectangle, which covers the whole projection.</summary>
    public List<FurnitureSurface>? Surfaces { get; init; }
    /// <summary>Optional, omitted when null; only with mount surface. Matched against FurnitureSurface.Accepts.</summary>
    public List<string>? SurfaceCategories { get; init; }
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
            var catalog = new HashSet<(string, AssetKind)>();
            foreach (var asset in definition.CatalogAssets ?? [])
            {
                if (!ContentFormat.IsValidId(asset.CatalogId) || !Enum.IsDefined(asset.Kind))
                    issues.Add(new("catalog-asset", definition.Id, "Use a namespace-based catalog ID and a known asset kind."));
                if (!catalog.Add((asset.CatalogId, asset.Kind))) issues.Add(new("duplicate-catalog-asset", definition.Id, "Catalog asset references must be unique within a definition."));
            }
            if (definition is FurnitureDefinition furniture) ValidateMobility(furniture, resolve, issues);
            if (definition is not FurnitureDefinition { Placement: { } placement }) continue;
            if (placement.Mount is { } mount && !Enum.IsDefined(mount))
                issues.Add(new("furniture-mount", definition.Id, "Unknown furniture mount."));
            if (placement.MountHeightMeters is { } height && (placement.Mount != FurnitureMount.Wall || height < 0))
                issues.Add(new("furniture-mount", definition.Id, "A mount height requires a wall mount and must be nonnegative."));
            if (placement.HeightMeters is { } itemHeight && itemHeight <= 0)
                issues.Add(new("furniture-height", definition.Id, "An item height must be positive."));
            var footprint = placement.Footprint;
            var surfaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (var surface in placement.Surfaces ?? [])
                if (!ContentFormat.IsValidNamespace(surface.Key) || !surfaces.Add(surface.Key) || surface.HeightMeters <= 0 || surface.Width <= 0 || surface.Depth <= 0 ||
                    surface.X < 0 || surface.Z < 0 || surface.X + surface.Width > footprint.Width * footprint.CellSizeMeters || surface.Z + surface.Depth > footprint.Depth * footprint.CellSizeMeters)
                    issues.Add(new("furniture-surface", definition.Id, "Surfaces need unique stable keys, positive height and size, and must lie inside the footprint rectangle."));
            foreach (var categories in (placement.Surfaces ?? []).Select(s => s.Accepts).Append(placement.SurfaceCategories))
                if (categories is not null && (categories.Any(c => !ContentFormat.IsValidNamespace(c)) || categories.Distinct(StringComparer.Ordinal).Count() != categories.Count))
                    issues.Add(new("furniture-surface-category", definition.Id, "Surface categories must be unique stable keys."));
            if (placement.SurfaceCategories is not null && placement.Mount != FurnitureMount.Surface)
                issues.Add(new("furniture-surface-category", definition.Id, "Surface categories are allowed only with mount surface."));
            if (footprint.Width <= 0 || footprint.Depth <= 0 || footprint.CellSizeMeters <= 0)
                issues.Add(new("footprint", definition.Id, "Footprint dimensions and cell size must be positive."));
            var cells = new HashSet<(int, int)>();
            foreach (var cell in footprint.OccupiedCells)
                if (cell.X < 0 || cell.Z < 0 || cell.X >= footprint.Width || cell.Z >= footprint.Depth || !cells.Add((cell.X, cell.Z)))
                    issues.Add(new("footprint-cell", definition.Id, "Occupied cells must be unique and inside the footprint."));
            var points = new HashSet<string>(StringComparer.Ordinal);
            foreach (var point in placement.InteractionPoints)
            {
                if (!ContentFormat.IsValidNamespace(point.Key) || !points.Add(point.Key) || point.HeadingDegrees is < 0 or >= 360 || point.Kind is { } kind && !ContentFormat.IsValidNamespace(kind))
                    issues.Add(new("interaction-point", definition.Id, "Use unique stable point keys, stable kind keys and headings in [0,360)."));
                if (point.RequiredQualificationId is { } id && resolve(id) is { } target && target is not StaffQualificationDefinition)
                    issues.Add(new("interaction-qualification", definition.Id, "An interaction point requires a staff qualification reference."));
            }
        }
        return issues;
    }

    /// <summary>Transport needs a pushable item with enough seat or lying points; pushable items need a handle.</summary>
    private static void ValidateMobility(FurnitureDefinition furniture, Func<string, ContentDefinition?> resolve, List<ValidationIssue> issues)
    {
        void Error(string message) => issues.Add(new("furniture-transport", furniture.Id, message));
        if (furniture.Mobility is { } mobility && !Enum.IsDefined(mobility)) Error("Unknown furniture mobility.");
        var points = furniture.Placement?.InteractionPoints ?? [];
        int Count(string kind) => points.Count(p => p.Kind == kind);
        if (furniture.Mobility == FurnitureMobility.Pushable && Count(InteractionPointKinds.Handle) == 0) Error("A pushable item needs at least one handle interaction point.");
        if (furniture.Transport is not { } transport) return;
        if (furniture.Mobility != FurnitureMobility.Pushable) Error("Transport requires mobility pushable; movable items are carried by staff without a patient.");
        if (transport.PatientCapacity < 1) Error("Patient capacity must be at least 1.");
        if (!Enum.IsDefined(transport.Posture)) Error("Unknown transport posture.");
        var required = transport.Posture == TransportPosture.Lying ? InteractionPointKinds.Lying : InteractionPointKinds.Seat;
        if (Count(required) < transport.PatientCapacity) Error($"Transport needs at least {transport.PatientCapacity} {required} interaction point(s).");
        if (transport.RequiredQualificationId is { } id && resolve(id) is { } target && target is not StaffQualificationDefinition)
            issues.Add(new("interaction-qualification", furniture.Id, "Transport staff requires a staff qualification reference."));
    }

    private static bool SafePath(string path) => !string.IsNullOrWhiteSpace(path) && !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':') &&
        !path.Any(char.IsControl) && !path.Split('/').Any(s => s is "" or "." or "..");
}
