# Phase 1 — implementation plan

## Projects and dependencies

- `Iryomera.Content.SDK` (net10.0, NuGet): data contracts, System.Text.Json, validation; NuGet.Versioning for semantic versions and dependency ranges. No engine, UI, database, distribution or localization runtime dependency.
- `Iryomera.Localization` (net10.0, NuGet): SDK language-file contract, SmartFormat for named variables/plurals, Microsoft.Extensions.Localization.Abstractions for an IStringLocalizer adapter.
- `Iryomera.Foundation.Tests`: xUnit, Microsoft.NET.Test.Sdk, JsonSchema.Net for independent contract/example verification.
- `Iryomera.Foundation.Sample`: offline console demonstrating JSON loading, reference validation, and English/German formatting.

## Work packages, in execution order

1. Preserve existing MIT license; add SDK pin, central package management, build/editor conventions and architecture rules.
2. Implement all requested definitions, independent typed relations, provenance/review metadata, balancing, manifests and localization contracts. Use explicit v1 envelopes and polymorphic `kind` discriminators.
3. Implement asynchronous JSON I/O, extensibility and graph/dependency/duplicate/type validation. Validate against an explicit set of installed packages without network access.
4. Implement English fallback, culture-aware variables and plural forms, import/export, missing/unused keys and cross-language placeholder diagnostics, and .NET localization adapter.
5. Publish versioned draft 2020-12 schemas, synthetic referentially consistent content and separate de/en files; document formats and Godot integration boundary.
6. Test valid and invalid documents, schema parity, dependencies, provenance, relation endpoints, localization and actual async round trips; run Release build/test/pack and executable sample.
7. Add Windows/Linux CI, contribution guide and detailed Phase 2 plan. Report only verified results; leave all later tools explicitly planned.

## Acceptance

No real third-party medical data, invented clinical probabilities, cloud runtime dependencies, empty future tool projects, or unverified completion claims. Phase 1 is complete only after compilation, tests and runnable examples succeed.
