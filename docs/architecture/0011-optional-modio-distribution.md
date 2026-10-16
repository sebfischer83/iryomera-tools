# ADR 0011 — portable optional mod.io distribution and explicit installation

Status: accepted for Phase 5. English documentation is authoritative.

## Boundaries

`Iryomera.ModIo` targets net8.0/net10.0 and references the portable Content SDK, not Godot, Studio, SQLite or ModKit. API client, download manager, dependency/update services and distribution-neutral source/installer/runtime contracts are separate types. A local archive source works without any mod.io client/configuration. Runtime orchestrates verified package delivery and installation, never loads foreign code or simulates patients.

`Iryomera.ModIo.ModKit` is an optional net10 integration package depending on both ModIo and ModKit. Its installer and publisher reuse existing complete archive/content/localization validation. ModKit remains independent of the distribution provider. The net8 game can use the portable REST/download/runtime API with its own `IModInstaller`; the net10 bridge is a Tools integration, not advertised as a net8 game binary. This avoids weakening the already tested ZIP policy or duplicating it in the REST client.

Remote dependency graphs use numeric mod.io game/mod identities, while SDK package dependencies use namespaced IDs and version ranges. Neither is inferred from labels or from remote version strings. Resolve remote edges in bounded, cycle-checked dependency-first order; validate actual downloaded SDK manifests and explicit local dependency packages at installation. File identity changes indicate an available update, including rollbacks; do not compare arbitrary remote release labels as SemVer. Installation targets are explicit new directories; old installations remain intact for host-managed activation/rollback. There is no automatic background update or batch activation transaction.

## HTTP, credentials and integrity

Use official API v1 endpoints, URL-encoded forms and multipart upload field names. Caller-provided API roots must be HTTPS. Authentication credentials are supplied in memory, never stored in receipts. API keys use the documented `api_key` query parameter and tokens the Bearer header. Remove HttpClientFactory's default request logging, sanitize HTTP/JSON exceptions, redact secret-bearing objects and never attach API credentials to download/CDN requests. Custom host logging/handlers remain the application's responsibility.

Honor 429 Retry-After by locally blocking subsequent requests conservatively until expiry; expose structured status/error reference and retry delay without echoing response bodies/URLs. Do not silently retry mutations. Enforce response/download sizes and cancellation. Download redirects are handled manually with a bounded hop count and explicit HTTPS host policy; never permit arbitrary redirect hosts, embedded credentials or HTTP downgrade. Signed download URLs stay transient and are not receipts/log messages.

Official mod.io downloads expose MD5, not SHA-256. Check that MD5 and exact size, compute local SHA-256 and apply full ModKit inventory/hash/content validation before installation. These checks are integrity checks, not publisher authentication, medical review, a license grant or an antivirus certification. Publication through the bridge validates a captured archive and matching content version before handing its stream to the REST client; the low-level REST client alone is not a clinical/content validator.

## Contracts and evidence

Content SDK/ModKit v1 remain unchanged. Versioned ModIo installation receipts are offline exchange records with stable package identity, local archive SHA-256 and optional remote origin IDs. Original JSON must pass the bundled schema before C# defaults; extension fields survive. No upstream API response is relabeled as an Iryomera schema.

Official documentation inspected 2026-10-16: [REST API reference](https://docs.mod.io/restapiref/), [introduction](https://docs.mod.io/restapi/introduction), [filtering](https://docs.mod.io/restapi/filtering) and [official OpenAPI](https://api.mod.io/v1/swagger), SHA-256 `6a2e1c8a3b568ac220a69b5d44b2eaa755baae9d889b8ff282bf8972a0e8aa8e`. Do not commit the full third-party specification. The OpenAPI email-exchange required list currently names `email` although the published example/body documents `security_code`; implement the documented security-code exchange, cover it with request-shape tests and call out this upstream inconsistency. Publishing hosts must display/link [mod.io terms](https://mod.io/terms) where users submit content; the library does not record user consent or auto-accept terms.
