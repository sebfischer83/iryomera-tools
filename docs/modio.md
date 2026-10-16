# Optional mod.io distribution

Phase 5 provides an optional REST v1 integration. Local data mods do not need a mod.io account, API key, subscription or network. The game remains private; these libraries have no Godot/game project dependency. English documentation is authoritative. See [ADR 0011](architecture/0011-optional-modio-distribution.md), [the plan](plans/phase-5.md) and [verification](phase-5-verification.md).

## Projects and dependency direction

| Package/project | Target | Responsibility |
| --- | --- | --- |
| `Iryomera.ModIo` | net8.0 / net10.0 | REST client, transient credentials, download manager, remote dependency/update services, neutral source/installer/runtime interfaces and receipts |
| `Iryomera.ModKit.Core` | net8.0 / net10.0 | Existing ModKit authoring/validation/archive APIs extracted from the executable; same `Iryomera.ModKit` namespaces |
| `Iryomera.ModIo.ModKit` | net8.0 / net10.0 | Optional complete ModKit installer and validated release publisher |
| `Iryomera.ModKit` | net10.0 tool | Existing `iryomera-mod` CLI over Core; independent of ModIo |
| `examples/Iryomera.ModIo.Sample` | net10.0 | Executable synthetic local/remote installation using in-memory HTTP only |

A NuGet tool package cannot serve as a regular library dependency. The Core extraction fixes that packaging boundary and retains API namespaces/source behavior; consumers referencing the old executable assembly directly must rebuild against Core. Existing SDK and mod ZIP wire formats remain unchanged. Install normal library packages with `dotnet add package Iryomera.ModIo.ModKit --version 0.6.0` after publication, or reference the projects/use locally packed `artifacts` as a NuGet source. No package is automatically published to nuget.org.

## Offline example and tests

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build
dotnet run --project examples/Iryomera.ModIo.Sample -c Release --no-build
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
python scripts/run-packed-modkit-smoke.py
python scripts/run-godot-smoke.py
```

The sample generates its own synthetic MIT mod, mocks API/download HTTP in memory, installs equivalent local and remote archives, validates a receipt and detects a changed file ID. It never opens a socket or sends an email, subscription or publication. Normal CI uses only mock HTTP for mod.io; the separate real-engine runner downloads the pinned official Godot build and uses packed portable libraries, offline HTTP and actual local package installation inside .NET 8.

## Client configuration and authentication

```csharp
using Iryomera.ModIo;
using Iryomera.ModIo.ModKit;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddIryomeraModIo(new ModIoOptions { GameId = configuredGameId });
services.AddIryomeraModKitInstallation(); // optional; local runtime also works without AddIryomeraModIo
services.AddIryomeraModIoPublishing(); // optional publisher; requires the configured REST client
using var host = services.BuildServiceProvider();
var credentials = host.GetRequiredService<InMemoryModIoCredentials>();
credentials.Set(apiKeyFromHostSecretStore);
var api = host.GetRequiredService<IModIoClient>();
// These calls are real operations: invoke only on an explicit user action/configuration.
await api.RequestEmailCodeAsync(email, cancellationToken);
var token = await api.ExchangeEmailCodeAsync(codeEnteredByUser, cancellationToken);
credentials.Set(apiKeyFromHostSecretStore, token);
var page = await api.SearchAsync(new ModSearch(Text: "example", Limit: 25), cancellationToken);
```

Use a real positive game ID and host-supplied credentials. Default API root is `https://api.mod.io/v1/`; a configured per-game HTTPS root ending `/v1/` is also supported. An API key supports read-only requests and the documented email flow. User-token writes require an unexpired token. Exchange returns a token without storing it implicitly; the host decides whether to retain it in memory or a platform secret store. No disk token store, refresh flow, social/platform login, UI or implicit login is provided. Expose the applicable mod.io terms/privacy/consent flow in the host; this library does not auto-accept terms. The upstream OpenAPI email-exchange required list conflicts with its published security-code example; request-shape tests follow the published `security_code` exchange.

API keys use the documented query parameter; tokens use Bearer headers and are never copied to downloads. DI removes default HttpClientFactory request loggers and disables transport redirects/cookies. Credentials/access-token diagnostic text is redacted; secret fields are excluded from serialization. API/transport exceptions contain generic text, HTTP status, numeric error reference and optional retry delay, never raw server bodies or signed URLs. Do not log request URIs/bodies, explicit secret accessors, token results or upstream download DTOs. Custom logging/HTTP handlers are the host's responsibility. When supplying HttpClient manually, disable automatic redirects and cookies; use a dedicated credential-free client for downloads.

On 429, `ModIoApiException.RetryAfter` reports Retry-After (60-second conservative fallback). The client blocks subsequent requests until that deadline; a shared DI singleton preserves the block across services. It conservatively blocks all endpoints for endpoint-specific limits too. There are no silent retries, especially for writes. Cancellation is propagated with sanitized messages; callers decide whether/when to request again after a failure.

## Endpoints and supported operations

| API method | Official v1 operation |
| --- | --- |
| `RequestEmailCodeAsync` / `ExchangeEmailCodeAsync` | POST `oauth/emailrequest` (`email`) / `oauth/emailexchange` (`security_code`); URL-encoded forms with API key |
| `SearchAsync` | GET `games/{game}/mods`; `_q`, `_limit` 1–100 and `_offset` |
| `GetModAsync` / `GetFileAsync` | GET mod / `files/{file}`; positive IDs and response identity checks |
| `SubscribeAsync` / `UnsubscribeAsync` | POST / DELETE `games/{game}/mods/{mod}/subscribe`; explicit `include_dependencies` flag |
| `GetDependenciesAsync` | GET `dependencies?recursive=false` with paging |
| `PublishAsync` | POST `games/{game}/mods`; multipart name/summary/description/logo, optional tags and `metadata_kvp[]=iryomera_schema:1.0.0` |
| `UploadAsync` | POST `games/{game}/mods/{mod}/files`; multipart filedata/version/changelog/active/filehash |

The library implements the single-file upload route within its default 128 MiB limit, below the upstream 500 MB route ceiling. Multipart upload sessions for larger files, marketplace/paid content, moderation and platform-specific file selection are not implemented. Publication hosts must link [mod.io terms](https://mod.io/terms) wherever users submit content. New listings require a host-supplied suitable PNG/JPEG logo; mod.io additionally enforces its own media size/content and account/game permissions.

The low-level REST client transports upstream data and is not an Iryomera archive validator. Prefer `ModIoPublisher.UploadAsync` from the optional bridge: it captures private bounded bytes, validates the complete existing archive/dependency/localization/asset policy and checks the release version against the package manifest before any upload request. New listing creation and release upload are explicit separate operations; no automatic destructive rollback/delete is attempted on a failed upload. Stream arguments to low-level publication remain caller-owned and open; do not modify them during the operation.

## Dependencies, updates and downloads

`ModDependencyResolver.ResolveAsync(modId)` walks direct, paginated edges and returns dependency-first order including the requested root. Shared nodes are emitted once. Cycles, duplicate pagination entries, inconsistent offsets/counts, cross-game identities and configured graph/depth/page limits fail. Defaults: 256 nodes, depth 64, 100 pages per node. This is a remote numeric-ID plan, not a mapping to SDK IDs or approval to install every result. At installation, actual package identity, namespaced SDK dependencies, versions and resources are validated independently with explicit dependency packages. No dependency is matched by display name or downloaded in the background.

`ModUpdateService.CheckAsync(modId, installedFileId)` compares current file identity, including rollbacks. Arbitrary mod.io `version` labels are display metadata, not trusted SemVer. Re-fetch the current file immediately before delivery: signed `binary_url` links expire. `ModIoSource` does so through details/current-file endpoints. Hosts select the desired platform and activation/rollback policy; no live automatic updater or game loader is provided.

`ModDownloadManager.DownloadAsync(file, output, expectedSha256?, ct)` enforces a fresh HTTPS URL, positive IDs, nonempty exact length, configured maximum bytes, valid MD5 and optional independently supplied SHA-256 pin. It computes local SHA-256 too. Default download hosts are `api.mod.io`, `*.modapi.io`, `*.modcdn.io`; configure additional exact approved hosts if your deployment's official CDN uses others. Every manual redirect is checked against the same policy; HTTP downgrade, userinfo, fragments and excessive redirects fail. Downloads use a separate credential-free client. Default maximum: 128 MiB; redirects: 5; API JSON: 8 MiB. No resume/range or automatic retry is implemented.

Downloads go to a fresh sibling temporary file and move atomically only after successful size/hash checks. Existing destinations and linked output ancestors are refused; failures/cancellation remove temporary files. MD5 is the upstream integrity field, not a secure publisher signature. SHA-256/index verification and complete ModKit validation likewise do not establish publisher identity, antivirus status, medical approval or license permission. A hostile same-privilege concurrent filesystem writer is outside the local tool guarantee.

## Explicit local or remote installation

```csharp
var runtime = new ModRuntime(new ModKitInstaller());
var local = new LocalArchiveSource(new Dictionary<string, string> {
    ["my-mod"] = "/absolute/path/to/my-mod.zip"
});
var installed = await runtime.InstallAsync(local, "my-mod", newDestination,
    dependencies: installedDependencyPackages, gameVersion: currentGameVersion, ct: cancellationToken);
// Remote alternative after explicit configuration:
var remoteSource = host.GetRequiredService<ModIoSource>();
var remote = await runtime.InstallAsync(remoteSource, "123", anotherNewDestination,
    dependencies: installedDependencyPackages, gameVersion: currentGameVersion, ct: cancellationToken);
```

`IModSource` delivers verified bytes; `IModInstaller` validates and publishes; `ModRuntime` orchestrates private temporary delivery and cleanup. A local source resolves only explicit registered IDs, not untrusted filesystem paths. ModKitInstaller rechecks the delivered SHA-256/length, inspects the archive and validates its receipt before atomically unpacking to a new destination. It never overwrites or activates an existing installation. Callers can persist the returned host-owned receipt separately using `ModReceiptDocuments.WriteAsync` and compare stable package identity against a previously installed mod before selecting an update for activation. Keep receipts outside the mod folder so the exact archive inventory stays valid. Multi-mod activation, an installed-mod registry, automatic provider-to-SDK identity mapping and batch transactions remain host/future work.

## Receipt format and compatibility

`schemas/modio/v1` contains offline draft 2020-12 `modInstallReceipt`/`modOrigin` schemas. Required receipt fields: `schemaVersion: "1.0.0"`, `packageId`, `namespace`, canonical SemVer `version`, lowercase 64-digit `archiveSha256`, `origin`. Package ID must belong to its namespace. Origin is `local` without remote IDs, or `modio` with positive `gameId`, `modId`, `fileId`. Unknown extension metadata survives. No token, API key, signed URL or machine-specific path is generated into the receipt. `ModReceiptDocuments.ReadAsync` checks original bounded JSON against the embedded schema before C# defaults, rejects duplicate properties and applies namespace/version checks.

The upstream API DTOs use upstream snake_case and preserve unknown response fields. They do not use Iryomera's `schemaVersion`; mod.io owns those contracts. SDK content/localization, importer staging and ModKit v1 schemas remain unchanged. Regenerate receipt schemas with `python scripts/generate-modio-schemas.py`. Own code/tests/sample are MIT; upstream terms and mod content retain their original licenses.
