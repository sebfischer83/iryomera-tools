# Phase 2 external review remediation

Scope: improve the existing importer, not ContentStudio. Integrate the reviewed Foundation (net8/net10 portable libraries, raw schema validation, canonical MedicalAnnotation) with the archived Phase 2 implementation. Keep every existing importer test and Foundation behavior.

1. Reconcile branches and verify the original Windows/Linux 124-test run. Retain modern CI and Godot packaged smoke testing.
2. Keep batch rights as the baseline; add record-scoped decisions with permission evidence for additional third-party terms. Recompute conservative HPOA/OMIM requirements from raw claims, including old workspaces. Batch permission cannot override a record restriction.
3. Add explicit bulk-review exclusions and eligibility checks. Preview first; commit decisions and all exclusions atomically to an auditable history. Keep the existing strict review operation.
4. Coalesce identical assertions across batches without dropping approved provenance, context or independent rights/review decisions. Conflicting claims remain errors.
5. Add an ObservationDefinition for LOINC semantics; no automatic playable examination or new clinical relation. Document additive-reader compatibility and preserve legacy examination records.
6. Export contextual HPO/MAxO annotations into the canonical SDK contract as well as the backward-compatible source sidecar. Validate original staging/release JSON against embedded schemas.
7. Provide an opt-in full-dataset harness with pinned release URLs/checksums, repeated deterministic import checks, runtime/memory/count/error reports. Download only publicly permitted ontology files. Restricted annotation/BfArM/LOINC data stays user-supplied outside git; no blanket rights approval or dataset publication.
8. Update schemas, ADRs, source documentation, CI evidence and limits. Run locked restore, Release build, all tests, synthetic CLI pipeline, NuGet packaging and real Godot smoke.

Dependencies remain inward: portable SDK <- net10 importer <- standalone integration harness. No UI, persistence service, automatic source network acquisition in the importer, or Phase 3 implementation.
