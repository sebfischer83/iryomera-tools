using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Iryomera.ModIo;

/// <summary>Explicit REST v1 requests, bounded responses and sanitized failures. Never automatically retries writes.</summary>
public sealed class ModIoClient : IModIoClient
{
    private readonly HttpClient http;
    private readonly ModIoOptions options;
    private readonly IModIoCredentials credentials;
    private readonly TimeProvider clock;
    private long blockedUntil;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, AllowDuplicateProperties = false };
    public ModIoClient(HttpClient http, ModIoOptions options, IModIoCredentials credentials, TimeProvider? clock = null)
    { options.Validate(); this.http = http; this.options = options; this.credentials = credentials; this.clock = clock ?? TimeProvider.System; }
    private static void Id(long id) { if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id)); }
    private string Path(long id) { Id(id); return $"games/{options.GameId}/mods/{id}"; }
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) => new(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    public async Task RequestEmailCodeAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || email.Any(char.IsControl)) throw new ArgumentException("Invalid email address.");
        await SendAsync(HttpMethod.Post, "oauth/emailrequest", Form(("email", email)), false, true, ct);
    }
    public async Task<ModIoAccessToken> ExchangeEmailCodeAsync(string securityCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(securityCode) || securityCode.Length > 128 || securityCode.Any(char.IsControl)) throw new ArgumentException("Invalid security code.");
        var bytes = await SendAsync(HttpMethod.Post, "oauth/emailexchange", Form(("security_code", securityCode)), false, true, ct);
        try
        {
            using var json = JsonDocument.Parse(bytes, new() { AllowDuplicateProperties = false }); var root = json.RootElement;
            return new(root.GetProperty("access_token").GetString()!, DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("date_expires").GetInt64()));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new ModIoTransportException("Invalid mod.io authentication response."); }
    }
    public Task<ModIoPage<ModIoMod>> SearchAsync(ModSearch search, CancellationToken ct = default)
    {
        if (search.Limit is < 1 or > 100 || search.Offset < 0 || search.Text?.Length > 1000) throw new ArgumentOutOfRangeException(nameof(search));
        return ReadAsync<ModIoPage<ModIoMod>>(HttpMethod.Get, $"games/{options.GameId}/mods?_limit={search.Limit}&_offset={search.Offset}" +
            (string.IsNullOrEmpty(search.Text) ? "" : "&_q=" + Uri.EscapeDataString(search.Text)), null, false, ct);
    }
    public async Task<ModIoMod> GetModAsync(long modId, CancellationToken ct = default)
    {
        var result = await ReadAsync<ModIoMod>(HttpMethod.Get, Path(modId), null, false, ct);
        if (result.Id != modId || result.GameId != options.GameId) throw new InvalidDataException("Remote mod identity mismatch."); return result;
    }
    public async Task<ModIoFile> GetFileAsync(long modId, long fileId, CancellationToken ct = default)
    {
        Id(fileId); var file = await ReadAsync<ModIoFile>(HttpMethod.Get, Path(modId) + "/files/" + fileId, null, false, ct);
        if (file.Id != fileId || file.ModId != modId) throw new InvalidDataException("Remote file identity mismatch."); return file;
    }
    public async Task SubscribeAsync(long modId, bool includeDependencies = false, CancellationToken ct = default) =>
        _ = await SendAsync(HttpMethod.Post, Path(modId) + "/subscribe", Form(("include_dependencies", includeDependencies ? "true" : "false")), true, false, ct);
    public async Task UnsubscribeAsync(long modId, CancellationToken ct = default) =>
        _ = await SendAsync(HttpMethod.Delete, Path(modId) + "/subscribe", null, true, false, ct);
    public Task<ModIoPage<ModIoMod>> GetDependenciesAsync(long modId, int offset = 0, CancellationToken ct = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        return ReadAsync<ModIoPage<ModIoMod>>(HttpMethod.Get, Path(modId) + $"/dependencies?recursive=false&_limit=100&_offset={offset}", null, false, ct);
    }
    public async Task<ModIoMod> PublishAsync(NewMod mod, Stream logo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mod.Name) || mod.Name.Length > 100 || string.IsNullOrWhiteSpace(mod.Summary) || mod.Summary.Length > 250 || string.IsNullOrWhiteSpace(mod.Description))
            throw new ArgumentException("Name, summary and description are required; name <= 100 and summary <= 250 characters.");
        if (mod.LogoFilename != System.IO.Path.GetFileName(mod.LogoFilename) || !new[] { ".png", ".jpg", ".jpeg" }.Contains(System.IO.Path.GetExtension(mod.LogoFilename).ToLowerInvariant()))
            throw new ArgumentException("Supply a PNG/JPEG logo filename without a path.");
        CheckStream(logo, options.MaxResponseBytes);
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(mod.Name), "name"); form.Add(new StringContent(mod.Summary), "summary"); form.Add(new StringContent(mod.Description), "description");
        form.Add(new StringContent("iryomera_schema:1.0.0"), "metadata_kvp[]");
        form.Add(new StreamContent(new BorrowedStream(logo)), "logo", mod.LogoFilename);
        foreach (var tag in mod.Tags ?? []) form.Add(new StringContent(tag), "tags[]");
        var result = await ReadAsync<ModIoMod>(HttpMethod.Post, $"games/{options.GameId}/mods", form, true, ct);
        if (result.Id <= 0 || result.GameId != options.GameId) throw new InvalidDataException("Published mod identity mismatch."); return result;
    }
    public async Task<ModIoFile> UploadAsync(long modId, Stream archive, string version, string? changelog = null, CancellationToken ct = default)
    {
        var path = Path(modId) + "/files"; CheckStream(archive, options.MaxDownloadBytes);
        if (string.IsNullOrWhiteSpace(version) || version.Length > 255) throw new ArgumentException("A release version is required.");
        var position = archive.Position; var md5 = Convert.ToHexString(await MD5.HashDataAsync(archive, ct)).ToLowerInvariant(); archive.Position = position;
        var form = new MultipartFormDataContent(); form.Add(new StreamContent(new BorrowedStream(archive)), "filedata", "mod.zip");
        form.Add(new StringContent(version), "version"); form.Add(new StringContent(md5), "filehash"); form.Add(new StringContent("true"), "active");
        if (changelog is not null) form.Add(new StringContent(changelog), "changelog");
        var result = await ReadAsync<ModIoFile>(HttpMethod.Post, path, form, true, ct);
        if (result.Id <= 0 || result.ModId != modId) throw new InvalidDataException("Uploaded file identity mismatch."); return result;
    }
    private static void CheckStream(Stream stream, long maximum)
    {
        if (!stream.CanRead || !stream.CanSeek || stream.Length - stream.Position > maximum || stream.Length == stream.Position)
            throw new ArgumentException("Supply a nonempty readable seekable stream within the configured size limit.");
    }
    private async Task<T> ReadAsync<T>(HttpMethod method, string path, HttpContent? body, bool tokenRequired, CancellationToken ct)
    {
        var bytes = await SendAsync(method, path, body, tokenRequired, false, ct);
        try
        {
            var result = JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new JsonException();
            if (result is ModIoPage<ModIoMod> page && (page.Data is null || page.ResultCount != page.Data.Count || page.ResultCount < 0 || page.ResultOffset < 0 || page.ResultLimit is < 1 or > 100 || page.ResultCount > page.ResultLimit || page.ResultTotal < (long)page.ResultOffset + page.ResultCount || page.Data.Any(m => m is null || m.Id <= 0 || m.GameId != options.GameId)))
                throw new InvalidDataException("Invalid mod.io page metadata or game identity.");
            return result;
        }
        catch (JsonException) { throw new ModIoTransportException("Invalid mod.io JSON response."); }
    }
    private async Task<byte[]> SendAsync(HttpMethod method, string path, HttpContent? body, bool tokenRequired, bool keyRequired, CancellationToken ct)
    {
        using var ownedBody = body;
        ct.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow(); var blocked = Interlocked.Read(ref blockedUntil);
        if (blocked > now.UtcTicks) throw new ModIoApiException(429, retryAfter: TimeSpan.FromTicks(blocked - now.UtcTicks));
        var auth = await credentials.GetAsync(ct); var token = auth.Token;
        var useToken = !keyRequired && token is not null && token.ExpiresAt > now;
        if (tokenRequired && !useToken) throw new InvalidOperationException("A current user access token is required.");
        if (!useToken && string.IsNullOrWhiteSpace(auth.ApiKey)) throw new InvalidOperationException("An API key is required.");
        if (!useToken) path += (path.Contains('?') ? "&" : "?") + "api_key=" + Uri.EscapeDataString(auth.ApiKey!);
        using var request = new HttpRequestMessage(method, new Uri(options.ApiBaseUri, path)) { Content = body };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (useToken) request.Headers.Authorization = new("Bearer", token!.Value);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException) { throw new ModIoTransportException("mod.io transport request failed."); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException("mod.io request canceled.", ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ModIoTransportException("mod.io transport request timed out."); }
        using (response)
        {
            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    TimeSpan? retry = null;
                    if ((int)response.StatusCode == 429)
                    {
                        retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - now) ?? TimeSpan.FromSeconds(60);
                        if (retry < TimeSpan.Zero) retry = TimeSpan.Zero;
                        if (retry > DateTimeOffset.MaxValue - now) retry = DateTimeOffset.MaxValue - now;
                        var until = (now + retry.Value).UtcTicks; long previous;
                        do { previous = Interlocked.Read(ref blockedUntil); if (previous >= until) break; }
                        while (Interlocked.CompareExchange(ref blockedUntil, until, previous) != previous);
                    }
                    int? reference = null;
                    try
                    {
                        var bytes = await SafeHttp.ReadAsync(response.Content, options.MaxResponseBytes, ct); using var doc = JsonDocument.Parse(bytes);
                        if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("error_ref", out var r) && r.TryGetInt32(out var value)) reference = value;
                    }
                    catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException) { }
                    throw new ModIoApiException((int)response.StatusCode, reference, retry);
                }
                return await SafeHttp.ReadAsync(response.Content, options.MaxResponseBytes, ct);
            }
            catch (HttpRequestException) { throw new ModIoTransportException("mod.io response transfer failed."); }
            catch (IOException) { throw new ModIoTransportException("mod.io response transfer failed."); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException("mod.io response transfer canceled.", ct); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ModIoTransportException("mod.io response transfer timed out."); }
        }
    }
}

internal sealed class BorrowedStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false;
    private readonly long length = inner.Length;
    public override long Length => length; public override long Position { get => inner.Position; set => inner.Position = value; }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, (int)Math.Min(count, Math.Max(0, length - inner.Position)));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, Math.Max(0, length - inner.Position))], cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
