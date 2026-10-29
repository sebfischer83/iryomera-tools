# ADR 0028 — furniture height

Status: accepted on 2026-10-29, implemented for library 0.26.0 (game request: `docs/plans/lively-rooms.md` L3 in the game repository, "no wall items above tall floor items – the catalog needs a height"). Implemented by the game session while the tools session was offline.

## Context

The game places decor by itself: wall pieces on room and corridor walls, plants and shelves in corners. A wall piece above a tall cabinet or behind a large plant is hidden, yet costs money. The grid knows footprints but no heights, and the game's private model catalog may not decide rules, because content and mods must be playable without it.

## Decision

- `FurniturePlacement.HeightMeters` (`heightMeters`, optional, omitted when null): how tall the item is in metres, from its base up – for a floor item its top above the floor. Allowed for every mount, must be positive (`furniture-height`).
- It describes the item as content, like the footprint: a mod's shelf can say it is tall without a model. Which height counts as tall and what follows from it are game rules.

## Consequences

- Schema, validation, formats and the Content Studio's furniture editor know the field; older packages and readers are unchanged (older readers keep it as an additive property).
- Content packages set it where it matters; items without it count as low.
