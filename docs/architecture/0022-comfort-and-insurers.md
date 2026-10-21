# ADR 0022 — furniture comfort and insurers

Status: accepted on 2026-10-21, implemented for library 0.20.0 (game request from the satisfaction milestone, issue #30; owner approval 2026-10-20, "request as proposed" and "offers from reputation").

## Context

The game's milestone "satisfaction → reputation → arrivals" (game ADR 0027) lets waiting patients lose satisfaction. Two follow-up steps need content: decoration that makes waiting more comfortable (game step Z3), and insurers whose contracts depend on the clinic's reputation and pay different prices (game step Z4). Both are game design, not medical or insurance-law statements; all values are synthetic.

## Decision

- GameplayBalance and GameplayOverride gain four optional, simulation-only fields, omitted when null:
  - `comfort`: whole number in [0,10], typically on furniture; absent means 0.
  - `minimumReputation`: number in [0,100]; from this reputation an insurer offers a contract; absent means 0.
  - `priceFactor`: number above 0, a factor on the income from the insurer's patients; absent means 1.
  - `patientShare`: number in [0,1], the insurer's share of all arrivals; absent means 0.
  Like other balancing fields they are not restricted to a definition kind and resolve field by field through `GameplayResolver`.
- New definition kind `insurer` (`InsurerDefinition`) with no own fields besides name and description keys. A separate kind keeps insurers out of the medical kinds and lets mods add their own; balancing on an existing kind would have needed a fake medical definition.
- Self-payers are the implicit rest: `Insurers.SelfPayShare` returns 1 minus the summed shares, never below 0. There is no self-payer record, so a mod cannot make self-payers pay a different factor; that stays a game rule (factor 1).
- `Insurers.All` lists every insurer of a package set with its resolved terms and the defaults above, ordered by ID.
- Validation (`balance-range`): comfort outside [0,10] or not whole, reputation outside [0,100], price factor not above 0, share outside [0,1]. `insurer-share`: the shares of all insurers visible to a package (its declared dependencies, then the package itself, after resolving overrides) add up to more than 1. Two mods that are valid on their own may still exceed 1 together; the validator cannot see that combination, so the game has to handle it (for example by scaling the shares down).
- The demo clinic adds comfort to the wall picture (3), ceiling lamp (1), waiting bench (2) and office chair (1), two decoration items, potted plant (comfort 4, price 300) and water dispenser (3, 500), offered optionally in the waiting room and reception, and three fictional insurers: Basic Fund (reputation 0, factor 1.0, share 0.6), Substitute Fund (55, 1.1, 0.25) and Private Cover (70, 1.4, 0.1). The two new items refer to neutral catalog IDs (`iryomera.assets:decor.potted_plant_basic`, `iryomera.assets:decor.water_dispenser_basic`) that the game's catalog does not list yet; the game draws such items as placeholders.

## Consequences

The fields and the kind are additive. Older readers keep the balancing fields as extension data but reject packages with an `insurer` definition, so the game needs 0.20.0 to load the demo clinic. How comfort reduces waiting losses, how contracts are offered, accepted and cancelled, and which insurer a patient has are game logic.
