namespace Iryomera.ModIo;

/// <summary>Bounded remote dependency graph. Numeric mod.io identities do not replace SDK dependency/version validation.</summary>
public sealed class ModDependencyResolver(IModIoClient api, ModIoOptions options)
{
    public async Task<IReadOnlyList<ModIoMod>> ResolveAsync(long modId, CancellationToken ct = default)
    {
        options.Validate();
        var completed = new HashSet<long>(); var active = new HashSet<long>(); var seen = new HashSet<long>(); var ordered = new List<ModIoMod>();
        async Task Visit(ModIoMod mod, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (mod.Id <= 0 || mod.GameId != options.GameId) throw new InvalidDataException("Dependency game/mod identity mismatch.");
            if (active.Contains(mod.Id)) throw new InvalidDataException("Cyclic mod.io dependencies.");
            if (completed.Contains(mod.Id)) return;
            if (depth >= options.MaxGraphDepth || seen.Add(mod.Id) && seen.Count > options.MaxGraphNodes) throw new InvalidDataException("Dependency graph exceeds configured limits.");
            active.Add(mod.Id); var children = new Dictionary<long, ModIoMod>(); var offset = 0; long? expectedTotal = null;
            for (var pageNumber = 0; ; pageNumber++)
            {
                if (pageNumber >= options.MaxPages) throw new InvalidDataException("Dependency pagination exceeds configured limit.");
                var page = await api.GetDependenciesAsync(mod.Id, offset, ct);
                if (page.Data is null || page.ResultCount != page.Data.Count || page.ResultOffset != offset || page.ResultCount < 0 || page.ResultLimit is < 1 or > 100 ||
                    page.ResultCount > page.ResultLimit || page.ResultTotal < (long)offset + page.ResultCount || expectedTotal is not null && expectedTotal != page.ResultTotal)
                    throw new InvalidDataException("Inconsistent dependency pagination.");
                expectedTotal = page.ResultTotal;
                foreach (var child in page.Data)
                {
                    if (!children.TryAdd(child.Id, child)) throw new InvalidDataException("Duplicate paginated dependency identity.");
                    if (children.Count > options.MaxGraphNodes) throw new InvalidDataException("Dependency graph exceeds configured limits.");
                }
                offset = checked(offset + page.ResultCount); if (offset >= page.ResultTotal) break;
                if (page.ResultCount == 0) throw new InvalidDataException("Dependency pagination made no progress.");
            }
            foreach (var child in children.Values.OrderBy(x => x.Id)) await Visit(child, depth + 1);
            active.Remove(mod.Id); completed.Add(mod.Id); ordered.Add(mod);
        }
        await Visit(await api.GetModAsync(modId, ct), 0); return ordered.AsReadOnly();
    }
}
public sealed class ModUpdateService(IModIoClient api)
{
    public async Task<ModUpdate> CheckAsync(long modId, long installedFileId, CancellationToken ct = default)
    {
        if (installedFileId <= 0) throw new ArgumentOutOfRangeException(nameof(installedFileId));
        var mod = await api.GetModAsync(modId, ct); var file = mod.Modfile;
        if (file is null || file.Id <= 0 || file.ModId != modId) throw new InvalidDataException("No matching current modfile is available.");
        return new(modId, installedFileId, file.Id, file.Version);
    }
}
