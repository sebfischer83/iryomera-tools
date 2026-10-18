# ModKit: data-only authoring and ZIP packages

ModKit is the Phase 4 .NET 10 tool and async application API. It creates, edits, validates, packages, inspects and unpacks local mods independently of Studio, Godot, databases and mod.io. It does not execute mod code, download resources, simulate patients, approve medical claims or grant redistribution rights. English documentation is authoritative. See [ADR 0010](architecture/0010-data-only-mod-packages.md) and [the implementation plan](plans/phase-4.md).

## Build and use

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet run --project src/Iryomera.ModKit -c Release --no-build -- --help
```

Replace `iryomera-mod` below with `dotnet run --project src/Iryomera.ModKit -c Release --no-build --`, or install the locally packed tool (released versions can also be installed from the [GitHub Packages feed](releases.md)):

```sh
dotnet pack src/Iryomera.ModKit -c Release --no-build -o artifacts
dotnet tool install Iryomera.ModKit --version 0.16.1 --add-source ./artifacts --tool-path ./artifacts/mod-tools
# Windows: artifacts/mod-tools/iryomera-mod.exe
# Linux: ./artifacts/mod-tools/iryomera-mod
```

Packages are created locally/CI; publication to nuget.org is not implied. Initial dependency/tool restore needs NuGet. Authoring, validation and packaging then work offline.

```sh
iryomera-mod new --output ./my-mod --namespace author.demo --author "Your name" --name "My synthetic mod"
iryomera-mod validate --input ./my-mod --game-version 0.2.0
iryomera-mod edit --input ./my-mod --path content.json --replacement ./edited-content.json
iryomera-mod pack --input ./my-mod --output ./my-mod.zip
iryomera-mod inspect --input ./my-mod.zip
iryomera-mod unpack --input ./my-mod.zip --output ./unpacked-mod
```

`new` scaffolds own synthetic definitions/assets, all unreviewed. It changes the namespace, author and English mod name; the German synthetic template remains explicit rather than inventing a translation of a custom name. Review and replace example labels/values before using it for authored content. The scaffold's declared game range `[0.1.0,1.0.0)` is illustrative; configure the actual compatibility range. If `--game-version` is omitted, no particular installed game version is certified.

Edit JSON in a text editor, or use `edit` to validate a candidate snapshot before atomically replacing one declared file. Invalid replacements leave the original file unchanged. Larger coordinated manifest/content changes (such as changing identity/version/dependencies in both manifests) can be made in a text editor and then validated together. New payload files must be declared in `mod.json`; they can be placed locally before replacing the manifest. A cooperative `.iryomera-mod.lock` prevents simultaneous CLI pack/edit writers. Unlisted files are never packaged; a stale `checksums.json` in an unpacked authoring folder is regenerated on the next pack.

## Public project format

The authoring root contains `mod.json` with its own `schemaVersion: "1.0.0"`, the existing SDK `manifest` and a `files` array. [The committed example](../examples/modkit/example-mod/mod.json) shows complete metadata. Each inventory entry has `path`, `kind`, original `license` and optional `attribution`.

| File kind | Supported payload |
| --- | --- |
| `content` | SDK ContentPackage JSON, including new diseases/symptoms/findings/examinations/treatments/rooms/furniture/equipment/staff roles, relationships and separate balancing/diagnostic configuration |
| `localization` | SDK namespace/language catalog JSON, including mandatory en/de and optional additional cultures |
| `modelGlb` | Binary glTF 2.0 model, JSON + optional BIN chunk; embedded bufferViews only |
| `texture`, `icon` | PNG, JPEG or WebP with matching file signature |
| `notice` | `.txt` or `.md` license/attribution/documentation file |

`manifest.contentFiles` must exactly list content/localization inventory entries. Actual catalog languages must match `supportedLanguages`. Every ContentPackage manifest must agree with the mod ID, namespace, version, license and dependencies. Multiple content files are merged only as a virtual validation graph: original bytes, provenance, review statuses and extension data remain unchanged in the package. Duplicate IDs and conflicting balances remain errors. The mod name's localization key participates in en/de checks. Catalog-only or balancing-only mods need no dummy definitions; namespaced balance targets must resolve through declared dependencies. Catalogs are owned by the mod namespace; overriding another package's language catalogs is not an implicit overlay facility.

Asset references on definitions must match an inventory entry's path, kind, license and attribution. Standard shared localization keys and independent-language Wikidata alias keys are validated through the shared Localization library. Package compatibility does not confer medical validity. An imported reviewed release retains its source terms/review metadata; distributing it still requires applicable permissions/notices. ModKit never upgrades an unreviewed status or replaces the importer/Studio release review process.

## Assets, furniture and rooms

Content SDK 0.5.0 additively introduces optional `ContentDefinition.assets` and `FurnitureDefinition.placement`. Existing valid content omits these fields and remains readable; they are omitted when null to preserve legacy wire shape and digest behavior. Standalone schemas cover AssetReference, FurniturePlacement, FurnitureFootprint, FootprintCell and InteractionPoint.

An asset reference has `path`, `kind` (`modelGlb`, `texture`, `icon`), original `license` and optional `attribution`. Paths are relative to the mod root. The supplied cube/image assets are own generated MIT fixtures, not purchased assets.

Furniture placement includes a footprint with positive integer `width`/`depth`, positive `cellSizeMeters` (default 1), and optional `occupiedCells`. An empty cell list fills the rectangle; explicit X/Z cells must be unique and within bounds. Interaction points have stable unique keys, an optional kind (`seat`, `lying`, `handle`, `operator`, `entry` or another stable key), local X/Y/Z meters, optional heading `[0,360)` (0° faces local +Z, counterclockwise seen from above, 90° faces +X) and an optional staff qualification ID. Origin is the footprint's lower X/Z corner, Y up; points can stand outside occupied cells. Runtime orientation, placement and interaction execution belong to the game.

Use `roomFurniture` and `roomEquipment` relations for what a room type offers, with `requirement` for required or optional items (absent means required for furniture and optional for equipment), and examination room/equipment/qualification relations for diagnostic resources. These are independent sourced records; no duplicated room requirement list or arbitrary mod callback is introduced. The example includes all thirteen definition kinds, a numeric observation, fourteen relationships (including a required `roomEquipment`, a `treatmentEquipment`, an `examinationStaffRole` and a `diseaseDepartment` with priority), room size and door rules, a department capacity, a 2×1 furniture footprint with a surface, service point and separate simulation balancing.

A definition may also list `catalogAssets` (`catalogId` + `kind`) to reuse a model from the game's private catalog without shipping it. Catalog references need no inventory entry, are never fetched or packaged, and `inspect` lists them as `unresolvedCatalogAssets`. The game decides whether an ID exists; unknown IDs fall back to a procedural model. Never put purchased files, vendor names, file names or node names into a mod or a catalog ID.

GLB conventions for shipped mod models: meters, Y up, front facing +Z (glTF default), scale 1:1, one file is one model (the game uses the scene root). The origin is the footprint's lower X/Z corner on the floor; for `mount: wall` it lies on the wall surface with the model extending into the room along +Z, and `mountHeightMeters` gives its height above the storey floor; for `mount: ceiling` it lies at the ceiling with the model hanging towards −Y; for `mount: surface` it lies on the supporting item's surface with the model standing towards +Y. There are no pivot, rotation or scale correction fields. The example adds a wall-mounted notice board that uses an example catalog ID.

GLB checks cover v2 framing, bounded JSON, chunk lengths and embedded-only resource policy. URI-bearing resources (including data URIs) are deliberately rejected in this v1 profile. The tool does not claim exhaustive glTF mesh/material/extension validation or rendering compatibility. Images are signature-checked, not decoded. The private game's asset loader must enforce its own supported-format/runtime limits; no asset is rendered or loaded as code by this tool.

## Dependencies and local operation

Supply installed dependency ContentPackage JSON files explicitly, including transitives:

```sh
iryomera-mod validate --input ./my-mod --dependency ./base/content.json ./other/content.json --game-version 0.2.0
iryomera-mod pack --input ./my-mod --output ./my-mod.zip --dependency ./base/content.json
iryomera-mod unpack --input ./my-mod.zip --output ./installed/my-mod --dependency ./base/content.json
```

Mods may override balancing values of declared dependencies with `gameplayOverrides` (field by field, last package in load order wins, conflicts reported as warnings by `validate`); see [formats](formats.md) and [ADR 0015](architecture/0015-gameplay-overrides.md). Within ModKit, balancing resource keys containing a colon must resolve as content IDs through declared dependencies; unqualified symbolic resources such as `power_supply` remain valid. This profile rule does not change legacy SDK resource vocabularies. Dependencies use existing package IDs and NuGet version ranges. Missing packages, version mismatches, duplicate IDs/namespaces, undeclared references and cycles fail. Mods can refer to a shared examination without copying its definition. Dependencies are never downloaded, automatically installed or bundled into the dependent archive. The game version check is separate from package dependency versions. Dependency files can themselves be validated standalone SDK packages; for split dependency projects supply their combined SDK package through the API. An installed-mod discovery/catalog UI is future work.

## Archive format and safety

A v1 archive contains exactly `mod.json`, declared payload files and `checksums.json`. The latter is a versioned ModArchiveIndex with exact `path`, `sizeBytes` and lowercase SHA-256 for every other entry. Entry/manifest/index inventories must agree exactly. Hashes verify integrity, not publisher identity or a signature.

Portable v1 filenames use ASCII letters/digits, `_`, `-`, `.` and forward-slash separators. Segments start with a letter/digit, contain at most 100 characters and cannot end with a dot. Total relative path length is at most 200. Case-insensitive collisions, Windows device names, absolute paths, traversal, alternate streams, directory entries, symlinks/reparse points, special files and parent/file conflicts are rejected. Links in source paths or output ancestors are rejected too. Existing destinations are never overwritten.

Default ceilings: 128 MiB compressed archive, 64 MiB per file, 256 MiB expanded total, 2,048 entries, 8 MiB JSON and compression ratio 200. GLB JSON is additionally capped at 4 MiB. API callers can supply positive PackageLimits; CLI uses the defaults. Expanded byte limits are enforced while streaming, before publishing any target. ZIP32 single-disk archives are supported; ZIP64 and oversized central directories are rejected before entry metadata allocation.

Inspect/unpack verify into fresh staging; unpack moves the fully validated folder atomically on the same filesystem. Pack captures declared files, checks for source changes, fixes entry order/timestamps/attributes, verifies its produced archive and publishes a new file atomically. Identical inputs yield identical ZIP bytes on the same compressor/runtime. Cross-runtime compressor byte identity is not promised. Failure/cancellation cleans temporary output and leaves no partially published mod. Same-privilege hostile concurrent filesystem mutation is outside the local tool's isolation guarantee.

## APIs, schemas and verification

- `ModProjects.CreateAsync` and `EditAsync`: synthetic scaffolding and validated single-file edits.
- `ModValidator.ValidateAsync`: files, virtual SDK graph, assets, localization, dependencies and optional game version; returns semantic issues. Malformed JSON/paths/files fail with useful exceptions.
- `ModArchive.PackAsync`, `InspectAsync`, `UnpackAsync`: bounded atomic archive operations using the same validation policy.
- `ModDocuments.ReadAsync<T>`: original JSON schema boundary for ModProject/ModArchiveIndex, before defaults. Schemas are embedded and resolved offline.
- All I/O APIs accept CancellationToken. CLI exit codes: 0 success, 1 invocation/I/O/format/packaging failure, 2 semantic validation issues, 130 handled cancellation.

Public entry schemas are under [schemas/modkit/v1](../schemas/modkit/v1/), separately versioned from SDK v1. Regenerate with `python scripts/generate-modkit-schemas.py`; regenerate own data/assets with `python scripts/generate-modkit-example.py`. See [the runnable example](../examples/modkit/README.md) and [verification report](phase-4-verification.md). Phase 5 mod.io, a graphical ModKit editor, signatures, a private game loader and patient simulation remain outside this phase.
