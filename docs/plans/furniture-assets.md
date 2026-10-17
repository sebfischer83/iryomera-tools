# Furniture asset catalog references and mounting — plan (draft)

Status: draft for owner review, 2026-10-17. Nothing is implemented. This is an SDK/ModKit extension outside the Phase 5 name importer; it starts only after the owner accepts it. Inputs come from the game session (game ADR 0013, which records them as undecided on the game side). A tools ADR is written when the plan is accepted.

## Context

- The base game uses purchased models that may never enter this repository or any public package (AGENTS.md, [ADR 0010](../architecture/0010-data-only-mod-packages.md)). Today `AssetReference` only supports a package-relative `path` with its own license, so base-game definitions cannot reference those models.
- The game keeps a private catalog: neutral asset ID → source file, node, pivot/rotation/scale correction, with a procedural fallback. File and node names are not stable keys.
- Wall pictures, notice boards, curtains and ceiling lamps need a mounting type; `FurniturePlacement` assumes the floor.
- Decorative per-room-type fixtures (game stage 1) stay a local game fixture without an SDK contract. Placeable furniture later uses the existing `roomFurniture` relation.

## Proposed contracts (additive, content schemaVersion 1.0.0, library 0.7.0)

1. **Catalog asset references.** New optional `ContentDefinition.catalogAssets`, omitted when null: a list of `{ catalogId, kind }`.
   - `catalogId` is an opaque content-style ID; the game uses `iryomera.assets:<category>.<name>`. No vendor, file or node names.
   - `kind` reuses `AssetKind` (`modelGlb`, `texture`, `icon`).
   - No license, path or inventory entry: nothing is shipped. The SDK checks only the ID format, a known kind and uniqueness per definition; it never resolves IDs. The game resolves them and falls back procedurally.
   - Mods may reference base-game catalog IDs to reuse models without shipping them.
   - A separate list instead of an optional `path` on `AssetReference` keeps `AssetReference.Path` required for existing C# consumers. Older readers keep `catalogAssets` as an unknown additive property instead of rejecting the package.
2. **Mounting.** New optional `FurniturePlacement.mount` (`floor` | `wall` | `ceiling`; absent means floor) and optional `mountHeightMeters` (only with `wall`, nonnegative). Both are omitted when null so legacy hashes stay unchanged.
   - `floor`: unchanged; origin at the footprint's lower X/Z corner on the floor, Y up.
   - `wall`: X along the wall, Z is the depth from the wall surface into the room (+Z); origin on the wall surface. `mountHeightMeters` is measured from the storey floor to the origin; when absent the game applies a per-object default.
   - `ceiling`: origin at the ceiling, the model hangs towards −Y; Y stays up.
   - Interaction points stay local to the origin. Whether wall/ceiling footprints block floor cells, wall-edge availability (no door, enough width) and storey height (3 m in the game) are game rules, not SDK validation.
3. **No correction fields.** Pivot offsets, rotation and scale are presentation and belong to the game catalog.

## Conventions for mod GLB files (documentation, no new fields)

Meters, Y up, front facing +Z (glTF default), scale 1:1, origin at the footprint's lower X/Z corner; for `wall` on the wall surface, for `ceiling` at the ceiling. One file is one model; the game uses the scene root. A node selector is not part of this version and could be added later as an optional field that ModKit checks against the GLB JSON node names.

## Work packages

1. Owner decision on the open points below; write the tools ADR (next free number, distinct from game ADR 0013).
2. SDK: `CatalogAssetReference`, `ContentDefinition.CatalogAssets`, `FurnitureMount`, `FurniturePlacement.Mount`/`MountHeightMeters`; validation codes `catalog-asset`, `duplicate-catalog-asset`, `furniture-mount`; schemas in `scripts/generate-schemas.py`; docs in `docs/formats.md`; net8/net10 tests including legacy wire-shape/hash preservation and older-reader behavior.
3. ModKit: catalog references need no inventory entry and are reported by `inspect` as unresolved external references; path assets keep the current inventory binding. Extend `docs/modkit.md` with the GLB conventions and the synthetic example with one catalog reference and one wall-mounted item.
4. Godot smoke: load the new fields from the packed net8 package.
5. Release as 0.7.0 through the tag-based feed ([releases](../releases.md)), then re-confirm with the game session.

## Open owner decisions

1. Accept a separate `catalogAssets` list (recommended) or an optional `path` with `catalogId` on `AssetReference` (simpler JSON, but a source-breaking C# change and older readers fail on missing `path`).
2. Should the SDK cap `mountHeightMeters` (for example at the 3 m storey) or leave all height limits to the game (recommended: game)?
3. When: before the name importer tool (Phase 5, step 3), interleaved, or after it.

Out of scope: catalog file format, model import/splitting, purchased assets, decorative per-room fixtures, a node selector, and any runtime placement logic.
