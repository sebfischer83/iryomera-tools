# Iryomera Tools — binding development rules

- Scope and sequence: Foundation → MedicalImporter → ContentStudio → ModKit → ModIo. Only implement the requested phase. Planned tools must not be represented by dummy implementations.
- Target .NET 10, C#, Windows and Linux. Keep Content.SDK independent of Godot, Blazor, EF/SQLite and mod.io. UI, persistence and distribution depend inward on the SDK.
- MIT covers our code and synthetic examples only. Preserve upstream data licenses and per-record provenance. Never commit restricted datasets, proprietary game data, credentials or purchased assets.
- Do not invent medical facts, associations, criteria or recommendations. Import does not imply approval. Keep medical provenance/review separate from simulation balancing; label synthetic examples.
- Stable namespace IDs, external identifiers and localization keys are different concepts. Do not change IDs on language switches. Keep relations as independent records with provenance, version and review status.
- Public JSON changes require synchronized versioned schemas, docs, examples and tests. Preserve additive extension fields. Breaking changes need a new schema version and migration decision.
- Use System.Text.Json, async I/O and cancellation. Use established libraries for version ranges and localized formatting. Keep deterministic validation with explicit dependency inputs and useful paths/codes.
- Run Release build, relevant tests and package checks after material changes. Test error cases and schema/example compatibility, not just happy paths. Do not claim success when checks cannot run.
- Add architecture decisions to docs/architecture. Read docs/plans before changing scope. Prefer small comprehensible changes and no unnecessary external services.
- Future imports must be reproducible, licensed and auditable. Future ZIP handling must enforce traversal/size limits. Never execute mod scripts or DLLs automatically.
- Consult current official documentation when integrating external APIs. mod.io is optional and requires mock HTTP tests; no live requests without explicit configuration.
