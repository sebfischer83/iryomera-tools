# ADR 0014 — treatment and room equipment relations

Status: accepted on 2026-10-18, implemented for library 0.9.0.

## Context

The owner wants to assign medical equipment to examinations, treatments and room types, also several times per device, and to mark equipment a room type needs. Until 0.8.0 only `examinationEquipment` existed. Asset assignment itself (catalog IDs, previews) is separate ([ADR 0013](0013-catalog-assets-and-mounting.md)).

## Decision

- Add relation kinds `treatmentEquipment` (treatment → medical equipment) and `roomEquipment` (room → medical equipment). They are ordinary independent relations with provenance and review; several assignments are several relations.
- Add an optional `requirement` (`required` | `optional`) to ContentRelation, omitted when null and allowed only on `roomEquipment`. Absent means optional. The game treats a room as complete only when all required equipment is placed. Quantities, costs and similar choices stay simulation balancing.
- No department relation: the game derives a department's equipment from its examinations and treatments (owner decision 2026-10-17).

## Consequences

Older readers reject packages that use the new kinds because unknown enum values are errors; existing content is unchanged. The SDK checks endpoint types and where `requirement` may appear (`relation-requirement`); placement and completeness are game logic. ContentStudio offers the new kinds in its relation editor; editing `requirement` in the Studio UI is part of the Studio asset work.
