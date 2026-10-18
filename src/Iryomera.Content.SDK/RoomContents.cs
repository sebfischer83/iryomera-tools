namespace Iryomera.Content.SDK;

/// <summary>An item a room type offers, aggregated over every relation that links it.</summary>
public sealed record PlaceableItem(string DefinitionId, RelationKind Kind, RelationRequirement Requirement, IReadOnlyList<string> RelationIds);

/// <summary>
/// What a room type offers to place, from roomFurniture and roomEquipment relations (ADR 0014). Items without such a
/// relation are not offered. Call on a validated package set; placement itself is game logic.
/// </summary>
public static class RoomContents
{
    /// <summary>
    /// The requirement with its kind-specific default: roomFurniture without a value is required (its meaning before
    /// 0.10.0), roomEquipment without a value is optional.
    /// </summary>
    public static RelationRequirement EffectiveRequirement(ContentRelation relation) => relation.Kind switch
    {
        RelationKind.RoomFurniture => relation.Requirement ?? RelationRequirement.Required,
        RelationKind.RoomEquipment => relation.Requirement ?? RelationRequirement.Optional,
        _ => throw new ArgumentException("Only roomFurniture and roomEquipment relations describe room contents.", nameof(relation))
    };

    /// <summary>
    /// Items offered in the room type, ordered by definition ID (ordinal) independent of package order. An item linked
    /// by several relations (for example base game and a mod) is listed once; it is required if any link requires it.
    /// </summary>
    public static IReadOnlyList<PlaceableItem> Placeable(IEnumerable<ContentPackage> packages, string roomId) =>
        packages.SelectMany(p => p.Relations)
            .Where(r => r.FromId == roomId && r.Kind is RelationKind.RoomFurniture or RelationKind.RoomEquipment)
            .GroupBy(r => (r.ToId, r.Kind))
            .Select(g => new PlaceableItem(g.Key.ToId, g.Key.Kind,
                g.Any(r => EffectiveRequirement(r) == RelationRequirement.Required) ? RelationRequirement.Required : RelationRequirement.Optional,
                g.Select(r => r.Id).Order(StringComparer.Ordinal).ToArray()))
            .OrderBy(i => i.DefinitionId, StringComparer.Ordinal).ThenBy(i => i.Kind)
            .ToArray();

    /// <summary>
    /// Equipment capabilities the given placed furniture provides through furnitureEquipment relations (ADR 0018), ordered
    /// by ID (ordinal) and listed once. Whether a room then satisfies examinationEquipment is game logic.
    /// </summary>
    public static IReadOnlyList<string> ProvidedEquipment(IEnumerable<ContentPackage> packages, IEnumerable<string> furnitureIds)
    {
        var placed = furnitureIds.ToHashSet(StringComparer.Ordinal);
        return packages.SelectMany(p => p.Relations)
            .Where(r => r.Kind == RelationKind.FurnitureEquipment && placed.Contains(r.FromId))
            .Select(r => r.ToId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}

/// <summary>Which items may stand on which surface (ADR 0013). Whether an item fits, stacking and clearance are game logic.</summary>
public static class FurnitureSurfaces
{
    /// <summary>
    /// True when the item is surface-mounted and the surface accepts it: a surface without accepted categories takes any
    /// item, otherwise the item must share at least one category. An item without categories only fits unrestricted surfaces.
    /// </summary>
    public static bool Accepts(FurnitureSurface surface, FurniturePlacement item) =>
        item.Mount == FurnitureMount.Surface &&
        (surface.Accepts is not { Count: > 0 } accepts || (item.SurfaceCategories ?? []).Any(c => accepts.Contains(c, StringComparer.Ordinal)));
}
