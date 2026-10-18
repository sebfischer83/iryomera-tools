# ADR 0015 — mods overriding gameplay balancing

Status: **proposed** (draft for owner decision, 2026-10-18). Not implemented.

## Context

The owner wants data-only mods to replace balancing values of existing content: prices, durations, quantities and similar values from `GameplayBalance`, never medical content. So far mods only add records: namespace ownership forbids changing another package, and duplicate balance records are errors ([ADR 0010](0010-data-only-mod-packages.md)). The game asked for an explicit override record, a defined load order or priority, and a visible conflict message when two mods set the same value, as a warning rather than an error. Name pools and formats keep the no-override rule ([ADR 0011](0011-name-pools.md)).

## Proposal

- **Separate contract.** Add an optional package-level list `gameplayOverrides`, omitted when null. Older readers keep it as an unknown additive property. Each record has `schemaVersion`, `id` (owned by the mod's namespace), `version`, `targetDefinitionId` and only the fields it sets, using the same field names and ranges as `GameplayBalance` (`rarity`, `severity`, `examinationDurationSeconds`, costs, `requiredResources`, …). It carries `simulationOnly: true` like other simulation-only records.
- **What can be targeted.** Only balancing of definitions in a declared dependency, never the mod's own definitions (use `gameplay` for those). Definitions, relations, medical annotations, reference ranges, recommendations, name pools and formats cannot be overridden. Diagnostic simulation settings stay out of scope for v1.
- **Field-level merge.** An override replaces only the fields it sets. `requiredResources` merges per key; removing a key is not supported in v1.
- **Deterministic resolution in the SDK.** A pure function, for example `GameplayResolver.Resolve(IReadOnlyList<ContentPackage> loadOrder)`, starts from each definition's own `gameplay` record and applies overrides in the given order; for each field the last package wins. The game owns the load order (mod settings) and passes it explicitly; the SDK never sorts by file system, time or culture. Game and ModKit therefore get identical results.
- **Conflicts are warnings.** When two packages set the same field of the same target, the resolver reports a `gameplay-override-conflict` result with the field, both package IDs and the winner. These are returned separately from `ValidationIssue`, which today always means an error; nothing is silently dropped. Structural problems remain errors: `gameplay-override-target` (unknown target or undeclared dependency), `gameplay-override-own` (targets its own namespace), and the existing range checks.
- **ModKit.** `validate` checks overrides against the supplied dependencies; `inspect` lists override targets. Packaging remains technical validation.

## Rules kept

- Mods never change medical content or its provenance and review state. Overrides are simulation values only and never imply clinical evidence.
- Additive content stays additive; this is the only exception, limited to balancing fields.
- No automatic code execution; resolution is data-only.

## Open owner decisions

1. Field-level merge (proposed) or replacing the whole balance record.
2. May a mod override another mod's balancing, or only the base game's?
3. Should conflicts be visible to players (mod settings screen), or only in tools and logs? This is game UI; the SDK only reports them.
4. Removing `requiredResources` keys: needed now, or later?
5. Release target: with or after the Studio asset work.
