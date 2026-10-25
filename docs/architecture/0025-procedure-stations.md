# ADR 0025 — stations of examinations and treatments

Status: accepted on 2026-10-25, implemented for library 0.23.0 (game request: `docs/plans/examination-stations-sdk-request.md` in the game repository; owner approval 2026-10-25, "implement it yourself" while the tools session was offline).

## Context

In the game an examination books a room, a staff member and a duration, but nothing says where patient and staff are. A consultation should look like one: the doctor sits at the desk, the patient opposite, then the patient lies on the couch and the doctor stands next to it. How an examination runs is content, so the game must not derive it from IDs.

## Decision

- ContentPackage gains the optional list `procedureStations` of ProcedureStationSet (`definitionId`, ordered `stations`). A ProcedureStation has `key`, `share`, `patient` and optional `staff`; a StationPlace has `pointKind` and optional `equipmentId`.
- A list on the package, not a field on the definition: ExaminationDefinition has no fields of its own, and a mod replaces a whole set by naming the same definition (last in load order wins). No partial overrides.
- One place per role. Several staff members per order can follow additively as a list.
- `standing` is a place kind of stations only, not a furniture point kind.
- Validation codes `procedure-station-target|share|key|place`. Places no room type can offer are not an error; `ProcedureStations.Unplaceable` reports them for tools.
- Studio editing is deferred; the private data generator writes the sets.

## Consequences

Packages with stations stay readable by older readers (unknown additive property). The game decides walking, the order of who moves first and fallbacks when a place is missing. Stations carry no medical meaning and no balancing.
