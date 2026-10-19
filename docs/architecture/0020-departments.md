# ADR 0020 — departments and patient flow

Status: accepted on 2026-10-18, implemented for library 0.18.0.

## Context

For milestone M9 ("Departments & progress", game ADR 0021) the game opens departments one after another. A department needs certain room types, treats patients in one of them, may require another department to be open first, and costs money and a number of discharged patients to open. Mods must be able to change these values.

## Decision

- New relation `departmentRoom` (medical department → room type) with `requirement`; absent means required, as for `roomFurniture`. An optional flag `treatmentRoom: true` marks the room type where the department treats patients. It is allowed only on this kind, must be on a required room, and a department has at most one (`relation-treatment-room`). Service departments such as diagnostics may have none. A flag was chosen over a separate relation kind so that a room type is linked once per department.
- New relation `departmentPrerequisite` (department → department that must already be open), acyclic (`department-cycle`).
- GameplayBalance and GameplayOverride gain `unlockCost` and `unlockDischargedPatients` (nonnegative; the latter a whole number). They resolve and conflict like all balancing fields (ADR 0015). Absent values mean open from the start in the game; that is game logic, not an SDK rule.
- `treatmentDurationSeconds` already applies to any definition; for diseases it is the time to treat a case.
- `Departments.Rooms` and `Departments.Prerequisites` aggregate the relations over a package set, like `RoomContents.Placeable`.
- Patient flow (game ADR 0021 section 9): RoomDefinition gains `role` (reception, waiting, ward) and `shareable`; balancing gains `openFromHour`/`openToHour`, `arrival` (walkIn, emergency) and `stayDurationSeconds`. `arrival` is an enum in balancing; the resolver carries it as its numeric value internally, so overrides and conflicts behave like every other field.
- Room types without a departmentRoom relation are general clinic rooms. Beds are ward furniture with a lying point; ward rooms make a department inpatient, so the abstract department capacity is no longer filled.
- The demo clinic gets general practice (open from the start, 8–18), the owner's department rooms, unlock values, opening hours, arrival routes and stays, and reception, waiting and ward rooms with desk, bench and bed.

## Consequences

Older readers reject the new relation kinds; the room and balancing fields are additive. The full department set of the real game is a separate, non-synthetic content package.
