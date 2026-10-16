using System.Text.Json.Serialization;

namespace Iryomera.ModIo;

/// <summary>Non-secret transport configuration. No request is made by constructing a client.</summary>
public sealed record ModIoOptions
{
    public required long GameId { get; init; }
    public Uri ApiBaseUri { get; init; } = new("https://api.mod.io/v1/");
    public int MaxResponseBytes { get; init; } = 8 * 1024 * 1024;
    public long MaxDownloadBytes { get; init; } = 128L * 1024 * 1024;
    public int MaxGraphNodes { get; init; } = 256;
    public int MaxGraphDepth { get; init; } = 64;
    public int MaxPages { get; init; } = 100;
    public int MaxRedirects { get; init; } = 5;
    public IReadOnlyList<string> DownloadHosts { get; init; } = ["api.mod.io", "*.modapi.io", "*.modcdn.io"];
    internal void Validate()
    {
        if (GameId <= 0 || MaxResponseBytes <= 0 || MaxDownloadBytes <= 0 || MaxGraphNodes <= 0 || MaxGraphDepth <= 0 || MaxPages <= 0 || MaxRedirects < 0)
            throw new ArgumentOutOfRangeException(nameof(ModIoOptions));
        if (!SafeHttp.IsHttps(ApiBaseUri) || !ApiBaseUri.AbsolutePath.EndsWith('/') || ApiBaseUri.Query.Length != 0)
            throw new ArgumentException("Use an absolute HTTPS API root ending in '/' without credentials, query or fragment.");
        if (DownloadHosts.Any(h => string.IsNullOrWhiteSpace(h) || h.Contains('/') || h.Contains(':') || h.TrimStart('*', '.').Length == 0))
            throw new ArgumentException("Configure exact download hosts or *.domain host suffixes.");
    }
}

/// <summary>Transient authentication result. Deliberately excluded from serialization and diagnostic text.</summary>
public sealed class ModIoAccessToken
{
    [JsonIgnore] public string Value { get; }
    public DateTimeOffset ExpiresAt { get; }
    public ModIoAccessToken(string value, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)) throw new ArgumentException("A nonempty token without control characters is required.");
        Value = value; ExpiresAt = expiresAt;
    }
    public override string ToString() => "ModIoAccessToken [redacted]";
}

/// <summary>Immutable secret snapshot, never persist or log its explicit secret accessors.</summary>
public sealed class ModIoAuthentication(string? apiKey, ModIoAccessToken? token)
{
    [JsonIgnore] public string? ApiKey { get; } = apiKey;
    [JsonIgnore] public ModIoAccessToken? Token { get; } = token;
    public override string ToString() => "ModIoAuthentication [redacted]";
}
public interface IModIoCredentials
{
    ValueTask<ModIoAuthentication> GetAsync(CancellationToken cancellationToken = default);
}
public sealed class InMemoryModIoCredentials : IModIoCredentials
{
    private ModIoAuthentication current = new(null, null);
    public void Set(string? apiKey, ModIoAccessToken? token = null)
    {
        if (apiKey is not null && (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsControl))) throw new ArgumentException("Invalid API key.");
        Volatile.Write(ref current, new(apiKey, token));
    }
    public ValueTask<ModIoAuthentication> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Volatile.Read(ref current));
    }
    public override string ToString() => "InMemoryModIoCredentials [redacted]";
}

public sealed class ModIoApiException : Exception
{
    public int StatusCode { get; }
    public int? ErrorReference { get; }
    public TimeSpan? RetryAfter { get; }
    public ModIoApiException(int statusCode, int? errorReference = null, TimeSpan? retryAfter = null)
        : base($"mod.io request failed (HTTP {statusCode}).")
    { StatusCode = statusCode; ErrorReference = errorReference; RetryAfter = retryAfter; }
}
public sealed class ModIoTransportException(string message) : Exception(message);

internal static class SafeHttp
{
    internal static bool IsHttps(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    internal static async Task<byte[]> ReadAsync(HttpContent content, long maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum) throw new InvalidDataException("HTTP payload exceeds limit.");
        await using var input = await content.ReadAsStreamAsync(ct); using var output = new MemoryStream();
        await CopyAsync(input, output, maximum, ct); return output.ToArray();
    }
    internal static async Task<long> CopyAsync(Stream input, Stream output, long maximum, CancellationToken ct)
    {
        var buffer = new byte[65536]; long count = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), ct); if (read == 0) return count;
            if (read > maximum - count) throw new InvalidDataException("Payload exceeds limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct); count += read;
        }
    }
    internal static void NewTarget(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("Destination already exists.");
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked destination paths are not allowed."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    }
}
