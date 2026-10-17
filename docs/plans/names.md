# Phase 5 — name importer plan

Status: Phase 5, owner decisions recorded 2026-10-17 in ADR 0011. Step 2 (SDK contracts) is implemented; the importer tool is not. ModIo moves to Phase 6. Scope and contracts are defined in [ADR 0011](../architecture/0011-name-pools.md); sources in [name source candidates](../data-sources/names.md).

Initial countries: DE, US, JP (family-first order, non-Latin script) and ES (two surnames). More sources are used for variety.

1. Owner decisions are recorded in ADR 0011. Re-confirm the final schema with the game before publishing it.
2. Done on `feature/phase-5-name-pools`: extend the SDK additively with `namePools` and `nameFormats`, hand-written schemas in `scripts/generate-schemas.py`, docs in `docs/formats.md`, net8/net10 tests, and a synthetic bilingual example (invented names only, labeled synthetic).
3. After the [furniture asset extension](furniture-assets.md): add `src/Iryomera.NameImporter` and its xUnit project: strict versioned staging workspace with file lock and atomic replace, `sources`, `import`, `validate`, `review`/`rights`, `export`. Import is never approval; restricted batches stay undecided.
4. Adapters in order, each after re-verifying its terms: Wikidata name items, US SSA, US Census surnames (race columns dropped), INE Spain, Statistik Austria, sigpwned fallback. City open data DE only after per-dataset license confirmation. BFS only as user-supplied input.
5. Merge identical strings across batches with all provenance retained; keep per-source statistics. Unicode normalization rules are explicit and part of the adapter identity; original bytes stay in staging.
6. Export approved pools and formats as an SDK content package plus license/attribution notices; verify it packs with ModKit and loads with `ContentDocuments`. Add a synthetic example mod that contributes its own pool for an existing country and a format for a new one, plus tests for name-string validation, `name-format-conflict` across dependencies, and an aggregation order that is identical for shuffled package/file input order on net8 and net10.
7. Tests: offline synthetic fixtures per adapter, malformed input, suppressed/missing columns, script/romanization handling, merge conflicts, rights gates and schema/example parity. Opt-in full-dataset runs follow the existing harness rules and publish aggregate reports only.
8. CI on Windows/Linux, docs, verification report. No generator runtime, PRNG or patient gender in this repository.
