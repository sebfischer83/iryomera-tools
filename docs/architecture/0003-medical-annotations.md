# ADR 0003: Lossless source medical annotations alongside binary content relations

Status: accepted during the Phase 1 review remediation. This is a Foundation model change, not an importer implementation.

## Problem

The original eight `ContentRelation` kinds express two-entity assertions. HPO disease/phenotype annotations also carry qualifier/negation, observational frequency, onset, sex, modifiers, evidence and source metadata. A MAxO annotation may relate a medical action to a phenotype in a disease context with an external extension, original predicate, curator and dates. Flattening these into an unconditional diseaseTreatment or diseaseFinding relation loses meaning. The source formats also evolve; an unrecognized source column must survive a round trip.

## Decision

Add the independent `MedicalAnnotation` contract and an optional `ContentPackage.medicalAnnotations` collection. Preserve the existing binary relation enum, fields and semantics. Keep schema version 1.0.0 because this is additive within the existing extension-preserving v1 contract; publish updated libraries as 0.2.0. Existing valid package JSON does not need a migration or annotation array.

An annotation has its own stable ID, version, source-assertion kind, exact predicate, subject/object and provenance. `diseasePhenotype` models the HPO-style disease/phenotype assertion; `medicalAction` models the MAxO-style action/phenotype assertion, with optional `diseaseContext` and `extensionContext`. Every `AnnotationReference` preserves the original `externalId` independently of an optional explicitly resolved `contentId`.

Keep all contextual information attached to the assertion:

- Nullable `isNegated`: null means not supplied/unknown; false and true are distinct explicit source states. Never infer it from a missing qualifier or frequency.
- `frequency.sourceValue`: unchanged source text, including fractions, percentages, ontology identifiers or expressions the SDK does not understand. Optional `frequency.term` can record an explicit term reference. There is no conversion into a numeric gameplay probability, no invented denominator, no imputed frequency and no attempt to grade evidence.
- Original qualifier strings, onset reference, sex/aspect strings and modifier references. Retain order where the source supplies it.
- `sourceFields`: complete original annotation fields as arbitrary JSON. This retains additional columns, exact source strings, predicate spelling, extension labels, curator and dates without claiming to interpret them. `JsonExtensionData` additionally preserves future typed-contract metadata at every model level.
- Existing `Provenance` holds source, source record identifier, release version, original license, import timestamp, medical review state and evidence/citations. Provenance applies to the complete assertion; distinct source assertions require distinct records rather than silently overwriting lineage. If the source has no unique row ID, a future importer must identify its row deterministically and document that derived identifier as such.

## Validation and unresolved references

Annotations share the global definition/relation ID uniqueness domain and package namespace/dependency rules. A complete ContentPackage must explicitly resolve the subject, object and any disease context. Missing resolutions produce `unresolved-annotation-reference`, and invalid/missing content IDs, undeclared dependencies or incorrect role types are errors. Disease/phenotype subjects resolve to diseases, phenotype objects to symptoms or findings, and medical-action subjects to procedures, examinations or treatments. These broad accepted types are transport constraints, not clinical classifications inferred from labels.

Contextual extension/onset/frequency/modifier references may remain explicitly external. If they provide a contentId, it is validated against the package/dependency graph. A chemical extension need not be fabricated as an SDK definition merely to make an annotation appear complete. Unknown external references are represented explicitly, never invented or silently removed. Schema validation alone cannot establish clinical correctness or reference closure.

## Projection and compatibility

The SDK does **not** project annotations into binary medical relations, remove qualifiers, infer recommendations, fill missing values or approve source data. Any future projection needs a separate documented policy and review; it must not erase the original annotation. Simulation balancing remains a separate collection and cannot substitute for observational frequency/evidence.

A v0.1 reader can preserve the new optional collection as an extension when editing JSON, but cannot interpret it; it must not be advertised as a medically aware annotation consumer. A game consumer must explicitly support the annotation shape it uses. No breaking changes were made to the original relation kinds or existing valid example IDs. No real HPO/MAxO datasets are included: the new example source fields/values are explicitly synthetic MIT fixtures.

## Official format references

Checked 2026-10-16: [official phenotype.hpoa field description](https://github.com/obophenotype/human-phenotype-ontology/blob/master/docs/annotations/phenotype_hpoa.md) and [official MAxO annotation format](https://github.com/monarch-initiative/maxo-annotations#annotation-format). These references justify preserving the source shape; they do not grant an upstream data redistribution license or medical approval.
