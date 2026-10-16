using System.Net;
using System.Text;
using System.Text.Json;
using Iryomera.ModIo;

namespace Iryomera.ModIo.Tests;

internal sealed class TestHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    internal int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { Interlocked.Increment(ref Calls); return send(request, ct); }
    internal static HttpResponseMessage Json(object data, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };
    internal static ModIoOptions Options => new() { GameId = 7, DownloadHosts = ["cdn.example.invalid", "api.mod.io"] };
    internal static InMemoryModIoCredentials Credentials(bool token = false)
    {
        var result = new InMemoryModIoCredentials(); result.Set("synthetic-api-key", token ? new("synthetic-token", DateTimeOffset.UtcNow.AddHours(1)) : null); return result;
    }
    internal ModIoClient Client(ModIoOptions? options = null, IModIoCredentials? credentials = null, TimeProvider? clock = null) =>
        new(new HttpClient(this), options ?? Options, credentials ?? Credentials(), clock);
    internal static ModIoMod Mod(long id, long? fileId = null, string version = "arbitrary release label") => new()
    {
        Id = id, GameId = 7, Name = "Synthetic mod", Modfile = fileId is null ? null : new()
        { Id = fileId.Value, ModId = id, Filesize = 3, Filehash = new() { Md5 = "900150983cd24fb0d6963f7d28e17f72" }, Version = version }
    };
    internal static ModIoPage<ModIoMod> Page(params ModIoMod[] mods) => new() { Data = mods.ToList(), ResultCount = mods.Length, ResultOffset = 0, ResultLimit = 100, ResultTotal = mods.Length };
}
internal sealed class MutableClock : TimeProvider
{
    internal DateTimeOffset Now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
