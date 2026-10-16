using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Iryomera.Content.SDK;

namespace Iryomera.ModKit;

internal static class PackageFiles
{
    internal static bool Safe(string path) => path.Length is > 0 and <= 200 && path.Split('/').All(s => s.Length is > 0 and <= 100 &&
        Regex.IsMatch(s, "^[A-Za-z0-9][A-Za-z0-9._-]*\\z", RegexOptions.CultureInvariant) && !s.EndsWith('.') &&
        !Regex.IsMatch(s.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    internal static string PathFor(string root, string relative)
    {
        if (!Safe(relative)) throw new InvalidDataException("Unsafe or nonportable package path: " + relative);
        var current = Path.GetFullPath(root); NoLinks(current);
        foreach (var segment in relative.Split('/'))
        {
            if (Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any(p => Path.GetFileName(p) == segment))
                throw new FileNotFoundException("Missing package file or mismatching path case: " + relative);
            current = Path.Combine(current, segment); NoLinks(current);
        }
        if (!File.Exists(current)) throw new FileNotFoundException("Missing package file: " + relative);
        return current;
    }
    internal static void NoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Links/reparse points are not accepted: " + current); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    internal static void CheckPaths(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            if (!Safe(path) || !seen.Add(path)) throw new InvalidDataException("Unsafe or colliding package path: " + path);
        foreach (var path in seen)
        {
            var parts = path.Split('/');
            for (var i = 1; i < parts.Length; i++) if (seen.Contains(string.Join('/', parts.Take(i)))) throw new InvalidDataException("A package file is used as a parent directory.");
        }
    }
    internal static async Task<ModFileDigest> CopyAsync(Stream input, Stream output, string path, long limit, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long count = 0;
        var buffer = new byte[65536]; int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (read > limit - count) throw new InvalidDataException("Expanded file exceeds its size limit: " + path);
            count += read; hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return new() { Path = path, SizeBytes = count, Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() };
    }
    internal static async Task<T> ReadJsonAsync<T>(string root, string relative, PackageLimits limits, Func<Stream, Task<T>> read) where T : class
    {
        await using var input = File.OpenRead(PathFor(root, relative));
        if (input.Length > Math.Min(limits.MaxJsonBytes, limits.MaxFileBytes)) throw new InvalidDataException("JSON exceeds its size limit.");
        return await read(input);
    }
    internal static string Scratch() { var path = Path.Combine(Path.GetTempPath(), "iryomera-mod-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); NoLinks(path); return path; }
    internal static string NewDestination(string path)
    {
        path = Path.GetFullPath(path); if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Destination already exists.");
        var parent = Path.GetDirectoryName(path)!; NoLinks(parent); Directory.CreateDirectory(parent); NoLinks(parent); return path;
    }
    internal static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    { await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true); await ContentJson.WriteAsync(stream, value, ct); }
}
