using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Iryomera.ModIo.Tests;

public sealed class ApiTests
{
    [Fact] public async Task Search_encodes_filters_and_key_and_keeps_additive_metadata()
    {
        using var handler = new TestHttp((r, _) =>
        {
            Assert.Equal(HttpMethod.Get, r.Method); Assert.Contains("_q=heart%20%26%20lung", r.RequestUri!.OriginalString);
            Assert.Contains("_limit=25&_offset=10", r.RequestUri.OriginalString); Assert.Contains("api_key=synthetic-api-key", r.RequestUri.Query); Assert.Null(r.Headers.Authorization);
            return Task.FromResult(TestHttp.Json(new { data = new[] { new { id = 3, game_id = 7, name = "Synthetic", future = true } }, result_count = 1, result_offset = 10, result_limit = 25, result_total = 11 }));
        });
        var page = await handler.Client().SearchAsync(new("heart & lung", 25, 10)); Assert.True(page.Data[0].Extensions!["future"].GetBoolean());
    }
    [Fact] public async Task Email_auth_uses_official_form_and_key_never_stores_token_implicitly()
    {
        var credentials = TestHttp.Credentials();
        using var handler = new TestHttp(async (r, ct) =>
        {
            Assert.Null(r.Headers.Authorization); Assert.Contains("api_key=synthetic-api-key", r.RequestUri!.Query);
            Assert.Equal("application/x-www-form-urlencoded", r.Content!.Headers.ContentType!.MediaType);
            var body = await r.Content.ReadAsStringAsync(ct);
            if (r.RequestUri.AbsolutePath.EndsWith("emailrequest")) { Assert.Equal("email=own%2Bfixture%40example.invalid", body); return TestHttp.Json(new { code = 200 }); }
            Assert.Equal("security_code=ABCDE", body); return TestHttp.Json(new { access_token = "returned-synthetic-token", date_expires = 2000000000 });
        });
        var client = handler.Client(credentials: credentials); await client.RequestEmailCodeAsync("own+fixture@example.invalid"); var token = await client.ExchangeEmailCodeAsync("ABCDE");
        Assert.Equal("returned-synthetic-token", token.Value); Assert.DoesNotContain(token.Value, token.ToString()); Assert.DoesNotContain(token.Value, JsonSerializer.Serialize(token));
        Assert.Null((await credentials.GetAsync()).Token);
    }
    [Fact] public async Task Authenticated_subscription_and_unsubscription_use_token_not_query_key()
    {
        using var handler = new TestHttp(async (r, ct) =>
        {
            Assert.Equal("Bearer", r.Headers.Authorization!.Scheme); Assert.Equal("synthetic-token", r.Headers.Authorization.Parameter); Assert.DoesNotContain("api_key", r.RequestUri!.Query);
            Assert.EndsWith("/games/7/mods/3/subscribe", r.RequestUri.AbsolutePath);
            if (r.Method == HttpMethod.Post) Assert.Equal("include_dependencies=true", await r.Content!.ReadAsStringAsync(ct));
            else Assert.Equal(HttpMethod.Delete, r.Method);
            return new(HttpStatusCode.NoContent);
        });
        var client = handler.Client(credentials: TestHttp.Credentials(true)); await client.SubscribeAsync(3, true); await client.UnsubscribeAsync(3); Assert.Equal(2, handler.Calls);
    }
    [Fact] public async Task Writes_reject_missing_and_expired_tokens_before_network()
    {
        using var handler = new TestHttp((_, _) => throw new Exception("No network expected"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Client().SubscribeAsync(3));
        var credentials = TestHttp.Credentials(); credentials.Set("key", new("expired", DateTimeOffset.UtcNow.AddMinutes(-1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Client(credentials: credentials).UnsubscribeAsync(3)); Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task Api_exceptions_do_not_echo_server_secrets_or_uris()
    {
        using var handler = new TestHttp((_, _) => Task.FromResult(TestHttp.Json(new { error = new { error_ref = 11006, message = "synthetic-api-key synthetic-token secret-URL" } }, HttpStatusCode.Forbidden)));
        var error = await Assert.ThrowsAsync<ModIoApiException>(() => handler.Client().GetModAsync(3)); Assert.Equal(403, error.StatusCode); Assert.Equal(11006, error.ErrorReference);
        Assert.DoesNotContain("synthetic-", error.ToString()); Assert.Null(error.InnerException);
    }
    [Fact] public async Task Transport_failures_are_sanitized()
    {
        using var handler = new TestHttp((_, _) => throw new HttpRequestException("https://example.invalid?api_key=synthetic-api-key"));
        var error = await Assert.ThrowsAsync<ModIoTransportException>(() => handler.Client().GetModAsync(3)); Assert.DoesNotContain("synthetic-api-key", error.ToString()); Assert.Null(error.InnerException);
    }
    [Fact] public async Task Rate_limit_blocks_until_retry_after_without_retrying_mutation()
    {
        var clock = new MutableClock();
        using var handler = new TestHttp((_, _) =>
        {
            var response = TestHttp.Json(new { error = new { error_ref = 11009 } }, HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30)); return Task.FromResult(response);
        });
        var client = handler.Client(credentials: TestHttp.Credentials(true), clock: clock);
        var first = await Assert.ThrowsAsync<ModIoApiException>(() => client.SubscribeAsync(3)); Assert.Equal(TimeSpan.FromSeconds(30), first.RetryAfter);
        await Assert.ThrowsAsync<ModIoApiException>(() => client.SubscribeAsync(3)); Assert.Equal(1, handler.Calls);
        clock.Now += TimeSpan.FromSeconds(31); await Assert.ThrowsAsync<ModIoApiException>(() => client.SubscribeAsync(3)); Assert.Equal(2, handler.Calls);
    }
    [Theory] [InlineData("{}")] [InlineData("not-json")] [InlineData("{\"id\":3,\"id\":3}")] [InlineData("null")]
    public async Task Malformed_responses_fail_safely(string input)
    {
        using var handler = new TestHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(input) }));
        await Assert.ThrowsAsync<ModIoTransportException>(() => handler.Client().GetModAsync(3));
    }
    [Fact] public async Task Bounded_response_and_remote_identity_are_enforced()
    {
        using var oversized = new TestHttp((_, _) => Task.FromResult(TestHttp.Json(TestHttp.Mod(3))));
        await Assert.ThrowsAsync<InvalidDataException>(() => oversized.Client(TestHttp.Options with { MaxResponseBytes = 8 }).GetModAsync(3));
        using var mismatch = new TestHttp((_, _) => Task.FromResult(TestHttp.Json(TestHttp.Mod(4))));
        await Assert.ThrowsAsync<InvalidDataException>(() => mismatch.Client().GetModAsync(3));
    }
    [Fact] public async Task Multipart_listing_and_release_upload_preserve_streams_and_hashes()
    {
        using var handler = new TestHttp(async (r, ct) =>
        {
            Assert.Equal("synthetic-token", r.Headers.Authorization!.Parameter); var body = await r.Content!.ReadAsStringAsync(ct);
            if (r.RequestUri!.AbsolutePath.EndsWith("/files"))
            { Assert.Contains("filedata", body); Assert.Contains("900150983cd24fb0d6963f7d28e17f72", body); Assert.Contains("1.0.0", body); return TestHttp.Json(TestHttp.Mod(3, 12).Modfile!, HttpStatusCode.Created); }
            Assert.Contains("logo", body); Assert.Contains("Synthetic listing", body); Assert.Contains("tags[]", body); return TestHttp.Json(TestHttp.Mod(3), HttpStatusCode.Created);
        });
        var client = handler.Client(credentials: TestHttp.Credentials(true)); using var logo = new MemoryStream([1, 2, 3]); using var archive = new MemoryStream("abc"u8.ToArray());
        Assert.Equal(3, (await client.PublishAsync(new("Synthetic listing", "Summary", "Description", "logo.png", ["example"]), logo)).Id);
        Assert.Equal(12, (await client.UploadAsync(3, archive, "1.0.0", "Synthetic changes")).Id); Assert.True(logo.CanRead); Assert.True(archive.CanRead);
    }
    [Fact] public async Task Cancellation_prevents_requests()
    {
        using var handler = new TestHttp((_, _) => throw new Exception("No network expected")); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.Client().GetModAsync(3, canceled.Token)); Assert.Equal(0, handler.Calls);
    }
    [Fact] public void Options_require_explicit_game_and_safe_https_root()
    {
        using var handler = new TestHttp((_, _) => throw new Exception());
        Assert.Throws<ArgumentOutOfRangeException>(() => handler.Client(TestHttp.Options with { GameId = 0 }));
        foreach (var uri in new[] { "http://api.mod.io/v1/", "https://user:pass@api.mod.io/v1/", "https://api.mod.io/v1/?key=bad", "https://api.mod.io/v1/#fragment" })
            Assert.Throws<ArgumentException>(() => handler.Client(TestHttp.Options with { ApiBaseUri = new(uri) }));
    }
    [Fact] public async Task Dependency_injection_removes_http_request_loggers_and_redacts_credentials()
    {
        var lines = new List<string>(); var services = new ServiceCollection(); services.AddLogging(b => b.AddProvider(new CaptureLogger(lines)));
        services.AddIryomeraModIo(TestHttp.Options);
        services.AddHttpClient("Iryomera.ModIo.Api").ConfigurePrimaryHttpMessageHandler(() => new TestHttp((_, _) => Task.FromResult(TestHttp.Json(TestHttp.Mod(3)))));
        using var provider = services.BuildServiceProvider(); var credentials = provider.GetRequiredService<InMemoryModIoCredentials>(); credentials.Set("synthetic-api-key");
        await provider.GetRequiredService<IModIoClient>().GetModAsync(3); Assert.DoesNotContain(lines, l => l.Contains("synthetic-api-key"));
        Assert.DoesNotContain("synthetic-api-key", JsonSerializer.Serialize(await credentials.GetAsync())); Assert.DoesNotContain("synthetic-api-key", credentials.ToString());
        Assert.NotNull(provider.GetRequiredService<ModIoSource>());
    }
    [Fact] public async Task Retry_after_is_not_shortened_and_cancellation_is_redacted()
    {
        using var rate = new TestHttp((_, _) => { var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new(TimeSpan.FromDays(2)); return Task.FromResult(r); });
        var error = await Assert.ThrowsAsync<ModIoApiException>(() => rate.Client().GetModAsync(3)); Assert.Equal(TimeSpan.FromDays(2), error.RetryAfter);
        using var cancellation = new CancellationTokenSource();
        using var canceled = new TestHttp((_, _) => { cancellation.Cancel(); throw new OperationCanceledException("synthetic-api-key", cancellation.Token); });
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.Client().GetModAsync(3, cancellation.Token)); Assert.DoesNotContain("synthetic-api-key", failure.ToString()); Assert.Null(failure.InnerException);
    }
    private sealed class CaptureLogger(List<string> lines) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this; public void Dispose() { } public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => lines.Add(formatter(state, exception));
    }
}
