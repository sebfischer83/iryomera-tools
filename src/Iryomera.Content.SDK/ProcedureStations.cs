namespace Iryomera.Content.SDK;

/// <summary>
/// Where a person is during one station (ADR 0025): at an interaction point of that kind on an item in the room, optionally
/// only on an item providing the equipment (furnitureEquipment). <see cref="ProcedureStations.Standing"/> binds to no item.
/// Presentation only; no medical statement.
/// </summary>
public sealed record StationPlace : ExtensibleRecord
{
    public required string PointKind { get; init; }
    /// <summary>Optional, omitted when null; a medicalEquipment definition. Not allowed with <see cref="ProcedureStations.Standing"/>.</summary>
    public string? EquipmentId { get; init; }
}

/// <summary>One section of an examination or treatment: where the patient and the staff member are, and its share of the duration.</summary>
public sealed record ProcedureStation : ExtensibleRecord
{
    public required string Key { get; init; }
    /// <summary>Above 0; the shares of a set sum to 1.</summary>
    public required decimal Share { get; init; }
    public required StationPlace Patient { get; init; }
    /// <summary>Optional, omitted when null; the staff member then stands next to the patient.</summary>
    public StationPlace? Staff { get; init; }
}

/// <summary>The stations of one examination or treatment, in order (ADR 0025).</summary>
public sealed record ProcedureStationSet : ExtensibleRecord
{
    public required string DefinitionId { get; init; }
    public List<ProcedureStation> Stations { get; init; } = [];
}

/// <summary>A station place no room type of its examination or treatment can offer; a likely data mistake, not an error.</summary>
public sealed record UnplaceableStation(string DefinitionId, string StationKey, string Role, string PointKind, string? EquipmentId);

/// <summary>
/// Stations of examinations and treatments (ADR 0025). Call on a validated package set; walking, timing and drawing the
/// people at their places is game logic.
/// </summary>
public static class ProcedureStations
{
    /// <summary>A place that binds to no item: the person stands in the room.</summary>
    public const string Standing = "standing";

    /// <summary>Tolerance of the sum of shares.</summary>
    public const decimal ShareTolerance = 0.001m;

    /// <summary>
    /// The stations of the definition, or null when it has none. A later package in the given order (for example a mod)
    /// replaces the whole set of an earlier one.
    /// </summary>
    public static IReadOnlyList<ProcedureStation>? For(IEnumerable<ContentPackage> packages, string definitionId) =>
        packages.SelectMany(p => p.ProcedureStations ?? []).LastOrDefault(s => s.DefinitionId == definitionId)?.Stations;

    /// <summary>
    /// Places of effective station sets that no room type of the examination or treatment (examinationRoom, treatmentRoom)
    /// can offer: no item the room type offers (roomFurniture, roomEquipment) has a point of that kind and, if named, provides
    /// the equipment. Definitions without room types and standing places are skipped. Ordered by definition ID and station order.
    /// </summary>
    public static IReadOnlyList<UnplaceableStation> Unplaceable(IReadOnlyList<ContentPackage> packages)
    {
        var furniture = packages.SelectMany(p => p.Definitions).OfType<FurnitureDefinition>()
            .GroupBy(f => f.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var result = new List<UnplaceableStation>();
        var sets = packages.SelectMany(p => p.ProcedureStations ?? []).GroupBy(s => s.DefinitionId, StringComparer.Ordinal)
            .Select(g => g.Last()).OrderBy(s => s.DefinitionId, StringComparer.Ordinal);
        foreach (var set in sets)
        {
            var rooms = packages.SelectMany(p => p.Relations)
                .Where(r => r.FromId == set.DefinitionId && r.Kind is RelationKind.ExaminationRoom or RelationKind.TreatmentRoom)
                .Select(r => r.ToId).Distinct(StringComparer.Ordinal).ToArray();
            if (rooms.Length == 0) continue;
            var items = rooms.Select(room => RoomContents.Placeable(packages, room)
                .Select(i => furniture.GetValueOrDefault(i.DefinitionId)).OfType<FurnitureDefinition>().ToArray()).ToArray();
            foreach (var station in set.Stations)
                foreach (var (role, place) in new[] { ("patient", station.Patient), ("staff", station.Staff) })
                    if (place is not null && place.PointKind != Standing && !items.Any(offered => offered.Any(item => Offers(packages, item, place))))
                        result.Add(new(set.DefinitionId, station.Key, role, place.PointKind, place.EquipmentId));
        }
        return result;
    }

    private static bool Offers(IReadOnlyList<ContentPackage> packages, FurnitureDefinition item, StationPlace place) =>
        (item.Placement?.InteractionPoints ?? []).Any(p => p.Kind == place.PointKind) &&
        (place.EquipmentId is not { } equipment || RoomContents.ProvidedEquipment(packages, [item.Id]).Contains(equipment, StringComparer.Ordinal));
}

internal static class ProcedureStationValidator
{
    internal static IReadOnlyList<ValidationIssue> Validate(ContentPackage package, Func<string, ContentDefinition?> resolve)
    {
        var issues = new List<ValidationIssue>();
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in package.ProcedureStations ?? [])
        {
            var path = set.DefinitionId + ".procedureStations";
            if (resolve(set.DefinitionId) is { } target && target is not (ExaminationDefinition or TreatmentDefinition))
                issues.Add(new("procedure-station-target", path, "Stations belong to an examination or a treatment."));
            if (!targets.Add(set.DefinitionId))
                issues.Add(new("procedure-station-target", path, "One station set per examination or treatment and package."));
            if (set.Stations.Count == 0 || set.Stations.Any(s => s.Share <= 0) ||
                Math.Abs(set.Stations.Sum(s => s.Share) - 1) > ProcedureStations.ShareTolerance)
                issues.Add(new("procedure-station-share", path, "At least one station; every share is above 0 and they sum to 1."));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var station in set.Stations)
            {
                var stationPath = path + "[" + station.Key + "]";
                if (!ContentFormat.IsValidNamespace(station.Key) || !keys.Add(station.Key))
                    issues.Add(new("procedure-station-key", stationPath, "Use unique stable station keys."));
                CheckPlace(station.Patient, stationPath + ".patient");
                if (station.Staff is { } staff) CheckPlace(staff, stationPath + ".staff");
            }
        }
        return issues;

        void CheckPlace(StationPlace place, string path)
        {
            if (!ContentFormat.IsValidNamespace(place.PointKind) || (place.PointKind == ProcedureStations.Standing && place.EquipmentId is not null))
                issues.Add(new("procedure-station-place", path, "Use a stable point kind; standing names no equipment."));
            if (place.EquipmentId is { } id && resolve(id) is { } equipment && equipment is not MedicalEquipmentDefinition)
                issues.Add(new("procedure-station-place", path, "A station place requires a medical equipment reference."));
        }
    }
}
