# ADR 0027 — competing hospitals

Status: accepted on 2026-10-28, implemented for library 0.25.0 (game request: roadmap item "comparison of houses and doctors", `docs/plans/press-rankings.md` in the game repository; owner decision 2026-10-28 that the names of competing houses are content in a data package). Implemented by the game session.

## Context

The game compares its departments with other hospitals. The other hospitals are invented. Their names must not be real hospitals or chains, must be translatable and must be extendable by mods, so they are content, not code.

## Decision

- New definition kind `competitor` (`CompetitorDefinition`): a fictional competing hospital with id, name key, version and provenance. It has no further fields, no relations and no balancing; the game decides how competitors are compared.
- Like `insurer` it is a fictional organisation; provenance should state that the name is invented.

## Consequences

- Schema, examples-independent validation and the Content Studio kind list know the kind; older packages are unchanged.
- Packages and mods can add competitors; the game reads all of them.
