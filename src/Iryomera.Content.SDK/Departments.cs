namespace Iryomera.Content.SDK;

/// <summary>A room type a department uses, aggregated over every departmentRoom relation that links it.</summary>
public sealed record DepartmentRoom(string RoomId, RelationRequirement Requirement, bool TreatmentRoom, IReadOnlyList<string> RelationIds);

/// <summary>
/// Department rooms and opening order (ADR 0020). Call on a validated package set; whether a department may open, and what
/// it costs, is game logic that reads unlockCost and unlockDischargedPatients from the resolved gameplay balancing.
/// </summary>
public static class Departments
{
    /// <summary>
    /// Room types of the department, ordered by room ID (ordinal). A room linked by several relations (for example base game
    /// and a mod) is listed once; it is required if any link requires it and the treatment room if any link marks it.
    /// </summary>
    public static IReadOnlyList<DepartmentRoom> Rooms(IEnumerable<ContentPackage> packages, string departmentId) =>
        packages.SelectMany(p => p.Relations)
            .Where(r => r.Kind == RelationKind.DepartmentRoom && r.FromId == departmentId)
            .GroupBy(r => r.ToId, StringComparer.Ordinal)
            .Select(g => new DepartmentRoom(g.Key,
                g.Any(r => RoomContents.EffectiveRequirement(r) == RelationRequirement.Required) ? RelationRequirement.Required : RelationRequirement.Optional,
                g.Any(r => r.TreatmentRoom == true), g.Select(r => r.Id).Order(StringComparer.Ordinal).ToArray()))
            .OrderBy(r => r.RoomId, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Departments that must already be open before this one can open, ordered by ID (ordinal), listed once.</summary>
    public static IReadOnlyList<string> Prerequisites(IEnumerable<ContentPackage> packages, string departmentId) =>
        packages.SelectMany(p => p.Relations)
            .Where(r => r.Kind == RelationKind.DepartmentPrerequisite && r.FromId == departmentId)
            .Select(r => r.ToId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}

internal static class DepartmentValidator
{
    internal static IEnumerable<ValidationIssue> Validate(ContentPackage package)
    {
        foreach (var relation in package.Relations.Where(r => r.TreatmentRoom is not null))
            if (relation.Kind != RelationKind.DepartmentRoom || relation.TreatmentRoom == true && RoomContents.EffectiveRequirement(relation) != RelationRequirement.Required)
                yield return new("relation-treatment-room", relation.Id, "A treatment room flag is allowed only on departmentRoom relations, and the treatment room must be required.");
        foreach (var department in package.Relations.Where(r => r.Kind == RelationKind.DepartmentRoom && r.TreatmentRoom == true).GroupBy(r => r.FromId, StringComparer.Ordinal))
            if (department.Select(r => r.ToId).Distinct(StringComparer.Ordinal).Count() > 1)
                yield return new("relation-treatment-room", department.Key, "A department has at most one treatment room type.");
        var prerequisites = package.Relations.Where(r => r.Kind == RelationKind.DepartmentPrerequisite)
            .GroupBy(r => r.FromId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(r => r.ToId).ToArray(), StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal); var active = new HashSet<string>(StringComparer.Ordinal);
        bool Cycle(string id)
        {
            if (active.Contains(id)) return true;
            if (!visited.Add(id)) return false;
            active.Add(id);
            foreach (var next in prerequisites.GetValueOrDefault(id, [])) if (Cycle(next)) return true;
            active.Remove(id); return false;
        }
        foreach (var id in prerequisites.Keys.Order(StringComparer.Ordinal))
            if (Cycle(id)) { yield return new("department-cycle", id, "Department prerequisites must be acyclic."); break; }
        foreach (var balance in package.Gameplay.Where(b => b.UnlockDischargedPatients is { } n && n != decimal.Truncate(n)))
            yield return new("balance-range", balance.DefinitionId, "Discharged patients for unlocking must be a whole number.");
        foreach (var change in (package.GameplayOverrides ?? []).Where(c => c.UnlockDischargedPatients is { } n && n != decimal.Truncate(n)))
            yield return new("balance-range", change.Id, "Discharged patients for unlocking must be a whole number.");
        var hours = package.Gameplay.Select(b => (Path: b.DefinitionId, b.OpenFromHour, b.OpenToHour, b.Arrival))
            .Concat((package.GameplayOverrides ?? []).Select(c => (Path: c.Id, c.OpenFromHour, c.OpenToHour, c.Arrival)));
        foreach (var (path, from, to, arrival) in hours)
        {
            if (new[] { from, to }.Any(h => h is { } x && (x < 0 || x > 24 || x != decimal.Truncate(x))) || from >= to)
                yield return new("balance-range", path, "Opening hours are whole hours in [0,24] with openFromHour before openToHour.");
            if (arrival is { } a && !Enum.IsDefined(a)) yield return new("balance-range", path, "Unknown patient arrival route.");
        }
        foreach (var room in package.Definitions.OfType<RoomDefinition>().Where(r => r.Role is { } role && !Enum.IsDefined(role)))
            yield return new("room-role", room.Id, "Unknown room role.");
    }
}
