using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Xunit;

namespace Iryomera.ModIo.Tests;

public sealed class DownloadTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "iryomera-download-tests-" + Guid.NewGuid().ToString("N"));
    public DownloadTests() => Directory.CreateDirectory(root);
    private string Target => Path.Combine(root, "mod.zip");
    private static ModIoFile FileInfo(string url = "https://cdn.example.invalid/mod.zip?transient=secret", long size = 3, string md5 = "900150983cd24fb0d6963f7d28e17f72", long? expires = null) => new()
    { Id = 12, ModId = 3, Filesize = size, Filehash = new() { Md5 = md5 }, Download = new() { BinaryUrl = new(url), DateExpires = expires } };
    private static HttpResponseMessage Bytes(byte[]? data = null) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data ?? "abc"u8.ToArray()) };
    [Fact] public async Task Verified_download_publishes_exact_bytes_with_md5_and_sha256_without_auth()
    {
        using var handler = new TestHttp((r, _) =>
        { Assert.Null(r.Headers.Authorization); Assert.False(r.Headers.Contains("Cookie")); Assert.DoesNotContain("api_key", r.RequestUri!.Query); return Task.FromResult(Bytes()); });
        var manager = new ModDownloadManager(new(handler), TestHttp.Options); var result = await manager.DownloadAsync(FileInfo(), Target);
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Target)); Assert.Equal(Convert.ToHexString(SHA256.HashData("abc"u8)).ToLowerInvariant(), result.Sha256);
        Assert.Equal(12, result.Origin.FileId); Assert.Equal(7, result.Origin.GameId); Assert.DoesNotContain("transient", result.ToString());
    }
    [Theory]
    [InlineData("http://cdn.example.invalid/mod.zip")]
    [InlineData("https://user:password@cdn.example.invalid/mod.zip")]
    [InlineData("https://cdn.example.invalid.evil/mod.zip")]
    [InlineData("https://evil.invalid/mod.zip")]
    [InlineData("https://cdn.example.invalid/mod.zip#fragment")]
    public async Task Untrusted_initial_urls_never_make_a_request(string url)
    {
        using var handler = new TestHttp((_, _) => throw new Exception("No network expected"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModDownloadManager(new(handler), TestHttp.Options).DownloadAsync(FileInfo(url), Target)); Assert.Equal(0, handler.Calls); Assert.False(File.Exists(Target));
    }
    [Theory] [InlineData("https://evil.invalid/mod.zip")] [InlineData("http://cdn.example.invalid/mod.zip")] [InlineData("https://user:password@cdn.example.invalid/mod.zip")]
    public async Task Unsafe_redirects_are_rejected_without_a_second_request(string redirect)
    {
        using var handler = new TestHttp((_, _) => { var result = new HttpResponseMessage(HttpStatusCode.Redirect); result.Headers.Location = new(redirect); return Task.FromResult(result); });
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModDownloadManager(new(handler), TestHttp.Options).DownloadAsync(FileInfo(), Target)); Assert.Equal(1, handler.Calls); Assert.Empty(Directory.GetFiles(root));
    }
    [Fact] public async Task Allowed_redirects_remain_credential_free_and_are_bounded()
    {
        using var handler = new TestHttp((r, _) =>
        {
            Assert.Null(r.Headers.Authorization);
            if (r.RequestUri!.Host == "api.mod.io") { var redirect = new HttpResponseMessage(HttpStatusCode.Redirect); redirect.Headers.Location = new("https://cdn.example.invalid/final.zip"); return Task.FromResult(redirect); }
            return Task.FromResult(Bytes());
        });
        await new ModDownloadManager(new(handler), TestHttp.Options).DownloadAsync(FileInfo("https://api.mod.io/download/hash"), Target); Assert.Equal(2, handler.Calls);
        File.Delete(Target);
        using var loop = new TestHttp((_, _) => { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new("https://cdn.example.invalid/loop"); return Task.FromResult(r); });
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModDownloadManager(new(loop), TestHttp.Options with { MaxRedirects = 1 }).DownloadAsync(FileInfo(), Target)); Assert.Equal(2, loop.Calls); Assert.Empty(Directory.GetFiles(root));
    }
    [Theory] [InlineData("size")] [InlineData("md5")] [InlineData("sha256")] [InlineData("truncated")] [InlineData("overflow")] [InlineData("partial")]
    public async Task Corrupt_or_incomplete_downloads_leave_no_published_or_temporary_file(string variant)
    {
        using var handler = new TestHttp((_, _) =>
        {
            var r = Bytes(variant == "truncated" ? "ab"u8.ToArray() : variant == "overflow" ? "abcd"u8.ToArray() : null);
            if (variant is "truncated" or "overflow") r.Content.Headers.ContentLength = 3;
            if (variant == "partial") r.StatusCode = HttpStatusCode.PartialContent; return Task.FromResult(r);
        });
        var info = FileInfo(size: variant == "size" ? 4 : 3, md5: variant == "md5" ? new('0', 32) : "900150983cd24fb0d6963f7d28e17f72");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModDownloadManager(new(handler), TestHttp.Options).DownloadAsync(info, Target, variant == "sha256" ? new('0', 64) : null)); Assert.Empty(Directory.GetFiles(root));
    }
    [Fact] public async Task Limits_expired_metadata_and_existing_destinations_prevent_downloads()
    {
        using var handler = new TestHttp((_, _) => throw new Exception("No network expected"));
        var manager = new ModDownloadManager(new(handler), TestHttp.Options with { MaxDownloadBytes = 2 });
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.DownloadAsync(FileInfo(), Target));
        manager = new(new(handler), TestHttp.Options);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.DownloadAsync(FileInfo(expires: 1), Target));
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.DownloadAsync(FileInfo(md5: "invalid"), Target));
        await File.WriteAllTextAsync(Target, "keep"); await Assert.ThrowsAsync<IOException>(() => manager.DownloadAsync(FileInfo(), Target)); Assert.Equal("keep", await File.ReadAllTextAsync(Target)); Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task Midstream_cancellation_cleans_partial_output()
    {
        using var canceled = new CancellationTokenSource();
        using var handler = new TestHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CancelingStream(canceled)) }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModDownloadManager(new(handler), TestHttp.Options).DownloadAsync(FileInfo(), Target, ct: canceled.Token)); Assert.Empty(Directory.GetFiles(root));
    }
    [Fact] public async Task Network_exceptions_never_echo_signed_download_url()
    {
        using var handler = new TestHttp((r, _) => throw new HttpRequestException(r.RequestUri!.ToString()));
        var error = await Assert.ThrowsAsync<ModIoTransportException>(() => new ModDownloadManager(new(handler), TestHttp.Options).DownloadAsync(FileInfo(), Target)); Assert.DoesNotContain("transient", error.ToString()); Assert.Null(error.InnerException);
    }
    [Fact] public void Authenticated_download_client_is_rejected()
    {
        using var client = new HttpClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-token"); Assert.Throws<ArgumentException>(() => new ModDownloadManager(client, TestHttp.Options));
    }
    private sealed class CancelingStream(CancellationTokenSource source) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); buffer.Span[0] = 97; source.Cancel(); return ValueTask.FromResult(1); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Flush() { } public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
