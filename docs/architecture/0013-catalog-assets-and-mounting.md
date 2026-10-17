# ADR 0013 — catalog asset references and furniture mounting

Status: accepted on 2026-10-17, not implemented. Tools ADR; the game's own ADR 0013 covers its side of the same interface. Plan: [furniture asset catalog references and mounting](../plans/furniture-assets.md).

## Context

Purchased models used by the base game must never enter this repository or a public package ([ADR 0010](0010-data-only-mod-packages.md)), but base-game definitions need to name them. Wall- and ceiling-mounted furniture cannot be expressed with the floor-only `FurniturePlacement`.

## Decision

- Add optional `ContentDefinition.catalogAssets`, a list of `{ catalogId, kind }`, omitted when null. `catalogId` is an opaque ID in content ID syntax (the game uses `iryomera.assets:<category>.<name>`, without vendor, file or node names). Nothing is shipped, so there is no path, license or ModKit inventory entry. The SDK validates format, kind and uniqueness only; the game resolves IDs from its private catalog with a procedural fallback. `AssetReference` remains unchanged, so its required `path` stays a non-breaking C# contract and older readers keep the new list as an additive property.
- Add optional `FurniturePlacement.mount` (`floor` | `wall` | `ceiling`, absent means floor) and `mountHeightMeters` (wall only, nonnegative, no SDK upper bound), omitted when null to preserve legacy hashes. Wall: X along the wall, Z into the room (+Z), origin on the wall surface, height from the storey floor to the origin. Ceiling: origin at the ceiling, model hangs towards −Y. Floor semantics are unchanged.
- No pivot, rotation or scale corrections in the SDK; they are presentation owned by the game catalog. Mod GLB files follow documented conventions instead: meters, Y up, front +Z, scale 1:1, origin per mount type, one file is one model. No node selector in this version.
- Placement legality (free wall edge, doors, storey height, whether wall/ceiling footprints block floor cells) is game logic. Decorative per-room-type fixtures stay a game fixture; placeable furniture later uses `roomFurniture`.

## Consequences

Additive within content schemaVersion 1.0.0 as library 0.7.0; content using the new fields needs a 0.7.0 reader to be validated. ModKit reports catalog references as unresolved external references and never fetches or packages them. No purchased asset, catalog file or model-splitting tool is added here.
