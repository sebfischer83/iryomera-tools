# ContentStudio, Wikidata and diagnostic contract verification

Verification date: 2026-10-16. Library version 0.4.0; public SDK/importer schemaVersion remains 1.0.0 with documented additive capability changes. Source/code decisions: [ADRs 0007–0009](architecture/0007-content-studio.md), [diagnostic format reference](diagnostic-content.md), [Wikidata profile](wikidata-import.md) and [Studio guide](content-studio.md).

## Scope and architecture

Content.SDK keeps its thirteen definition variants. Optional observation result descriptors, standalone sourced reference intervals/recommendations, independent output/resource/prerequisite relations and explicitly separate simulation configuration extend the existing contracts. Old valid examples and neutral LOINC observations remain valid. There is no patient simulator, seeded random executor, live observation cache, diagnostic AI or dependency on the private game. ADR 0008 plans those private-runtime responsibilities and consistency rules.

The importer adds an offline Wikidata adapter (1.2.0), preserving actual en/de entity metadata, explicit identifiers, statement ranks/qualifiers/references and raw bindings in separately reviewed source assertions. Existing seven adapters retain 1.1.0 and their pinned full-ontology normalization hashes. Source rights and clinical reviews remain independent; deprecated/incomplete/unreferenced statements do not enter controlled bulk approval. Label approval does not copy unapproved statement rows through entity source claims. No real Wikidata medical query/download was performed.

The new net10 Studio host references SDK, Localization and importer application APIs. EF Core/SQLite persistence uses actual migrations, short-lived contexts, optimistic revisions, atomic package/index/audit writes and immutable original imports. Tests exercise edit rollback, review invalidation, rights/revocation, reference-closed bilingual approved exports, scoped source proof and independent-language Wikidata aliases. Advanced clinical contracts currently use validated JSON panels; ordinary definitions/relationships/balancing have forms. ModKit/ModIo remain unimplemented.

## Local results (Debian 13, .NET SDK 10.0.402, runtime 10.0.13 / 8.0.32)

| Check | Observed result |
| --- | --- |
| Locked restore + full Release build | Success; build 0 warnings, 0 errors |
| Foundation xUnit net8.0 | 126 passed, 0 failed/skipped |
| Foundation xUnit net10.0 | 126 passed, 0 failed/skipped |
| MedicalImporter xUnit net10.0 | 95 passed, 0 failed/skipped |
| ContentStudio SQLite/service/HTTP/schema tests | 17 passed, 0 failed/skipped |
| Total .NET executions | 364 passed; Foundation cases run on two runtimes |
| Python acquisition-boundary tests | 4 passed |
| Schema/example regeneration + whitespace checks | Success |
| Foundation DE/EN sample + real importer CLI example | Success, only own synthetic fixtures |
| NuGet packaging | SDK, Localization and importer 0.4.0 produced; Studio deliberately has no NuGet package (solution pack reports that skip) |
| Real Godot 4.7.2 .NET / actual net8 runtime | Success with packaged 0.4.0 assets: annotations, diagnostic contracts and DE/EN catalogs |
| Studio real Chromium browser (Playwright 1.62.0) | Success: interactive DE/EN edit, SQLite, SVG relationship selection, explicit approval/rights and JSON ZIP download; external app requests blocked |
| Standalone published Studio browser test | Same flow passed from publish output, including local assets and copied synthetic samples |

All original tests were retained. Added cases include schema-required versions, typed units/categories/context, source evidence, invalid ranges/probabilities, missing diagnostic references, incorrect resource roles, prerequisite cycles, mod dependencies and localization of new category/rationale keys. The diagnostic fixture defines three fictional diseases and five shared examinations with numeric, categorical, boolean and text results. No real reference values or medical approval are claimed by its synthetic evidence.

The CI workflow runs locked restore, generated schemas/examples, all test targets, samples, published Studio browser flow, packages and real Godot on both Windows and Linux. Browser/runtime setup needs network; the tested app and imports operate offline after setup. GitHub run links/results are recorded below once completed; local Linux results alone do not establish a Windows pass.

## Pinned full official ontology regression (local)

Already privately acquired official HPO/MAxO ontology files were reused; no restricted/account-based source or production annotation file was downloaded. Each input was imported twice and all pinned input/normalized hashes, counts and expected-error baselines matched. Times and peak process working set are measurements on this host, not capacity guarantees.

| Input | Terms | Assertions | Error records | First/repeat import seconds | Peak working set MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| HPO JSON 2026-09-01 | 20,482 | 25,087 | 651 | 1.781 / 0.456 | 390.2 |
| HPO OBO 2026-09-01 | 20,482 | 25,087 | 651 | 2.228 / 0.970 | 311.1 |
| MAxO JSON 2026-01-15 | 2,003 | 16,082 | 13,390 | 0.746 / 0.355 | 252.5 |
| MAxO OBO 2026-01-15 | 2,003 | 3,507 | 815 | 0.404 / 0.296 | 202.6 |

MAxO JSON reports multiple issues for some records (25,970 error diagnostics); error-record counts are not diagnostic counts. See [the full-dataset methodology/limitations](full-dataset-tests.md). Matching expected unresolved references/duplicate-source assertions is a reproducibility pass, not a clinically or semantically releasable dataset. Raw/normalized full datasets stay outside version control; CI artifacts contain aggregate reports only.

## Open issues

- Full ICD-10-GM/OPS/LOINC verification needs legitimately provided exact-release inputs and applicable terms. Source-specific restrictions, unresolved foreign references and actual clinical review remain open. Synthetic tests grant no rights or medical authority.
- Private-game decisions remain: pinned PRNG/version, patient state/time transitions, multi-disease composition, overlapping context precedence, controlled patient-factor vocabularies, evidence-backed clinical ranges/unit conversion and AI utility scoring. Public contracts do not implement or pretend to validate those runtime choices.
- Studio is a trusted local operator tool, without authenticated multi-user approval, large-ontology capacity guarantees, automatic conflict resolution or installed dependency-package management. Raw source parsing stays in the CLI; workspaces are limited to 32 MiB. Richer diagnostic workflow/translation widgets remain planned.
- Wikidata support covers the documented saved SELECT profile, not arbitrary entity dumps, exhaustive RDF semantics, source truth verification, revision reconciliation or live SPARQL download. Partial query results may omit labels/evidence; nothing is invented to fill them.
- Published binaries must retain original dependency/data notices. NuGet packages are generated locally/CI and are not claimed to be published on nuget.org. No data-mod executor, secure ModKit ZIP loader or mod.io client is included.

## GitHub Actions

Pending final branch push and completion of the Windows/Linux workflow. This section will link the observed runs rather than infer success from configuration.
