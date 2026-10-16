# ADR 0005 — Record rights, controlled reviews and cross-batch assertions

Status: accepted, Phase 2 review, 2026-10-16. Supplements [import staging](0003-importer.md) and [canonical medical annotations](0003-medical-annotations.md).

## Additional rights are an export gate

Batch permission remains mandatory. `RightsDecision.recordId` optionally narrows a decision to one record in one immutable batch. A record-level blocked/undecided decision overrides batch permission. Explicit source `additional_terms`/`usage_restrictions`/`rights`/`license`/`license_url` and LOINC `EXTERNAL_COPYRIGHT_NOTICE` also require this scoped review. HPOA IEA claims (including disease labels derived from those rows) and any record explicitly referring to OMIM require a separate permitted decision with a nonempty `permissionEvidence` reference. This is intentionally conservative; an OMIM identifier is not by itself proof of infringement or of permission. Evidence must identify the terms/authorization and intended use, not just repeat a warning. The CLI records trusted operator judgments and cannot verify the legal adequacy of that evidence.

Recompute this policy from original source fields and external IDs on every export, including old workspaces lacking new fields. Never trust an absent warning as permission. Raw imports remain unreviewed. Preserve selected rights history and original licenses in release reports; never include excluded source claims or their record-specific permissions. Permissions do not transfer to another batch, record, release or clinical reviewer. Clinical approval and rights remain separate gates. Keep confidential permission documents outside public releases; use an appropriate evidence reference and supply required redistribution notices in the original license file.

The official HPOA documentation states IEA annotations derived from `omim.txt` require contacting OMIM before reuse in other software. No OMIM/HPOA restricted production files were downloaded or approved in this review. No generic automatic license allowlist is introduced.

## Controlled bulk review

Keep `review --all` strict for compatibility. Add `review-bulk`, read-only unless `--apply` is specified. Build a complete preview before mutation: automatic exclusions cover obsolete terms, non-ACTIVE LOINC entries, record-specific rights restrictions, record validation errors and unavailable/excluded endpoints. Operators can supply an ID-to-reason JSON exclusion object. Unknown IDs, missing reasons or invalid audit metadata fail atomically. Structural workspace errors are not hidden by exclusions.

Persist each run with reviewer/time/note, selected IDs and every automatic/manual exception in `ImportWorkspace.bulkReviews`. On approval runs, exclusions append a rejected decision, revoking any earlier approval for those records. All previous audit entries remain intact. Decisions and audit append in the same atomic workspace transaction. `--apply` recomputes eligibility from the current locked snapshot; a preview is not a signature or authenticated clinical certification. Baseline batch permission is still required at export.

## Identical claims and conflicts

Stable source assertion IDs remain compatible. Identical assertions with the same ID/kind/predicate/endpoints/context/original fields can coexist across batches. Emit one resolved assertion/relation/SDK annotation and retain every **approved** claim and its reviewed provenance in `sourceClaims`. Evaluate each contributing batch/record's rights independently. Unapproved claims never ride along on a matching approved claim. Different original evidence or qualifier rows keep separate IDs; we do not infer clinical equivalence or discard disputed assertions. Conflicting content under one stable ID remains an error.

Explicit primary identifiers win over an active term's alias when the primary record is obsolete: preserve the obsolete identity and emit `shadowed-alternate-id`, rather than silently replacing it. Collisions with active primary records or competing aliases remain errors. This rule was prompted by the complete official HPO/MAxO files. Unlabelled obsolete nodes remain raw source metadata with an explicit diagnostic; no name/definition is fabricated.

## Formats and reproducibility

Importer schema v1 accepts old documents and adds optional rights scope/evidence, bulk audit and source claim lists. `[JsonRequired]` protects schemaVersion; original workspace JSON passes bundled offline JSON Schema validation before deserialization/defaults. Strict older importer readers may reject new optional fields; old-to-new reading is supported, new-to-old reading is not promised.

Adapter **1.1.0** records the changed LOINC/obsolete normalization rules in batch identity. Readers continue accepting adapter 1.0.0 batches with their original IDs. New imports never masquerade as an old adapter's byte-identical output. SDK HPO/MAxO annotations also populate canonical `MedicalAnnotation`, retaining the source sidecar. The annotation ID suffix `.annotation` avoids collisions with legacy binary relation IDs. Frequency remains exact text, negation is tri-state, and no diseaseTreatment relation is inferred.

## Cross-platform canonical bytes

The first opt-in Windows full-file run exposed System.Text.Json's environment-dependent indented newlines: repeated output matched on each host but Linux and Windows digests differed. ContentJson now explicitly emits LF on both frameworks/platforms. Tests assert the newline policy and verify both legacy LF/CRLF batch and assertion hashes. The adapter 1.0.0 reader accepts both original platform hashes without rewriting imported IDs/reviews; new 1.1.0 batches require canonical LF identity. Old Windows and Linux assertion IDs may differ and are not silently renamed or assumed equivalent. Core content IDs are unchanged. Full-file cases pin one normalized digest for both operating systems.
