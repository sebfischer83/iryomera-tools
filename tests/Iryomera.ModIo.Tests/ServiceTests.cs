using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.ModIo.Tests;

public sealed class ServiceTests
{
    [Fact] public async Task Dependencies_are_paginated_dependency_first_and_shared_nodes_are_not_repeated()
    {
        using var handler = new TestHttp((r, _) =>
        {
            if (!r.RequestUri!.AbsolutePath.EndsWith("/dependencies")) return Task.FromResult(TestHttp.Json(TestHttp.Mod(1)));
            Assert.Contains("recursive=false", r.RequestUri.Query);
            var id = long.Parse(r.RequestUri.AbsolutePath.Split('/')[^2]);
            if (id == 1 && r.RequestUri.Query.Contains("_offset=0")) return Task.FromResult(TestHttp.Json(new ModIoPage<ModIoMod> { Data = [TestHttp.Mod(2)], ResultCount = 1, ResultOffset = 0, ResultLimit = 1, ResultTotal = 2 }));
            if (id == 1) return Task.FromResult(TestHttp.Json(new ModIoPage<ModIoMod> { Data = [TestHttp.Mod(3)], ResultCount = 1, ResultOffset = 1, ResultLimit = 1, ResultTotal = 2 }));
            return Task.FromResult(TestHttp.Json(id is 2 or 3 ? TestHttp.Page(TestHttp.Mod(4)) : TestHttp.Page()));
        });
        var order = await new ModDependencyResolver(handler.Client(), TestHttp.Options).ResolveAsync(1); Assert.Equal(new long[] { 4, 2, 3, 1 }, order.Select(x => x.Id)); Assert.Equal(6, handler.Calls);
    }
    [Theory] [InlineData("cycle")] [InlineData("wrong-game")] [InlineData("depth")] [InlineData("nodes")] [InlineData("empty-page")] [InlineData("offset")] [InlineData("duplicate")]
    public async Task Invalid_dependency_graphs_fail_explicitly(string variant)
    {
        using var handler = new TestHttp((r, _) =>
        {
            if (!r.RequestUri!.AbsolutePath.EndsWith("/dependencies")) return Task.FromResult(TestHttp.Json(TestHttp.Mod(1)));
            var mod = variant == "wrong-game" ? new ModIoMod { Id = 2, GameId = 8, Name = "Wrong game" } : TestHttp.Mod(variant == "cycle" ? 1 : 2);
            var page = new ModIoPage<ModIoMod> { Data = variant == "empty-page" ? [] : variant == "duplicate" ? [mod, mod] : [mod], ResultCount = variant == "empty-page" ? 0 : variant == "duplicate" ? 2 : 1, ResultOffset = variant == "offset" ? 4 : 0, ResultLimit = 100, ResultTotal = variant == "duplicate" ? 2 : 1 };
            return Task.FromResult(TestHttp.Json(page));
        });
        var options = TestHttp.Options with { MaxGraphDepth = variant == "depth" ? 1 : 64, MaxGraphNodes = variant == "nodes" ? 1 : 256 };
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModDependencyResolver(handler.Client(), options).ResolveAsync(1)); Assert.True(handler.Calls < 5);
    }
    [Theory] [InlineData(10, false)] [InlineData(9, true)] [InlineData(11, true)]
    public async Task Update_checks_file_identity_including_rollbacks(long installed, bool expected)
    {
        using var handler = new TestHttp((_, _) => Task.FromResult(TestHttp.Json(TestHttp.Mod(3, 10, "not-semver"))));
        var update = await new ModUpdateService(handler.Client()).CheckAsync(3, installed); Assert.Equal(expected, update.IsAvailable); Assert.Equal("not-semver", update.AvailableVersion);
    }
    [Fact] public async Task Source_refreshes_current_modfile_and_downloads_with_independent_client()
    {
        using var apiHandler = new TestHttp((r, _) => Task.FromResult(TestHttp.Json(r.RequestUri!.AbsolutePath.EndsWith("/files/12")
            ? (object)new ModIoFile { Id = 12, ModId = 3, Filesize = 3, Filehash = new() { Md5 = "900150983cd24fb0d6963f7d28e17f72" }, Download = new() { BinaryUrl = new("https://cdn.example.invalid/file.zip") } }
            : TestHttp.Mod(3, 12))));
        using var downloads = new TestHttp((r, _) => { Assert.Null(r.Headers.Authorization); return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent("abc"u8.ToArray()) }); });
        var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        { var source = new ModIoSource(apiHandler.Client(credentials: TestHttp.Credentials(true)), new(new(downloads), TestHttp.Options)); var result = await source.FetchAsync("3", output); Assert.Equal(12, result.Origin.FileId); Assert.Equal(2, apiHandler.Calls); Assert.Equal(1, downloads.Calls); }
        finally { if (File.Exists(output)) File.Delete(output); }
    }
    [Fact] public async Task Receipt_roundtrip_preserves_extensions_without_urls_or_credentials()
    {
        var receipt = Receipt() with { Extensions = new() { ["future"] = JsonSerializer.SerializeToElement(new { audit = "own synthetic" }) } };
        using var bytes = new MemoryStream(); await ModReceiptDocuments.WriteAsync(bytes, receipt); bytes.Position = 0; var copy = await ModReceiptDocuments.ReadAsync(bytes);
        Assert.Equal("own synthetic", copy.Extensions!["future"].GetProperty("audit").GetString()); Assert.Equal(receipt.PackageId, copy.PackageId);
    }
    [Theory] [InlineData("missing-version")] [InlineData("wrong-version")] [InlineData("hash")] [InlineData("origin")] [InlineData("namespace")] [InlineData("duplicate")]
    public async Task Receipt_original_json_and_semantics_are_checked(string variant)
    {
        var json = JsonSerializer.Serialize(Receipt(), ContentJson.Options);
        json = variant switch
        {
            "missing-version" => json.Replace("\"schemaVersion\": \"1.0.0\",", ""),
            "wrong-version" => json.Replace("\"schemaVersion\": \"1.0.0\"", "\"schemaVersion\": \"2.0.0\""),
            "hash" => json.Replace(new string('a', 64), "invalid"),
            "origin" => json.Replace("\"provider\": \"local\"", "\"provider\": \"modio\""),
            "namespace" => json.Replace("\"namespace\": \"author.example\"", "\"namespace\": \"other\""),
            _ => json.Replace("\"schemaVersion\": \"1.0.0\"", "\"schemaVersion\": \"1.0.0\", \"schemaVersion\": \"1.0.0\"")
        };
        using var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)); if (variant == "duplicate") await Assert.ThrowsAsync<JsonException>(() => ModReceiptDocuments.ReadAsync(bytes)); else await Assert.ThrowsAsync<InvalidDataException>(() => ModReceiptDocuments.ReadAsync(bytes));
    }
    private static ModInstallReceipt Receipt() => new() { PackageId = "author.example:mod", Namespace = "author.example", Version = "1.0.0", ArchiveSha256 = new('a', 64), Origin = new() { Provider = "local" } };
}
