# Reusable examination and diagnostic content

These contracts describe authored content. They do not execute examinations or diagnose patients. [ADR 0008](architecture/0008-diagnostic-content.md) records the medical/simulation boundary, compatibility decision and private-game runtime plan.

Deutsch: [Modellübersicht mit Beziehungsgrafik und JSON-Beispielen](diagnosemodell.md).

## Meaning and relationships

| Definition | Meaning | Illustrative distinction |
| --- | --- | --- |
| DiseaseDefinition | Disease identity and independently sourced manifestation relationships | A disease can reference many shared examinations |
| SymptomDefinition | Subjective patient report | Reported pain |
| FindingDefinition | Objective clinical sign | Observed pressure tenderness |
| ObservationDefinition | Parameter or qualitative result definition, not a patient measurement | CRP concentration; a rhythm category |
| ExaminationDefinition | Reusable action with possible outputs and requirements | CRP determination; ECG |
| TreatmentDefinition | Treatment/action identity | Independently reviewed treatment relationship |
| DiagnosticRecommendation | Optional sourced disease/examination/context assertion | Reviewed rationale, not an executable physician checklist |

The clinical words in this table illustrate distinctions only. No disease associations, real reference intervals or treatment recommendations have been added. A future shared catalog of 30–50 examinations can serve hundreds of diseases without embedding copies in diseases.

New relation kinds are `examinationObservation`, `examinationRoom`, `examinationQualification` and `examinationPrerequisite`. Existing `examinationEquipment`, disease/symptom/finding/examination/treatment relations remain usable. A prerequisite points from the dependent examination to the examination required first. Cycles fail validation. Each relationship remains an independently identified, versioned, sourced and reviewed assertion.

## Additive schema changes, library 0.4.0

The v1 bundle retains `schemaVersion: "1.0.0"`. Old valid packages and neutral LOINC observations remain valid; new fields are optional. New enum values/contracts require a capable reader. Older binaries are not claimed to understand the new relation kinds.

| Change | Contract and constraints |
| --- | --- |
| `ObservationDefinition.result` | Optional `ObservationResultDefinition`: `valueKind` numeric/categorical/boolean/text, optional exact `unitCode`, `categories`, `patientContextKeys` |
| Categories | Stable local `key`, DE/EN `nameKey`, optional `findingId`; only categorical results have categories |
| `ContentPackage.referenceRanges` | Independent `ObservationReferenceRange`: ID/version, observation, exact unit, inclusive numeric bounds, context, provenance with evidence |
| `ContentPackage.diagnosticRecommendations` | Independent `DiagnosticRecommendation`: disease/examination IDs, optional context definition IDs, patient predicates, rationale key, provenance with evidence |
| Context | `PatientContextCriterion`: stable factor key and exactly one numeric-bound, categorical or boolean condition; a list is a conjunction |
| `ContentPackage.diagnosticSimulation` | Optional explicitly `simulationOnly` configuration, described below |
| `GameplayBalance.examinationDurationSeconds` | Optional nonnegative simulation duration; existing costs/difficulty/resources retained |

Standalone entry schemas exist for all nine new record types under `schemas/v1`. Version-bearing records require `schemaVersion` in original JSON, including nested records. Numeric bounds, type/unit/category compatibility, source evidence, reference closure, namespace ownership, global ID uniqueness, dependencies, prerequisite cycles and probabilities are checked by `ContentValidator`. `ContentDocuments.ReadValidatedAsync<ContentPackage>` combines original-byte schema validation with those graph checks offline. Leaf schemas alone cannot validate graph semantics. Schemas preserve unknown additive extension fields.

Units are exact codes, preferably UCUM; there is no automatic conversion. Unitless numeric observations can use `1`. A clinical reference range needs traceable evidence and a separate review; import is never approval. No reference range is inferred from LOINC. Overlapping context intervals and unit conversion policy require an explicit future authoring/runtime decision.

## Separate simulation configuration

`SimulatedObservationDistribution` maps disease/observation/context IDs to **authored game parameters**: both numeric bounds, nonnegative category weights with a positive total, a true probability in [0,1], or a localized text key. Exactly one shape must match the observation descriptor. Category weights are weights, not asserted clinical frequencies; this contract does not select a distribution algorithm.

`SimulatedManifestation` contains a disease, symptom/finding, game probability and visibility (`initiallyVisible`, `afterExamination`, `hidden`). `ExaminationResultPolicy` defines visibility and repetition: default `untilPatientStateChanges`, or `afterInterval` with a positive interval in simulated seconds. Each is explicitly `simulationOnly: true`; medical source provenance does not carry these invented values. Existing gameplay balancing holds costs, durations, difficulty, success and resource quantities separately.

## Tested fictional example

`examples/synthetic/diagnostics.json` contains three fictional amber/azure/coral conditions using five shared fictional examinations:

| Examination ID suffix | Observation | Kind |
| --- | --- | --- |
| `history` | `narrative` | text |
| `pressure_check` | `pressure_present` | boolean |
| `sample_measurement` | `sample_index`, unit `1` | numeric |
| `image_scan` | `image_pattern` | categorical, optionally linked to a finding |
| `rhythm_trace` | `rhythm_pattern` | categorical, optionally linked to a finding |

All IDs use `diagnostics_demo:`. Each disease references all five reusable examinations. The fixture has room/equipment/qualification requirements, an acyclic prerequisite, 15 separately marked simulated distributions, one **fictional** sourced interval and one **fictional** recommendation. Nothing is medically approved. `diagnostics.en.json` and `diagnostics.de.json` include all names, category labels, rationale and simulated text. Regenerate deterministically with `python scripts/generate-diagnostic-example.py`.

Example ID `diagnostics_demo:sample_measurement` keeps the same identity in English (“Fictional sample analysis”) and German (“Fiktive Probenanalyse”). A language switch does not alter package references or saves.

## Studio, Godot and data mods

Studio loads the package and both catalogs, edits definitions/relations once, and offers validated advanced JSON editors for result descriptors, reference intervals, recommendations and separate simulation settings. Medical edits invalidate approval; simulation edits preserve medical provenance. Later form widgets can replace JSON editing without changing formats. See [Studio guide](content-studio.md).

Godot consumes the same SDK net8.0 package using `ContentDocuments`, plus catalogs through `IContentLocalizer`. The real-engine integration test loads the five-examination example and validates all result kinds and DE/EN keys. The public repo never depends on the private game. Data mods may reference existing examination/observation IDs through declared versioned package dependencies or define new namespaced types/relations/catalogs. All formats are declarative; no foreign DLL or script is required. Mod archive creation/loading remains Phase 4.

The private game will own seeded, versioned random generation, persistent latent patient observations, state/time transitions, examination execution, diagnosis decisions and treatment/progression. Cache results by patient state and observation identity so shared examinations reveal the same underlying state. A repeat without a modeled state change must stay consistent. Its tests must use an explicitly pinned PRNG/seed; no random runtime or patient simulator is implemented in Tools.

The planned physician AI can compare differential diagnoses and expected simulated information gain against cost, duration, urgency, resources and qualifications, using recommendations as optional context. Medical likelihoods and recommendations must never be invented from similar names. Remaining decisions include PRNG version, multi-disease composition, context precedence, controlled factor vocabularies, reference sources/unit conversions and AI scoring; none are silently decided by a content import.
