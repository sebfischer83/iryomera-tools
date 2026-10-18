# ADR 0019 — purchase prices and wages as balancing

Status: accepted on 2026-10-18, implemented for library 0.15.0.

## Context

For milestone M8 the game gets an economy: a balance, build and buy costs, hourly wages, and income per examination and per successful treatment. Examination and treatment costs already exist as balancing fields. The owner decided that furniture prices and wages are SDK balancing values too, so mods can override them like other balancing (ADR 0015). Construction costs for floors, walls and doors stay a game setting.

## Decision

- GameplayBalance and GameplayOverride gain optional `purchaseCost` (game currency per item) and `wagePerHour` (game currency per game hour), nonnegative and omitted when null.
- They follow the existing balancing rules: they resolve field by field in load order through `GameplayResolver`, and competing values are `gameplay-override-conflict` warnings. Like `capacity` they are not restricted to a definition kind; typically `purchaseCost` targets furniture or equipment and `wagePerHour` targets staff roles.
- The demo clinic input carries the game's suggested synthetic amounts in a `balancing` block. The generator writes them as gameplay balancing.

## Consequences

The fields are additive. A 0.14 reader keeps them as extension data but does not resolve them, so the game needs 0.15.0. Prices and wages are game economy values, never real costs; they carry no medical or legal meaning.
