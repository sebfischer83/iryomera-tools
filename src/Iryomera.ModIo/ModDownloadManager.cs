using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Iryomera.ModIo;

public sealed record VerifiedArchive(string Path, string Sha256, long SizeBytes, ModOrigin Origin);

/// <summary>Credential-free, bounded HTTPS streaming. The supplied client must disable automatic redirects/cookies.</summary>
public sealed class ModDownloadManager
{
    private readonly HttpClient http;
    private readonly ModIoOptions options;
    private readonly TimeProvider clock;
    public ModDownloadManager(HttpClient http, ModIoOptions options, TimeProvider? clock = null)
    {
        options.Validate();
        if (http.DefaultRequestHeaders.Authorization is not null || http.DefaultRequestHeaders.Contains("Cookie"))
            throw new ArgumentException("Use a dedicated credential-free download HttpClient.");
        this.http = http; this.options = options; this.clock = clock ?? TimeProvider.System;
    }
    public async Task<VerifiedArchive> DownloadAsync(ModIoFile file, string output, string? expectedSha256 = null, CancellationToken ct = default)
    {
        if (file.Id <= 0 || file.ModId <= 0 || file.Filesize <= 0 || file.Filesize > options.MaxDownloadBytes ||
            file.Filehash is null || !Regex.IsMatch(file.Filehash.Md5 ?? "", "^[a-fA-F0-9]{32}\\z") || file.Download is null)
            throw new InvalidDataException("Incomplete or oversized remote file metadata.");
        if (expectedSha256 is not null && !Regex.IsMatch(expectedSha256, "^[a-fA-F0-9]{64}\\z")) throw new ArgumentException("Invalid expected SHA-256.");
        if (file.Download.DateExpires is > 0 && file.Download.DateExpires <= clock.GetUtcNow().ToUnixTimeSeconds())
            throw new InvalidDataException("Download URL expired; retrieve fresh file metadata.");
        var current = file.Download.BinaryUrl; ValidateUrl(current);
        output = System.IO.Path.GetFullPath(output); SafeHttp.NewTarget(output);
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            for (var hop = 0; ; hop++)
            {
                ct.ThrowIfCancellationRequested();
                // No API-key, Bearer token, caller cookies, or authenticated API client is reused here.
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (hop >= options.MaxRedirects || response.Headers.Location is null) throw new InvalidDataException("Download redirect limit or missing location.");
                    current = new Uri(current, response.Headers.Location); ValidateUrl(current); continue;
                }
                if (!response.IsSuccessStatusCode) throw new ModIoApiException((int)response.StatusCode);
                if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength is long size && size != file.Filesize)
                    throw new InvalidDataException("Unexpected download status or length.");
                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    var count = await SafeHttp.CopyAsync(input, destination, Math.Min(file.Filesize, options.MaxDownloadBytes), ct);
                    if (count != file.Filesize) throw new InvalidDataException("Downloaded size does not match metadata.");
                }
                await using var captured = File.OpenRead(temporary);
                var md5 = Convert.ToHexString(await MD5.HashDataAsync(captured, ct)); captured.Position = 0;
                var sha = Convert.ToHexString(await SHA256.HashDataAsync(captured, ct)).ToLowerInvariant();
                if (!md5.Equals(file.Filehash.Md5, StringComparison.OrdinalIgnoreCase) || expectedSha256 is not null && !sha.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded checksum does not match metadata.");
                await captured.DisposeAsync(); ct.ThrowIfCancellationRequested(); File.Move(temporary, output);
                return new(output, sha, file.Filesize, new() { Provider = "modio", GameId = options.GameId, ModId = file.ModId, FileId = file.Id });
            }
        }
        catch (HttpRequestException) { throw new ModIoTransportException("Mod download transport failed."); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException("Mod download canceled.", ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ModIoTransportException("Mod download timed out."); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private void ValidateUrl(Uri uri)
    {
        if (!SafeHttp.IsHttps(uri) || !options.DownloadHosts.Any(pattern => pattern.StartsWith("*.", StringComparison.Ordinal)
            ? uri.IdnHost.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase) && uri.IdnHost.Length > pattern.Length - 1
            : uri.IdnHost.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Download URL is outside the configured HTTPS host policy.");
    }
}
