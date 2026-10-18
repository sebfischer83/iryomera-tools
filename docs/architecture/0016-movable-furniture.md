# ADR 0016 — movable and transport furniture

Status: accepted on 2026-10-18, implemented for library 0.12.0. Agreed with the game session.

## Context

The game needs wheelchairs, mobile beds and similar items: where a patient sits or lies, where staff pushes or operates, and whether an item can be moved. The game's private asset catalog already corrects model pivots; the question was where docking geometry and game meaning live.

## Decision

- Docking geometry lives in content, using the existing `FurniturePlacement.interactionPoints`, so it is moddable and editable in Studio and works for mod GLBs. Points gain an optional `kind`; standard kinds are `seat`, `lying`, `handle`, `operator` and `entry`. The game catalog only corrects the model (pivot at the lower corner for footprint furniture) so it matches the footprint origin and the points.
- The heading convention is fixed: 0° faces local +Z, counterclockwise seen from above (90° = +X), a right-handed rotation about +Y.
- FurnitureDefinition gains optional `mobility` (`fixed`, `movable`, `pushable`) and `transport` (`patientCapacity`, `posture`, optional `requiredQualificationId`). The SDK checks consistency with the point kinds (`furniture-transport`); paths, collisions and staff assignment are game logic. These values are game rules, not medical facts.
- Studio's `catalogAssetIndex` gains optional `mobility` and `interactionPointKinds` per model. Studio warns, without blocking, when a definition's mobility or transport does not fit the referenced model. For the game the content definition wins.

## Consequences

Additive and omitted when null; older readers keep the new fields as additive properties. The ModKit example adds a synthetic pushable wheelchair. No patient movement or pathfinding is implemented in this repository.
