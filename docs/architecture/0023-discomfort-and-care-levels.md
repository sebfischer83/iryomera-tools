# ADR 0023 — discomfort and care levels

Status: accepted on 2026-10-25, implemented for library 0.21.0 (game requests: examination batches need a downside, game ADR 0040; step-down care with an intermediate care unit, game ADR 0041; owner approval 2026-10-24/25, "discomfort" and "implement it in the tools repository" because the tools session was offline).

## Context

The game's measurement of examination batches showed that ordering more examinations always pays: examinations cost the clinic nothing but time. The owner chose discomfort as the downside: each examination or treatment costs the patient some satisfaction. Separately, the game's care levels move patients from intensive care to an intermediate care unit and then to a normal ward; the game must know a ward room's level without deriving it from IDs. Both are game design, not medical statements; all values are synthetic.

## Decision

- GameplayBalance and GameplayOverride gain two optional, simulation-only fields, omitted when null:
  - `discomfort`: whole number in [0,10], typically on examinations and treatments; absent means 0.
  - `careLevel`: whole number in [1,3], typically on ward rooms (1 normal, 2 intermediate, 3 intensive); absent means 1.
  Like other balancing fields they are not restricted to a definition kind, resolve field by field through `GameplayResolver` and can be overridden by mods.
- Validation (`balance-range`): discomfort outside [0,10] or not whole, care level outside [1,3] or not whole, on balancing records and overrides.
- Studio: both fields in the gameplay editor.

## Consequences

The fields are additive; older readers keep them as extension data. How discomfort reduces satisfaction, and how patients step down between care levels (phases, boarding), are game logic. The demo clinic is unchanged; content packages set the values themselves.
