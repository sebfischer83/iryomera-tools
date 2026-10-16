# Phase 2 — MedicalImporter implementation plan

Implementation delivered for the formats listed in [the source scope](../data-sources/README.md). See [verification and explicit remaining limits](../phase-2-verification.md). The work packages below remain the architectural acceptance record; unsupported source formats are not claimed as implemented.

## Foundation review prerequisites

The Phase 1 remediation adds canonical `MedicalAnnotation` / `AnnotationReference` / `ObservationFrequency` contracts. Future HPO/MAxO adapters must target those lossless SDK contracts (including raw source fields), use ContentDocuments/LocalizationDocuments at raw JSON boundaries and preserve explicit unresolved references. Do not replace this with an unconditional binary projection or a separate incompatible medical assertion schema. See ADR 0003 and ADR 0004 for semantics and game/runtime targets. The .NET 10 CLI should consume the net10.0 asset; the game can consume net8.0.

The Phase 2 external-review task authorized reconciling the published archive with the reviewed Foundation. The importer now emits canonical annotations and uses raw staging-schema validation. See the [review plan](phase-2-review.md) and ADRs 0005/0006. This does not authorize Phase 3.

## Goal and project boundaries

Add the executable/tool package `src/Iryomera.MedicalImporter` and `tests/Iryomera.MedicalImporter.Tests`. It references Content.SDK; Content.SDK must not reference importer adapters, CLI, storage or HTTP. Use an established CLI parser after reviewing its current official API and license. Keep all adapter tests offline and synthetic. Add actual import examples, not empty adapter classes.

## 1. Source and license verification gate

Before implementing each source adapter, check its current official release files, formats, identifiers, release version, download terms, attribution, commercial use, derivative and redistribution restrictions. Record the exact release URL, terms URL, access date, license and decision in `docs/data-sources/`. An open ontology's license must not be assumed to cover separately published annotation files. No complete third-party dataset is committed or redistributed without explicit permission.

Official starting points (to be verified at implementation time, not a present license grant):

- HPO: https://hpo.jax.org/ and https://github.com/obophenotype/human-phenotype-ontology
- MAxO ontology: https://github.com/monarch-initiative/MAxO
- MAxO annotations: https://github.com/monarch-initiative/maxo-annotations
- ICD-10-GM / OPS: https://www.bfarm.de/ (official classification releases and terms)
- LOINC: https://loinc.org/license/ and official downloadable releases

If a license decision affects architecture or redistribution materially, request the owner's decision with the actual terms attached. Permit local user-supplied input for restricted datasets and preserve their licenses in every output record. MIT applies only to importer code and our fixtures.

## 2. Reproducible staging and CLI

Implement `sources list`, `import <source> --input <path> --source-version <version> --license <...> --workspace <path>`, `validate`, and `export --output <path>`. Select a single durable local staging format with atomic writes; the later Studio can consume staged packages via a documented interface. Preserve both valid and unresolved assertions in staging. Do not export dangling references or silently drop diagnostics.

An import report records input SHA-256, byte length, adapter/version, source release/license metadata, normalized options, stable namespace mapping, timestamps and diagnostic counts. A reproducible run accepts a fixed import timestamp; the same input/options/adapter/timestamp must produce byte-identical normalized JSON in a stable order. Never use current time as an ID. Cancellation must not leave partial approved exports.

Introduce explicit staging/review models outside the SDK as needed. A raw import starts `unreviewed`, including its relations. Clinical approval is a separate auditable action; only approved records and approved relations with approved, available endpoints can pass the release export gate. Handle reference closure with an explicit fail/report policy, not implicit approval. The editor will later own reviewer identity/permissions.

## 3. First vertical slice: HPO

Inspect representative current official JSON/OBO releases. Implement the formats actually published and document selected release/version features. A term definition, synonyms, obsolete status, alternate ID and cross-reference are not interchangeable with a disease/phenotype association. Parse disease associations only from the separately documented annotation source when available and licensed. Do not treat `is_a` ontology hierarchy as a disease assertion.

Map explicit phenotype records to appropriate SDK definitions using deterministic namespace IDs and provenance. Keep unsupported relations, negated/obsolete annotations, unknown diseases and missing references visible as diagnostics. Determine whether new relation kinds are necessary; do not force unsupported ontology semantics into the eight v1 relation kinds. If required, propose an additive schema update with tests and an ADR.

Deliver one runnable import → staging → validation → explicit review → export slice with synthetic term/association fixtures before starting the next adapter.

## 4. MAxO and separately versioned annotations

Implement MAxO ontology parsing (official OBO/JSON/OWL as actually available) independently from its annotation adapter. Reuse mature ontology/XML parsers where appropriate and disable external XML entity resolution. Definitions become procedures or treatments only when source semantics justify the type. Preserve disease/phenotype/action external IDs and evidence. Report annotations that lack a target definition; never invent disease-treatment links from text matching. Medical multi-way annotation semantics may need an additive assertion model, documented before export.

## 5. ICD-10-GM, then LOINC and OPS

Add individual adapters in this order using the exact official licensed machine-readable formats. ICD diagnosis classifications are not symptom or treatment recommendations. LOINC identifies observations and panels; it does not supply clinical cutoffs or test performance by itself. OPS classifications do not supply treatment recommendations. Add explicit format options/recognition with useful mismatch diagnostics; support additional formats only with representative fixtures and documented field mappings.

## 6. Matching, validation and integration

Use explicit cross-references as authoritative links; synonym matching produces review candidates only. Never infer probabilities, criteria or advice. Preserve source/version/license/external ID/import time per assertion, including classifications, synonyms and evidence. Define a merge policy that retains conflicting assertions instead of overwriting provenance. Connect exporter to ContentValidator and versioned schemas, then add a CLI integration fixture runnable from README. Publish exact imported/skipped field and relation tables for each adapter.

## Required tests / completion gate

- Representative synthetic HPO and MAxO term and annotation fixtures; valid, malformed and unsupported versions/formats.
- Stable IDs, alternate IDs, obsolete/negated records, duplicates and non-ASCII text.
- Provenance and original license retained through normalization, staging, review and export.
- Explicit references, unresolved references, conflict reports, missing dependencies and no invented associations.
- Repeated import is deterministic; canceled/failed imports leave no partial release.
- Unreviewed/rejected records cannot enter approved export; closure failures are reported.
- Validated output passes SDK validators and published JSON schemas; exported content loads in the foundation sample or a dedicated importer sample.
- Windows/Linux CI, documented offline invocation, Release build/tests, and a concrete hand-off format for Phase 3.
