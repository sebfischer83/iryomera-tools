# ADR 0021 — treatments and complications

Status: proposed on 2026-10-19 for library 0.19.0 (game request, owner-approved; game ADR 0021 §11).

## Context

Until 0.18 the game treated a case with values on the disease (`treatmentDurationSeconds`, `treatmentCost`, `stayDurationSeconds`) in the department's treatment room. The game now chooses between treatments per disease, rolls an inpatient stay within a range, and rolls complications that extend the stay, require an examination or a follow-up treatment. The same treatment can be outpatient for one disease and inpatient for another.

## Decision

- Relations `treatmentRoom` (treatment → room type) and `treatmentStaffRole` (treatment → staff role), mirroring `examinationRoom` and `examinationStaffRole`. More than one staff role is an error (`treatment-staff-role`). A treatment without room or staff role stays valid, so older content keeps validating, but `Treatments.Playable` reports it as not playable; the game then uses the disease's values and the department's treatment room as before.
- `priority` is allowed on `diseaseTreatment` (1 first choice). `Treatments.Options` lists a disease's treatments in that order.
- Balancing gains `stayChance` and `complicationChance` (0–1), `stayDurationMaxSeconds` (with `stayDurationSeconds` as the shortest stay; not shorter within one record) and `extraStayDurationSeconds`.
- **Values per pair:** a balancing record or override may target the ID of a `diseaseTreatment` or `treatmentComplication` relation instead of a definition (`gameplay-target` for other relations). Mod overrides and conflict reports then work as for every other value. Precedence pair → treatment → disease is game logic; `Treatments.Effective` merges the five treatment fields in that order and reports each field's source.
- New definition kind `complication` with required `effect` (`extraStay`, `requiredExamination`, `followUpTreatment`) and `timing` (`duringTreatment`, `duringStay`). Parameters: `extraStayDurationSeconds` balancing, relation `complicationExamination` (complication → examination) and `complicationTreatment` (complication → treatment), each allowed only with its effect and required by it (`complication-effect`). `treatmentComplication` links a treatment to a complication; its base probability is `complicationChance` on that relation.
- Follow-up chains treatment → complication → treatment must not loop (`treatment-cycle`). The demo re-operation therefore uses its own treatment.
- Risk modifiers, the displayed risk level, the rolls and their seeds are game rules, not content. There are no deaths in version 1.

## Consequences

Older readers reject the new definition kind, relation kinds and enum values; new effects or timings need a new minor version. Content written for 0.18 stays valid. The demo clinic moves its treatment values from the diseases to the disease → treatment pairs.
