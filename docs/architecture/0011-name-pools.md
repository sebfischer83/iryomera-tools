# ADR 0011 — name pools for generated doctors and patients

Status: accepted for Phase 5 (name importer) on 2026-10-17. Not implemented yet.

## Context

The game needs plausible, country-specific names for generated patients and staff. Today it uses ten fixed German fixture strings (`DummyClinicCatalog` in the game repository) and has no gender, origin, savegame format or non-Latin fonts. The game will consume SDK packages from its milestone M7 (game ADR 0009) and does not duplicate schemas. Random selection, patient state and the seeded PRNG belong to the private game ([ADR 0008](0008-diagnostic-content.md)).

## Decision

Tools deliver **name data**, the game **generates** names.

- Add two optional package-level lists, `namePools` and `nameFormats`, additive within content schemaVersion 1.0.0 and omitted when null. They are records like reference ranges, not definition kinds, so older readers keep them as unknown additive properties instead of rejecting the package, and they cannot be targeted by relations or gameplay balance:
  - `namePool`: stable ID (`namespace:local_name`), display `nameKey`, `country` (ISO 3166-1 alpha-2), optional `region` (ISO 3166-2), `part` (`given` | `family`), keyed sources with provenance, and entries. Each entry has a stable entry key, `localized` (original script, exact source spelling), optional `romanized` with its source key, `gender` (`female` | `male` | `unisex` | `unknown`, required for given names) and one or more per-source attestations (external ID, rank, count, proportion, birth-year cohort).
  - `nameFormat`: per country/region, order (`givenFamily` | `familyGiven`), number of family-name parts (for example two Spanish surnames) and the separator in the original script (empty for Japanese). The separator applies only to `localized` parts; romanized parts are always joined with one space, and the game decides whether to show romanized names in Western order. Patronymic rules are deferred to a later additive version. Professional titles such as "Dr. med." or "Prof." are localization keys in the en/de catalogs, not name entries.
- Name strings are data, not localized text. They do not get localization keys, and they never change with the UI language.
- Statistics stay per source. Merging the same string from several sources keeps one entry with every contributing provenance and each source's own statistics; no combined weight is computed in the tools.
- Country mixes and origin weights for a clinic or scenario are pure game logic. The tools define no balancing type for them, and they are never part of name pools. How cohorts are used is also decided by the game.
- Doctors and patients share the same pools. No person-type field and no nickname field are added. Story characters, if needed later, get their own content type.

## New tool

`src/Iryomera.NameImporter` (.NET 10 CLI, command name to be decided) with its own strict staging workspace, adapters per source ([source review](../data-sources/names.md)), an explicit rights/review step and a release export of approved pools as an SDK content package that ModKit can pack. It reuses SDK/Localization and the Wikidata query infrastructure where possible, but not the medical review rules, because names are not medical assertions. It does not depend on MedicalImporter, Studio, Godot or mod.io.

## Rules

- Never link name origin, script or country to diseases, medical risk, patient factors or balancing. The game holds the same rule.
- Never import data about real persons or ethnicity/race/nationality columns. Generated combinations are synthetic and must be labeled as such where shown in tooling.
- Preserve exact source releases, licenses, attribution and update dates. Restricted sources (for example commercial-use restrictions) are local user-supplied input with redistribution undecided.
- Patient gender is not introduced by this ADR. If the game adds it, that is a separate decision because it has medical relevance.

## Moddability

Name pools and formats are ordinary SDK content, so data-only mods can ship them through ModKit like any other definitions ([ADR 0010](0010-data-only-mod-packages.md)). The imported base pools and mod pools use the same contract; the importer is only one way to author them.

- Namespace ownership is unchanged: a mod cannot edit another package's pool. It adds its own pools (for example a new country, region or more entries for an existing country) in its own namespace. The game aggregates all installed pools by `country`/`region`/`part` (country-wide pools plus pools of the requested region); entry keys are scoped to their pool, so identical strings from different packages do not collide.
- The SDK provides the aggregation and guarantees a deterministic order independent of load order, file system or culture: pools sorted by ID, then entries by entry key, both with ordinal comparison. The game's seeded selection relies on this; changing the order is a breaking contract change.
- A mod may ship a `nameFormat` only for a country/region not already covered by its declared dependencies; competing formats for the same scope are a validation error with the stable code `name-format-conflict` rather than an implicit last-wins override. Further name diagnostics use the same kebab-case `name-*` prefix.
- Removing or hiding entries of another package needs an explicit override or exclusion contract. No such contract is planned: mods only add names, and filtering belongs to the game's mod settings.
- Hand-authored mod entries need provenance and a license like other content, but no source statistics. Packaging remains technical validation, not a rights decision.
- Validation applies to all name strings regardless of origin: Unicode NFC, length limits, no control or bidirectional override/isolate characters, no empty or whitespace-only names, and declared `romanized` values in Latin script. Content moderation of mod-supplied names is the game's or the distribution platform's responsibility.

## Game handoff

The game stores the final rendered name string together with pool/format IDs and content version, so saves remain stable across content updates. If a pool is missing when a save is loaded (for example an uninstalled mod), the game keeps the stored string; that policy is owned by the game and needs no SDK contract. It displays `romanized` until a font decision for CJK/Cyrillic/RTL scripts exists; `localized` is always delivered.

## Owner decisions (2026-10-17)

1. The name importer is Phase 5, before ModIo (now Phase 6).
2. Birth-year cohorts are optional per-source statistics in the contract; pools without them remain valid.
3. Doctors and patients share pools; no nicknames or person types.
4. Country/origin selection is game logic only.
5. Mods add pools; no override/exclusion contract.
6. The DFD will not be contacted. German surnames start from Wikidata and the sigpwned fallback without frequency statistics.
