# ADR 0024 — staff and service room roles

Status: accepted on 2026-10-25, implemented for library 0.22.0 (game request: staff breaks, ward offices, toilets and cleaning, game ADR 0045; owner approval 2026-10-25, "implement it in the tools repository").

## Context

The game gains staff breaks, an office for every bed station, separate toilets for patients and staff and cleaning. It must know which room type plays which part without deriving it from IDs. Room roles already tell reception, waiting rooms and wards apart (ADR 0020).

## Decision

- `RoomRole` gains `StaffRoom`, `WardOffice`, `PatientToilet`, `StaffToilet` and `CleaningRoom` (JSON `staffRoom`, `wardOffice`, `patientToilet`, `staffToilet`, `cleaningRoom`).
- No new fields or validation rules: a role stays optional and one per room type.
- Studio: the room role list shows the new values in German and English.

## Consequences

Packages that use a new role need readers of 0.22.0 or later; older readers reject the unknown enum value. Breaks, toilet visits, dirt and cleaning are game logic and game balancing. The demo clinic is unchanged.
