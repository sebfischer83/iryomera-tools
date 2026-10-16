using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Iryomera.ModIo;
using Iryomera.ModIo.ModKit;
using Iryomera.ModKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Iryomera.ModIo.ModKit.Tests;

public sealed class IntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "iryomera-distribution-tests-" + Guid.NewGuid().ToString("N"));
    public IntegrationTests() => Directory.CreateDirectory(root);
    private string P(string name) => Path.Combine(root, name);
    private async Task<string> Archive()
    {
        await new ModProjects().CreateAsync(P("authoring"), "author.example", "Own synthetic test", "Synthetic example");
        await new ModArchive().PackAsync(P("authoring"), P("example.zip")); return P("example.zip");
    }
    [Fact] public async Task Local_mods_install_offline_without_modio_configuration_and_keep_all_bytes()
    {
        var archive = await Archive(); var local = new LocalArchiveSource(new Dictionary<string, string> { ["example"] = archive });
        var services = new ServiceCollection(); services.AddIryomeraModKitInstallation(); using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var result = await provider.GetRequiredService<ModRuntime>().InstallAsync(local, "example", P("installed"), gameVersion: "0.2.0");
        Assert.Equal("local", result.Receipt.Origin.Provider); Assert.Equal("author.example:mod", result.Receipt.PackageId);
        Assert.Equal(await File.ReadAllBytesAsync(P("authoring/content.json")), await File.ReadAllBytesAsync(P("installed/content.json")));
        Assert.True((await new ModValidator().ValidateAsync(P("installed"))).IsValid);
        using var receipt = new MemoryStream(); await ModReceiptDocuments.WriteAsync(receipt, result.Receipt); receipt.Position = 0;
        Assert.Equal(result.Receipt.ArchiveSha256, (await ModReceiptDocuments.ReadAsync(receipt)).ArchiveSha256); Assert.False(File.Exists(P("installed/receipt.json")));
        await Assert.ThrowsAsync<IOException>(() => provider.GetRequiredService<ModRuntime>().InstallAsync(local, "example", P("installed")));
    }
    [Fact] public async Task Mock_remote_download_installs_through_the_same_archive_policy()
    {
        var archive = await Archive(); var bytes = await File.ReadAllBytesAsync(archive);
        using var handler = new Stub((r, _) => { Assert.Null(r.Headers.Authorization); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); });
        var options = new ModIoOptions { GameId = 7, DownloadHosts = ["cdn.example.invalid"] };
        var file = new ModIoFile { Id = 12, ModId = 3, Filesize = bytes.Length, Filehash = new() { Md5 = Convert.ToHexString(MD5.HashData(bytes)) }, Download = new() { BinaryUrl = new("https://cdn.example.invalid/transient?hash=not-a-token") } };
        var delivered = await new ModDownloadManager(new(handler), options).DownloadAsync(file, P("download.zip"));
        var installed = await new ModKitInstaller().InstallAsync(delivered, P("installed")); Assert.Equal(12, installed.Receipt.Origin.FileId); Assert.Equal(7, installed.Receipt.Origin.GameId);
        Assert.DoesNotContain("transient", JsonSerializer.Serialize(installed.Receipt)); Assert.True((await new ModValidator().ValidateAsync(installed.Directory)).IsValid);
    }
    [Theory] [InlineData("hash")] [InlineData("size")] [InlineData("origin")] [InlineData("game")]
    public async Task Invalid_delivery_or_policy_never_publishes_installation(string variant)
    {
        var archive = await Archive(); var delivered = await new LocalArchiveSource(new Dictionary<string, string> { ["mod"] = archive }).FetchAsync("mod", P("capture.zip"));
        delivered = variant switch { "hash" => delivered with { Sha256 = new('0', 64) }, "size" => delivered with { SizeBytes = 1 }, "origin" => delivered with { Origin = new() { Provider = "modio" } }, _ => delivered };
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModKitInstaller().InstallAsync(delivered, P("installed"), gameVersion: variant == "game" ? "9.0.0" : null)); Assert.False(Directory.Exists(P("installed")));
    }
    [Fact] public async Task Invalid_archives_from_local_sources_cannot_skip_validation()
    {
        await File.WriteAllTextAsync(P("bad.zip"), "not a ZIP"); var local = new LocalArchiveSource(new Dictionary<string, string> { ["bad"] = P("bad.zip") });
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModRuntime(new ModKitInstaller()).InstallAsync(local, "bad", P("installed"))); Assert.False(Directory.Exists(P("installed")));
    }
    [Fact] public async Task Publisher_validates_content_version_before_any_http_write_and_streams_exact_archive()
    {
        var archive = await Archive(); var bytes = await File.ReadAllBytesAsync(archive);
        using var handler = new Stub(async (r, ct) =>
        {
            Assert.EndsWith("/games/7/mods/3/files", r.RequestUri!.AbsolutePath); Assert.Equal("synthetic-token", r.Headers.Authorization!.Parameter);
            var form = Assert.IsType<MultipartFormDataContent>(r.Content); var payload = form.Single(c => c.Headers.ContentDisposition!.Name!.Trim('"') == "filedata");
            Assert.Equal(bytes, await payload.ReadAsByteArrayAsync(ct));
            return new(HttpStatusCode.Created) { Content = new StringContent("{\"id\":12,\"mod_id\":3,\"filesize\":" + bytes.Length + ",\"filehash\":{\"md5\":\"" + Convert.ToHexString(MD5.HashData(bytes)) + "\"}}") };
        });
        var credentials = new InMemoryModIoCredentials(); credentials.Set(null, new("synthetic-token", DateTimeOffset.UtcNow.AddMinutes(10)));
        var publisher = new ModIoPublisher(new ModIoClient(new(handler), new() { GameId = 7 }, credentials));
        await Assert.ThrowsAsync<InvalidDataException>(() => publisher.UploadAsync(3, archive, "2.0.0")); Assert.Equal(0, handler.Calls);
        Assert.Equal(12, (await publisher.UploadAsync(3, archive, "1.0.0", "Synthetic test release")).Id); Assert.Equal(1, handler.Calls);
    }
    [Fact] public async Task Local_source_limits_unknown_ids_and_cancellation_leave_no_target()
    {
        var archive = await Archive(); var source = new LocalArchiveSource(new Dictionary<string, string> { ["mod"] = archive }, 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => source.FetchAsync("mod", P("copy.zip"))); Assert.False(File.Exists(P("copy.zip")));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => source.FetchAsync("../mod", P("copy.zip")));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModRuntime(new ModKitInstaller()).InstallAsync(source, "mod", P("installed"), ct: canceled.Token)); Assert.False(Directory.Exists(P("installed")));
    }
    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return send(request, cancellationToken); }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
