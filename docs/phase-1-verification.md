# Phase 1 verification report

Verified locally on Linux, 2026-10-16, .NET SDK **10.0.402**, target `net10.0`, Release configuration.

| Check | Observed result |
| --- | --- |
| Locked restore | Successful against committed dependency lock files |
| Solution build | Successful, **0 warnings, 0 errors** |
| xUnit | **73 passed, 0 failed, 0 skipped** |
| Console example | Exit 0; **22 definitions / 22 relations** validated; English/German names, plurals and number formats rendered |
| NuGet pack | `Iryomera.Content.SDK.0.1.0.nupkg` and `Iryomera.Localization.0.1.0.nupkg` created locally |
| Package inspection | Both contain net10.0 DLL, XML API docs and README; SDK includes all **24 schema files** |
| Schema/example compatibility | Independent JsonSchema.Net evaluation of all example documents, all twelve definition kinds and nested public formats passed as part of tests |

## Commands

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build --logger trx
dotnet run --project examples/Iryomera.Foundation.Sample -c Release --no-build
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
```

The managed workspace required a local SDK under `/tmp/iryomera-dotnet`, writable CLI/NuGet/XDG directories, single-node builds and disabled shared compilation. Test execution needed local socket permission for the VSTest host. These are execution-environment constraints, not runtime dependencies or changes to the application. Initial restore/install used the network; the tests and example do not call remote APIs or medical sources.

## Tested behavior

- All twelve definition kinds and eight directed relation kinds; separate simulation balancing.
- Invalid IDs, including final newlines, namespace ownership, duplicate IDs/packages, missing definitions and incorrect relation endpoint types.
- Missing/version-incompatible/undeclared dependencies and dependency cycles.
- Required provenance, unknown review values, approval audit fields, unsupported schema versions, invalid balancing ranges and duplicate balance records.
- Async JSON round trips and cancellation, null object graphs, required fields, duplicate JSON properties, unknown discriminators and preservation of nested additive extension fields.
- Concrete and base definition JSON round trips with discriminator/type checking.
- Complete de/en catalogs, culture parents and English/key fallback, plural and numeric formatting, .NET adapter positional formatting, mod namespace isolation and unchanged content IDs.
- Missing/unused translation keys, malformed templates, unsafe property selectors, unknown formatter names, nested placeholder mismatch, duplicate catalogs and null translation values.
- Published schemas accept examples and reject malformed definitions, timestamps, review metadata and null translations.
- Mod manifest metadata validation, including unsafe relative path rejection; archive loading is not implemented.

## Limits and open work

- Windows/Linux GitHub Actions workflow is configured; **GitHub Actions and Windows execution have not been observed in this local session**.
- Packages are local artifacts, not published to NuGet. Repository changes have not been pushed.
- No actual third-party medical data, clinical approval, importer, editor, database, ZIP mod packager or mod.io client is included. Full source/license review belongs to Phase 2 before import.
- Schema identifiers are offline-resolvable URIs, not an online schema hosting service. Both schema and semantic validation are needed.
- Godot integration is a documented engine-independent interface, not a tested Godot runtime adapter. Confirm the game's supported runtime separately.
- Review authorization, approved export policies and field-level source merging belong to later importer/editor workflows. Technical validation does not approve medicine.

Next implementation work is specified in [the Phase 2 plan](plans/phase-2.md).
