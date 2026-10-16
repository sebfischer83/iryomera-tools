using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Iryomera.ModKit;

/// <summary>Data-only ZIP integrity and bounded staging. No executable/asset loader or installation runtime.</summary>
public sealed class ModArchive
{
    public async Task PackAsync(string directory, string output, IEnumerable<Iryomera.Content.SDK.ContentPackage>? dependencies = null,
        string? gameVersion = null, PackageLimits? limits = null, CancellationToken ct = default)
    {
        limits ??= new(); limits.EnsureValid(); output = PackageFiles.NewDestination(output);
        PackageFiles.NoLinks(directory);
        PackageFiles.NoLinks(Path.Combine(directory, ".iryomera-mod.lock"));
        await using var projectLock = new FileStream(Path.Combine(directory, ".iryomera-mod.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var scratch = PackageFiles.Scratch(); var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var captured = new List<ModFileDigest>(); long total = 0;
            async Task Capture(string name)
            {
                var source = PackageFiles.PathFor(directory, name); var target = Path.Combine(scratch, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = File.OpenRead(source); await using var destination = File.Create(target);
                var digest = await PackageFiles.CopyAsync(input, destination, name, Math.Min(limits.MaxFileBytes, limits.MaxExpandedBytes - total), ct);
                total += digest.SizeBytes; captured.Add(digest);
            }
            await Capture("mod.json");
            var project = await PackageFiles.ReadJsonAsync(scratch, "mod.json", limits, s => ModDocuments.ReadAsync<ModProject>(s, ct));
            if (project.Files.Count + 2 > limits.MaxEntries) throw new InvalidDataException("Too many package entries.");
            PackageFiles.CheckPaths(project.Files.Select(f => f.Path).Concat(["mod.json", "checksums.json"]));
            foreach (var file in project.Files.OrderBy(f => f.Path, StringComparer.Ordinal)) await Capture(file.Path);
            ModValidator.RequireValid(await new ModValidator().ValidateAsync(scratch, dependencies, gameVersion, limits, ct));
            foreach (var digest in captured)
            {
                await using var original = File.OpenRead(PackageFiles.PathFor(directory, digest.Path));
                var current = await PackageFiles.CopyAsync(original, Stream.Null, digest.Path, limits.MaxFileBytes, ct);
                if (current.SizeBytes != digest.SizeBytes || current.Sha256 != digest.Sha256) throw new IOException("Source changed during capture: " + digest.Path);
            }
            await PackageFiles.WriteJsonAsync(Path.Combine(scratch, "checksums.json"), new ModArchiveIndex { Files = captured.OrderBy(f => f.Path, StringComparer.Ordinal).ToList() }, ct);
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, true))
            {
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create, true, Encoding.UTF8))
                    foreach (var name in captured.Select(f => f.Path).Append("checksums.json").Order(StringComparer.Ordinal))
                    {
                        var entry = zip.CreateEntry(name, CompressionLevel.Optimal); entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        entry.ExternalAttributes = unchecked((int)0x81a40000);
                        await using var input = File.OpenRead(PackageFiles.PathFor(scratch, name)); await using var destination = entry.Open();
                        await PackageFiles.CopyAsync(input, destination, name, limits.MaxFileBytes, ct);
                    }
                if (file.Length > limits.MaxArchiveBytes) throw new InvalidDataException("Compressed archive exceeds limit.");
            }
            // Enforce exactly the same rules on produced archives as on received archives.
            await InspectAsync(temporary, dependencies, gameVersion, limits, ct);
            ct.ThrowIfCancellationRequested(); File.Move(temporary, output);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); Directory.Delete(scratch, true); }
    }

    public async Task<ModValidationResult> InspectAsync(string archive, IEnumerable<Iryomera.Content.SDK.ContentPackage>? dependencies = null,
        string? gameVersion = null, PackageLimits? limits = null, CancellationToken ct = default)
    {
        var scratch = PackageFiles.Scratch();
        try { return await ReadAsync(archive, scratch, dependencies, gameVersion, limits ?? new(), ct); }
        finally { Directory.Delete(scratch, true); }
    }

    public async Task<ModValidationResult> UnpackAsync(string archive, string output, IEnumerable<Iryomera.Content.SDK.ContentPackage>? dependencies = null,
        string? gameVersion = null, PackageLimits? limits = null, CancellationToken ct = default)
    {
        output = PackageFiles.NewDestination(output);
        var scratch = Path.Combine(Path.GetDirectoryName(output)!, ".iryomera-unpack-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        try
        {
            var result = await ReadAsync(archive, scratch, dependencies, gameVersion, limits ?? new(), ct);
            ct.ThrowIfCancellationRequested(); Directory.Move(scratch, output); return result;
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }

    private static async Task<ModValidationResult> ReadAsync(string archive, string scratch, IEnumerable<Iryomera.Content.SDK.ContentPackage>? dependencies,
        string? gameVersion, PackageLimits limits, CancellationToken ct)
    {
        limits.EnsureValid(); PackageFiles.NoLinks(archive);
        await using var input = File.OpenRead(archive);
        if (input.Length > limits.MaxArchiveBytes) throw new InvalidDataException("Compressed archive exceeds limit.");
        await PreflightAsync(input, limits, ct); input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, true, Encoding.UTF8);
        if (zip.Entries.Count > limits.MaxEntries) throw new InvalidDataException("Too many archive entries.");
        PackageFiles.CheckPaths(zip.Entries.Select(e => e.FullName));
        long declared = 0;
        foreach (var entry in zip.Entries)
        {
            var mode = (entry.ExternalAttributes >> 16) & 0xf000;
            if ((mode != 0 && mode != 0x8000) || (entry.ExternalAttributes & ((int)FileAttributes.ReparsePoint | (int)FileAttributes.Directory)) != 0)
                throw new InvalidDataException("Links, directories and special ZIP entries are not allowed.");
            if (entry.Length > limits.MaxFileBytes || entry.Length > limits.MaxExpandedBytes - declared ||
                (entry.Length > 0 && (entry.CompressedLength == 0 || (decimal)entry.Length / entry.CompressedLength > limits.MaxCompressionRatio)))
                throw new InvalidDataException("Archive size/compression ratio exceeds limit.");
            declared += entry.Length;
        }
        var indexEntry = zip.Entries.SingleOrDefault(e => e.FullName == "checksums.json") ?? throw new InvalidDataException("Missing checksums.json.");
        await using var indexBytes = new MemoryStream();
        await using (var stream = indexEntry.Open()) await PackageFiles.CopyAsync(stream, indexBytes, indexEntry.FullName, Math.Min(limits.MaxJsonBytes, limits.MaxFileBytes), ct);
        indexBytes.Position = 0; var index = await ModDocuments.ReadAsync<ModArchiveIndex>(indexBytes, ct);
        PackageFiles.CheckPaths(index.Files.Select(f => f.Path).Concat(["checksums.json"]));
        if (!index.Files.Select(f => f.Path).Append("checksums.json").ToHashSet(StringComparer.Ordinal).SetEquals(zip.Entries.Select(e => e.FullName))) throw new InvalidDataException("Integrity index and archive entries differ.");
        var digests = index.Files.ToDictionary(f => f.Path, StringComparer.Ordinal); long actual = indexBytes.Length;
        foreach (var entry in zip.Entries.Where(e => e != indexEntry))
        {
            var expected = digests[entry.FullName];
            if (expected.SizeBytes != entry.Length) throw new InvalidDataException("Entry size differs from its index.");
            var path = Path.Combine(scratch, entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(path)!); PackageFiles.NoLinks(path);
            await using var source = entry.Open(); await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            var digest = await PackageFiles.CopyAsync(source, target, entry.FullName, Math.Min(expected.SizeBytes, limits.MaxExpandedBytes - actual), ct);
            actual += digest.SizeBytes;
            if (digest.SizeBytes != expected.SizeBytes || digest.Sha256 != expected.Sha256) throw new InvalidDataException("Entry hash/size mismatch: " + entry.FullName);
        }
        await File.WriteAllBytesAsync(Path.Combine(scratch, "checksums.json"), indexBytes.ToArray(), ct);
        var result = await new ModValidator().ValidateAsync(scratch, dependencies, gameVersion, limits, ct);
        ModValidator.RequireValid(result);
        if (!result.Project.Files.Select(f => f.Path).Append("mod.json").ToHashSet(StringComparer.Ordinal).SetEquals(index.Files.Select(f => f.Path)))
            throw new InvalidDataException("Mod manifest and integrity inventory differ.");
        return result;
    }

    // Bound the central directory before ZipArchive allocates its entry metadata. V1 deliberately excludes ZIP64/multi-disk.
    private static async Task PreflightAsync(Stream input, PackageLimits limits, CancellationToken ct)
    {
        if (input.Length < 22) throw new InvalidDataException("Truncated ZIP.");
        var tail = new byte[(int)Math.Min(input.Length, 65557)]; input.Position = input.Length - tail.Length; await input.ReadExactlyAsync(tail, ct);
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != 0x06054b50 || i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) != tail.Length) continue;
            ushort Short(int at) => BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + at));
            uint Long(int at) => BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + at));
            var position = input.Length - tail.Length + i;
            if (Short(4) != 0 || Short(6) != 0 || Short(8) != Short(10) || Short(10) == ushort.MaxValue || Short(10) > limits.MaxEntries ||
                Long(12) == uint.MaxValue || Long(16) == uint.MaxValue || (long)Long(16) + Long(12) != position || Long(12) > (long)limits.MaxEntries * 1024)
                throw new InvalidDataException("ZIP64, multi-disk or oversized central directory is not supported.");
            return;
        }
        throw new InvalidDataException("Invalid ZIP end record.");
    }
}
