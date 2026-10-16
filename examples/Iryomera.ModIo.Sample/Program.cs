using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Iryomera.ModIo;
using Iryomera.ModIo.ModKit;
using Iryomera.ModKit;

// Every HTTP request is handled in memory. No socket, account, email or publication is used.
var root = Path.Combine(Path.GetTempPath(), "iryomera-modio-example-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    var project = Path.Combine(root, "authoring"); var zip = Path.Combine(root, "example.zip");
    await new ModProjects().CreateAsync(project, "offline.demo", "Own synthetic example", "Offline distribution example");
    await new ModArchive().PackAsync(project, zip); var bytes = await File.ReadAllBytesAsync(zip);
    var file = new ModIoFile { Id = 12, ModId = 3, Filesize = bytes.Length, Version = "1.0.0", Filehash = new() { Md5 = Convert.ToHexString(MD5.HashData(bytes)) }, Download = new() { BinaryUrl = new("https://cdn.example.invalid/own-synthetic.zip") } };
    var mod = new ModIoMod { Id = 3, GameId = 7, Name = "Own synthetic mod", Modfile = file };
    var options = new ModIoOptions { GameId = 7, DownloadHosts = ["cdn.example.invalid"] };
    using var apiHttp = new HttpClient(new OfflineHandler(r => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(r.RequestUri!.AbsolutePath.EndsWith("/files/12") ? (object)file : mod, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })) }));
    using var downloadHttp = new HttpClient(new OfflineHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
    var credentials = new InMemoryModIoCredentials(); credentials.Set("own-synthetic-offline-key");
    var client = new ModIoClient(apiHttp, options, credentials); var runtime = new ModRuntime(new ModKitInstaller());
    var remote = await runtime.InstallAsync(new ModIoSource(client, new(downloadHttp, options)), "3", Path.Combine(root, "remote-installed"));
    var local = await runtime.InstallAsync(new LocalArchiveSource(new Dictionary<string, string> { ["example"] = zip }), "example", Path.Combine(root, "local-installed"));
    if (remote.Receipt.ArchiveSha256 != local.Receipt.ArchiveSha256 || !(await new ModUpdateService(client).CheckAsync(3, 11)).IsAvailable) throw new InvalidOperationException("Distribution roundtrip failed.");
    using var receiptBytes = new MemoryStream(); await ModReceiptDocuments.WriteAsync(receiptBytes, remote.Receipt); receiptBytes.Position = 0;
    var receipt = await ModReceiptDocuments.ReadAsync(receiptBytes); if (receipt.Origin.FileId != 12) throw new InvalidOperationException("Receipt identity lost.");
    Console.WriteLine("IRYOMERA_MODIO_SMOKE_OK: offline mock API/download, identical local/remote packages, validated installation, receipts and file-ID update detection.");
}
finally { Directory.Delete(root, true); }

sealed class OfflineHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
}
