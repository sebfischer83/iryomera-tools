# Phase 1 verification report

Updated 2026-10-16 after the external Phase 1 review. Historical evidence and the remediation revision are recorded separately; local Linux results are not described as Windows results.

## Original Foundation 0.1.0: verified GitHub Actions evidence

Commit: [`5646c93581ad21c0e5c2a49f5015af647b83e22c`](https://github.com/sebfischer83/iryomera-tools/commit/5646c93581ad21c0e5c2a49f5015af647b83e22c).

[Foundation CI run 38923703189](https://github.com/sebfischer83/iryomera-tools/actions/runs/38923703189) completed successfully. Job status/steps were checked through the GitHub API during this review:

| Platform | Verified result | Job |
| --- | --- | --- |
| Windows (`windows-latest`) | Success; 2026-10-16 15:24:42–15:26:01 UTC | [Windows job 117049764127](https://github.com/sebfischer83/iryomera-tools/actions/runs/38923703189/job/117049764127) |
| Linux (`ubuntu-latest`) | Success; 2026-10-16 15:24:42–15:25:08 UTC | [Linux job 117049764538](https://github.com/sebfischer83/iryomera-tools/actions/runs/38923703189/job/117049764538) |

That revision built net10.0, passed 73 xUnit cases per operating system, ran the synthetic console example and packed both 0.1.0 libraries. The original sample had 22 definitions and 22 relations; the SDK package had 24 schema files. It did **not** prove Godot binary compatibility or lossless HPO/MAxO annotation handling. The earlier statement that Windows CI was unobserved is superseded by the linked completed run.

## Review remediation 0.2.0

This section describes the specific historical Phase 1 review revision. Phase 2 is now integrated separately; its changes/evidence are in [the Phase 2 report](phase-2-verification.md).

Scope: Foundation only, based on the published Phase 1 commit. The previously prepared but unpublished importer was preserved on a separate local `archive/phase-2-local` branch. It is not in this branch, solution, CI or release packages.

Changes under verification:

- Lossless standalone `MedicalAnnotation`, `AnnotationReference` and `ObservationFrequency` contracts, optional package collection, full source fields/provenance and synthetic bilingual examples. No source import adapters or clinical projections.
- JsonRequired schemaVersion on every version-bearing exchange object. Defaults remain convenient for C# construction; missing wire fields fail explicitly.
- Shared offline schema-before-deserialization and semantic validation through ContentDocuments, with LocalizationDocuments composing catalog checks.
- SDK/Localization net8.0/net10.0 package assets and both-runtime tests; actual packed-library Godot 4.7.2 .NET desktop smoke test.
- Verified current official action releases: checkout 7.0.1, setup-dotnet 6.0.0, setup-python 7.0.0 and upload-artifact 7.0.2. CI provisions .NET 8 and 10, runs schema generation/locked restore/build/tests/example/pack/Godot integration on Windows and Linux.

Local Linux build/validation: .NET SDK 10.0.402 with .NET 8.0.32 runtime; Release compilation has no warnings/errors. **104 tests per framework** (208 executions locally, zero failures/skips), library package contents and the real headless Godot game process are checked. The Godot scene asserts actual .NET 8 execution, net8 package asset selection, schema/semantic loading, annotation round trip and English/German/plural rendering. Editor import is separately checked for errors with the .NET 10 SDK environment.

### Completed review CI evidence

Tested implementation commit: [`43b56a97f5077ca50d6009ae1bfdc9ee10a425d2`](https://github.com/sebfischer83/iryomera-tools/commit/43b56a97f5077ca50d6009ae1bfdc9ee10a425d2), branch `fix/phase-1-review`.

[Foundation CI run 38930859417](https://github.com/sebfischer83/iryomera-tools/actions/runs/38930859417) completed **successfully** on 2026-10-16. Results were checked from both the GitHub jobs API and executed job logs, including the test summaries and Godot success markers.

| Platform | net8.0 tests | net10.0 tests | Real Godot .NET 8 game | Full job |
| --- | --- | --- | --- | --- |
| Linux | 104 passed, 0 failed/skipped | 104 passed, 0 failed/skipped | Success | [Job 117074174184](https://github.com/sebfischer83/iryomera-tools/actions/runs/38930859417/job/117074174184), 16:18:10–16:19:11 UTC |
| Windows | 104 passed, 0 failed/skipped | 104 passed, 0 failed/skipped | Success | [Job 117074174546](https://github.com/sebfischer83/iryomera-tools/actions/runs/38930859417/job/117074174546), 16:18:10–16:20:33 UTC |

**416 xUnit executions** across both frameworks/operating systems succeeded, plus two actual Godot engine integration runs. Both full jobs also passed generated-schema verification, locked restore, solution build, console example, package creation and artifact upload. Build output reports zero warnings/errors. Both 0.2.0 library packages contain net8.0/net10.0 DLL/XML assets; the SDK includes 27 schema files and the embedded bundle.

This report update changes documentation only; the linked implementation commit identifies the tested code without a circular claim that a CI run tested a report containing its own future URL.

## Reproduce the complete process

Install .NET 10 SDK, .NET 8 runtime/SDK and Python 3:

```sh
python scripts/generate-schemas.py
git diff --exit-code -- schemas
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build --logger trx
dotnet run --project examples/Iryomera.Foundation.Sample -c Release --no-build
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
python scripts/run-godot-smoke.py
```

The managed local workspace uses SDK/CLI/NuGet paths under /tmp and single-node builds with shared compilation disabled; these are environment accommodations, not application dependencies. Prerequisite/package/engine setup uses the network. The libraries, bundled schema evaluation, tests and content/localization operations are offline.

## Limits and remaining work

- No real medical data, clinical approval, importer, editor, SQLite, mod archives or mod.io integration is part of this remediation. Real source license/release review belongs to Phase 2 before implementing imports.
- Observational frequency is preserved exactly, not interpreted as game probability. Negation/qualifiers and MAxO context are never flattened into positive binary medical assertions.
- Existing valid v1 JSON remains compatible. Missing required versions were malformed under the published schema already; accepting them through C# defaults was a bug.
- Binary compatibility is verified for pinned Godot 4.7.2 .NET Windows/Linux desktop with net8.0 assets. Other engine/framework/export combinations, trimming and NativeAOT need separate verification.
- Packages are local/CI artifacts, not automatically published to NuGet. Schema URIs are identifiers resolved from the embedded/bundled schema, not an online service.
- Semantic graph checks require a ContentPackage/dependency closure. Standalone leaf schemas cannot prove reference closure. Object-only validation cannot reconstruct a wire field discarded before validation.

Decisions: [medical annotations](architecture/0003-medical-annotations.md), [runtime/multi-targeting](architecture/0004-godot-runtime.md). Usage: [formats](formats.md), [Godot integration](godot-integration.md), [review work plan](plans/foundation-review.md).
