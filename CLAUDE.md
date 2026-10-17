# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

The binding development rules are in AGENTS.md and apply in full:

@AGENTS.md

## Commands

Requires exactly .NET SDK 10.0.402 (`global.json`, `rollForward: disable`; other SDKs change the implicit `Microsoft.AspNetCore.App.Internal.Assets` version and break `--locked-mode`) plus a .NET 8 runtime, because the SDK, Localization and Foundation tests multi-target `net8.0;net10.0`. Python 3 is used for the generators and scripted smoke tests.

```sh
dotnet restore Iryomera.Tools.sln --locked-mode      # lock files are committed; update them when packages change
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build  # runs both net8.0 and net10.0 targets
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
```

Single project / single test (xUnit):

```sh
dotnet test tests/Iryomera.ModKit.Tests -c Release --filter "FullyQualifiedName~ArchiveTests"
dotnet test tests/Iryomera.Foundation.Tests -c Release -f net10.0 --filter "FullyQualifiedName~SchemaTests"
```

Generated schemas and examples must be regenerated after a contract change. CI runs all of these and fails on `git diff` in `schemas/` or `examples/`:

```sh
python scripts/generate-schemas.py            # schemas/v1 (SDK content format)
python scripts/generate-importer-schemas.py   # schemas/importer/v1
python scripts/generate-studio-schemas.py     # schemas/studio/v1
python scripts/generate-modkit-schemas.py     # schemas/modkit/v1
python scripts/generate-name-importer-schemas.py  # schemas/names/v1
python scripts/generate-diagnostic-example.py
python scripts/generate-modkit-example.py
```

Further checks that CI runs on Windows and Linux:

```sh
python -m unittest discover -s scripts -p "test_*.py"
dotnet run --project examples/Iryomera.Foundation.Sample -c Release --no-build
python scripts/run-importer-example.py
python scripts/run-modkit-example.py
python scripts/run-studio-smoke.py --published-directory artifacts/studio   # after dotnet publish; needs Playwright (scripts/requirements-browser.txt)
python scripts/run-godot-smoke.py                                           # after pack; downloads pinned Godot 4.7.2 .NET
```

Run the tools locally:
- `dotnet run --project src/Iryomera.MedicalImporter -c Release -- <sources|import|validate|status|matches|review|review-bulk|rights|export>` (tool command `iryomera-medical`)
- `dotnet run --project src/Iryomera.ModKit -c Release -- <new|validate|edit|pack|inspect|unpack>` (tool command `iryomera-mod`)
- `dotnet run --project src/Iryomera.NameImporter -c Release -- <sources|import|validate|status|review|rights|export>` (tool command `iryomera-names`)
- `dotnet run --project src/Iryomera.ContentStudio -c Release`, then open http://127.0.0.1:5180. EF migrations run at startup.

Full-dataset tests are opt-in: `python scripts/run-full-dataset-tests.py`. See docs/full-dataset-tests.md.

Build settings in `Directory.Build.props`: `TreatWarningsAsErrors`, nullable, C# 14, deterministic builds, and a single shared `<Version>`. Package versions are managed centrally in `Directory.Packages.props`.

## Architecture

Dependencies point inward to `Iryomera.Content.SDK`:

```
Content.SDK  <-  Localization  <-  ModKit (CLI)
     ^                ^
MedicalImporter (CLI) <- ContentStudio (Blazor Server + SQLite)
```

- **Content.SDK** (net8/net10 NuGet): typed definitions (polymorphic by `kind`), independent relations, MedicalAnnotations, provenance/review, manifests and separate `gameplay` simulation config. `ContentDocuments` validates raw JSON against the bundled JSON Schemas *before* deserialization and then runs semantic validation (`Validation.cs`, `DiagnosticValidator.cs`). The caller supplies the full installed dependency closure; nothing is resolved remotely. Issues carry stable codes and paths.
- **Localization** (net8/net10 NuGet): separate en/de catalogs keyed by localization keys, never by IDs. Uses SmartFormat and an `IStringLocalizer` adapter. Shared alias handling lives here.
- **MedicalImporter**: source adapters (`Adapters/`: OBO/OBO Graph, HPOA/MAxO TSV, ClaML, LOINC CSV, Wikidata SPARQL) produce one strict, versioned JSON *workspace*. Writes take a file lock and replace atomically. Pipeline: `import` (always unreviewed) → `review` / `review-bulk` / `rights` (append-only audit) → `export` (`ReleaseExporter`: approved, reference-closed content plus a `source-assertions.json` sidecar and license notices). Staging v1 rejects unknown properties, unlike the additive SDK format.
- **ContentStudio**: Blazor Interactive Server UI over services that use short-lived contexts from `IDbContextFactory`. SQLite stores full SDK JSON snapshots, a normalized search index, revision tokens and audit rows. It calls the MedicalImporter application APIs for review, rights and export instead of reimplementing them. UI strings are in `Resources/` en/de JSON.
- **NameImporter**: name source adapters (`Adapters/`: US SSA, sigpwned CSV) produce a strict, versioned name workspace (schemas/names/v1) with append-only review and batch rights decisions; `export` writes approved, permitted records as SDK `namePools` plus en/de pool labels and notices. Depends on SDK/Localization only, not on MedicalImporter.
- **ModKit**: data-only mod projects with typed inventories and SHA-256 indexes. `ModArchive` handles bounded streaming ZIP pack/inspect/unpack with staged atomic publication.
- **Godot integration test** (`tests/Iryomera.Godot.Integration`): references the *packed* NuGet packages (`FoundationPackageVersion`), not project references. It runs only through `run-godot-smoke.py`.

JSON Schemas are **not** generated from C#. They are hand-written in the Python generators, so a public contract change means editing the C# model and the matching generator, then the docs (docs/formats.md etc.), examples and tests together.

Read docs/plans and the ADRs in docs/architecture before changing scope. The docs index is docs/README.md. NameImporter (Phase 5, docs/plans/names.md, ADR 0011) is in progress: the tool exists with the first adapters. ModIo (Phase 6) is planned but has no project yet.
