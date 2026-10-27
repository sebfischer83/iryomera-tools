# ADR 0026 — recurring diseases

Status: accepted on 2026-10-27, implemented for library 0.24.0 (game request: milestone "regular patients with chronic conditions", `docs/plans/chronic-patients.md` in the game repository; owner approval 2026-10-27). Implemented by the game session.

## Context

The game plans regular patients: people with a chronic condition who come back over weeks. Which diseases recur and how often is content, so the game must not derive it from disease IDs (IDs are opaque).

## Decision

- Balancing gains `recurrenceDays`, a whole number of days in [1,365] on a disease: the mean interval between a patient's visits. Absent means the disease does not recur; there is no separate "chronic" flag.
- Like `careLevel` and `discomfort` it is simulation-only, overridable by mods field by field and checked as `balance-range`.
- The game decides everything else (who becomes a regular patient, how the interval varies, flare-ups, loyalty).

## Consequences

- Schema, resolver, override and Content Studio gameplay editor know the field; older packages are unchanged.
