# Phase 4 verification

Version: 0.5.0. Verification date: 2026-10-16. [Plan](plans/phase-4.md), [ADR 0010](architecture/0010-data-only-mod-packages.md), [ModKit guide](modkit.md) and [synthetic example](../examples/modkit/README.md).

## Merge and implemented scope

The reviewed Phase 3 branch (including the previously unmerged Phase 2/remediation history) was merged into main at `1894e207816cb0f272fc669ce6e12f40f9fcf357`. [Main CI run 38951568179](https://github.com/sebfischer83/iryomera-tools/actions/runs/38951568179) completed successfully on Windows and Linux before Phase 4 integration. Phase 4 is developed separately on `feature/phase-4-modkit`.

ModKit implements new/edit/validate/pack/inspect/unpack as a local .NET 10 CLI and reusable async APIs. It has no Studio/database/Godot/mod.io dependency. The project format reuses SDK metadata with an explicit typed/licensed file inventory; ZIP adds a versioned SHA-256/size index. Validation combines original JSON schemas, complete virtual content graphs, independent medical provenance, game-version ranges, explicit package dependencies, en/de/localized aliases and declared assets. Original payload bytes and extension metadata survive packaging; conflicting virtual-graph extension values fail.

Portable SDK additions are optional asset references and furniture footprints/interaction points, with corresponding v1 schemas. Null properties stay omitted to preserve legacy wire/digest behavior. Existing room/resource/diagnostic relations are reused. ModKit resolves namespaced balancing resource IDs as a profile rule while legacy SDK resource vocabularies remain unchanged. Shared alias validation was factored into Localization for Studio/ModKit reuse and now rejects alias metadata that would suppress mandatory shared-key translations.

The synthetic MIT example contains all thirteen definition kinds, ten relationships, numeric observation metadata, separate simulation balancing, en/de catalogs, an own generated cube GLB, two generated PNGs, original asset notices and a 2×1 footprint with service interaction point. The new-command templates are data resources, not medical business-logic constants. There is no medical approval or private patient simulation.

## Local validation

Host: Debian 13, .NET SDK 10.0.402, runtimes 10.0.13 / 8.0.32.

| Check | Observed result |
| --- | --- |
| Full Release build | 0 warnings, 0 errors |
| Foundation tests net8.0 | 137 passed |
| Foundation tests net10.0 | 137 passed |
| Importer tests | 95 passed |
| Studio SQLite/service/HTTP tests | 17 passed |
| ModKit project/CLI/archive tests | 44 passed |
| Total .NET executions | 430 passed; Foundation cases execute on both runtimes |
| Python acquisition tests | 4 passed |
| Real ModKit CLI example | new/edit/validate/pack/inspect/unpack, deterministic repeat ZIP and exact payload bytes passed |
| Published Studio browser regression | Interactive en/de edit, SQLite, SVG, review/rights and ZIP download passed with external app requests blocked |

New failure cases include traversal/absolute/Windows-device paths, case collisions, file/parent conflicts, malicious ZIP links/special/reparse entries, tampered hashes/sizes, missing/extra entries, invalid indexes, configured size/count/ratio ceilings, source symlinks, linked destination parents, canceled operations, disguised model/image payloads and rehashed GLB references to external URIs. Tests confirm no partial target is published and invalid edits retain original bytes. Actual filesystem symlink creation is exercised on Linux; Windows ZIP link/reparse metadata attacks are exercised without requiring elevated symlink privileges.

NuGet SDK/Localization/importer/ModKit packages were produced, and the packed ModKit installed and served its real command tree locally. The packed net8 SDK/Localization binaries passed Godot 4.7.2 with annotations, diagnostics, asset/layout contracts and en/de catalogs. Windows/Linux branch CI results will be recorded below after completion. A local Linux run does not establish a Windows pass.

## Limits and remaining work

- Phase 5/mod.io is not implemented. No graphical ModKit editor, game loader, private patient simulator, publisher signature or automatic dependency download is claimed.
- ZIP32 single-disk packages use the documented portable filename/size profile. Hashes provide integrity, not authenticity; there is no signature/trusted-publisher verification. Filesystem staging assumes no hostile same-privilege concurrent writer.
- GLB checks validate framing and embedded-resource policy, not exhaustive glTF conformance or renderability; image checks validate signatures without decoding. Runtime asset/interaction behavior belongs to the game.
- CLI dependency inputs are explicit SDK packages; installed-project discovery and coordinated multi-file editing transactions remain future enhancements. Advanced definitions remain data/JSON authoring until the later GUI.
- Technical packaging is neither clinical review nor a rights grant. Imported medical data, source-specific restrictions and third-party assets keep their original licenses. No restricted dataset or purchased asset is included or downloaded. Existing ICD/OPS/LOINC complete-file verification still needs legitimately supplied inputs.
- SDK and ModKit schema families stay independently versioned at 1.0.0. New asset/layout data needs a reader supporting the 0.5.0 capability; old valid content remains readable.

## GitHub Actions

Pending final branch push and observed Windows/Linux completion. CI regenerates schemas/example assets, runs all suites and the real CLI example, published Studio browser flow, packs libraries/tools and exercises net8 packages in real Godot.
