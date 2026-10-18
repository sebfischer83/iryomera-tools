# ADR 0017 — room rules, staff roles, disease routing and capacity

Status: accepted on 2026-10-18, implemented for library 0.13.0.

## Context

For milestone M7 the game replaces its local demo fixtures with SDK content. A demo clinic needs room types with minimum size and a door rule, departments with capacity, examinations performed by staff roles, and routing from diseases to departments. The SDK had room types, departments, staff roles, qualifications and examination relations, but no fields or relations for these four points.

## Decision

- RoomDefinition gains optional `minimumCells` and `requiresDoor` (game layout rules; rooms are any area of grid cells, so no side length is defined).
- GameplayBalance and GameplayOverride gain optional `capacity`, so capacity is simulation balancing and overridable by mods.
- New relation `examinationStaffRole` (examination → staff role), matching the game model where an examination requires a staff role plus qualifications from `examinationQualification` (owner decision).
- New relation `diseaseDepartment` (disease → department) with optional `priority`: 1 is preferred, every linked department is acceptable.

## Consequences

Additive fields are omitted when null; the new relation kinds need a 0.13.0 reader. Routing and capacity are game and simulation rules for synthetic or reviewed content; they are not clinical guidance. A synthetic demo clinic package will be generated in this repository from the game's fixture values.
