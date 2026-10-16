# JSON exchange formats v1

All public exchange contracts are in `schemas/v1/contracts.schema.json` (`$defs`). Each has a small `*.schema.json` entry file. Draft: JSON Schema 2020-12. `$id` values are stable identifiers under `https://schemas.iryomera.org/v1/`; they are **not a hosted service**. Resolve entry files against the bundled contracts schema locally; no network is needed. Regenerate using `python3 scripts/generate-schemas.py` and run the schema tests after any change.

## Encoding and compatibility

UTF-8 with LF formatting newlines on every platform, camelCase property names, case-sensitive identifiers, string enums only, strict JSON numbers. `schemaVersion` is `1.0.0`. Package and individual assertion versions are independent canonical SemVer 2 strings. Dependency ranges use NuGet.Versioning notation: `[1.0.0]` is exact, `[1.0.0,2.0.0)` is a bounded interval, `1.0.0` is a minimum. Prefer bounded intervals for published content.

Unknown additive properties are accepted and preserved by `JsonExtensionData` on contracts, including nested records. Do not use extension fields to bypass typed relationships or hide medical facts in gameplay. Unknown `kind` values and unknown enum values are errors. Duplicate JSON properties are rejected. Missing C# `required` members fail deserialization; explicit nulls are diagnosed by validation. Optional arrays/maps default to empty and optional nullable properties can be omitted. Always emit `schemaVersion`; defaults permit constructing current-version documents in code, but published JSON must pass the schema.

Use `ContentJson.Options` or `ContentJson.ReadAsync<T>` / `WriteAsync<T>`. Serialize definitions through the `ContentDefinition` base type to retain the `kind` discriminator; `WriteAsync` also handles a concrete definition argument. Deserialize standalone definitions with `ReadAsync<ContentDefinition>` and pattern-match the derived type. `ContentJson.ReadAsync<DiseaseDefinition>` also reads the discriminator and rejects a mismatching kind. Raw System.Text.Json callers should deserialize through the base type. Streams remain owned by the caller. There is no automatic approval, file I/O policy or network access.

## Contract catalog

| Contract | Fields and rules |
| --- | --- |
| ContentPackage | `schemaVersion`, `manifest`, `definitions`, `relations`, `medicalAnnotations`, `gameplay`; atomic exchange envelope |
| ContentPackageManifest | `schemaVersion`, `id`, `namespace`, `version`, `license`, `dependencies` |
| ContentDefinition | Abstract tagged union with `kind`, `id`, `nameKey`, optional `descriptionKey`, `version`, `provenance`, `classifications`, `synonymKeys` |
| Definition variants | `disease`, `symptom`, `finding`, `examination`, `observation`, `treatment`, `medicalProcedure`, `medicalDepartment`, `medicalEquipment`, `staffQualification`, `room`, `furniture`, `staffRole` |
| ContentRelation | `id`, `version`, `kind`, `fromId`, `toId`, `provenance`; directional assertion |
| Provenance | `source`, `externalId`, `sourceVersion`, `license`, `importedAt`, `reviewStatus` (default `unreviewed`), optional `reviewedBy`/`reviewedAt`, `evidence` |
| EvidenceReference | `citation`, optional absolute `uri` and `evidenceCode`; does not invent a grading system |
| ExternalIdentifier | `system`, `value`, `provenance`; external classification independent of internal ID |
| ContentDependency | `packageId`, `versionRange` |
| GameplayBalance | `definitionId`, optional normalized `rarity`, `severity`, `diagnosticDifficulty`, `simulatedSuccessProbability`; nonnegative `treatmentDurationSeconds`, `examinationCost`, `treatmentCost`; nonnegative quantities in `requiredResources` |
| ModManifest | package metadata plus `nameKey`, `author`, `compatibleGameVersions`, relative `contentFiles`, `supportedLanguages`; installation format remains planned |
| LocalizationDefinition | `schemaVersion`, `namespace`, `language`, `entries` key/template dictionary |

Namespace segments and local ID segments start with a lowercase ASCII letter, followed by lowercase letters, digits or underscores. Dots separate segments. Exactly one colon separates namespace and local name, e.g. `author.demo:scan.basic`. All definitions/relations in a package must be owned by its manifest namespace. Content IDs need not contain English or German words; translated names never appear in reference fields.

## Relation endpoints

| `kind` | From | To |
| --- | --- | --- |
| diseaseSymptom | DiseaseDefinition | SymptomDefinition |
| diseaseFinding | DiseaseDefinition | FindingDefinition |
| examinationFinding | ExaminationDefinition | FindingDefinition |
| diseaseExamination | DiseaseDefinition | ExaminationDefinition |
| diseaseTreatment | DiseaseDefinition | TreatmentDefinition |
| examinationEquipment | ExaminationDefinition | MedicalEquipmentDefinition |
| treatmentQualification | TreatmentDefinition | StaffQualificationDefinition |
| roomFurniture | RoomDefinition | FurnitureDefinition |

Phase 1 does not infer department, room or staffing assignments for diseases. These should be added as sourced assertions or explicitly simulation requirements when a later phase needs them; they must not be guessed from unrelated facts. Several assertions with the same endpoints can coexist if they have distinct IDs and provenance.

## Validation layers

1. Parse via `ContentJson`: syntax, types, required members, duplicate properties and enum discriminators.
2. Validate JSON against the appropriate schema: structural shape, ID patterns, ranges, timestamps (enable `format` assertions), required audit metadata. The SDK bundles schemas and JsonSchema.Net; ContentDocuments composes raw schema and semantic validation offline.
3. Call `ContentValidator.Validate(package, installedPackages)` and `ManifestValidator.Validate(mod, installedPackages)`: global uniqueness, version ranges, namespace ownership, reference existence and endpoint types, dependency graph, review metadata and balance constraints.
4. Call `LocalizationValidator.Validate(catalogs, usedKeys)` for language completeness, key usage and parsed placeholder parity. `GetContentKeys(package)` provides definition name/description/synonym keys; add mod/UI/dynamic keys separately.

Schemas intentionally allow extensions. They cannot express cross-file uniqueness, dependency ranges, locale availability, reference typing or placeholder parity. They are not a replacement for semantic validators. Diagnostics are `ValidationIssue(Code, Path, Message)` and are not stored in content JSON. Import/export stream methods propagate I/O exceptions and cancellation. ContractShape.Validate guards non-null object graphs before semantic validation.

## Phase 1 review additions (library 0.2.0, compatible schema 1.0.0)

`ContentPackage.medicalAnnotations` is optional and defaults to an empty list. Its independent `MedicalAnnotation` records preserve original predicates, explicit subject/object resolutions, optional disease/extension context, nullable negation, exact observational frequency strings, qualifiers, onset, sex/aspect, modifiers, provenance and complete raw source fields. `AnnotationReference` and `ObservationFrequency` have dedicated entry schemas. See [ADR 0003](architecture/0003-medical-annotations.md) and the synthetic `examples/synthetic/annotations.json` package. The SDK never converts contextual annotations into unqualified binary relations or game probabilities.

Wire-required `schemaVersion` is now enforced with JsonRequired on **every** version-bearing exchange object, including nested package manifests and standalone annotations. It still defaults to 1.0.0 for C# object construction. Existing valid JSON already contains these schema-required fields and remains valid; previously accepted malformed JSON with omitted versions must be corrected rather than silently defaulted. Explicit null/wrong values remain invalid.

### Unified raw-document entry

```csharp
await using var input = File.OpenRead("content.json");
var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(input,
    installedPackages: installedPackages, cancellationToken: cancellationToken);
if (!result.IsValid)
    foreach (var issue in result.Issues)
        Console.WriteLine($"{issue.Code} {issue.Path}: {issue.Message}");
```

`ContentDocuments.ReadValidatedAsync<T>` evaluates the **original JSON** against the embedded matching schema before deserialization/defaults. JsonSchema.Net is now a runtime dependency; the bundled schema is resolved locally without a network fetch. Duplicate JSON properties are rejected. Invalid JSON/schema yields diagnostics and no Value. On schema-valid content, Value remains available with semantic errors so an editor can inspect it; `IsValid` requires a non-null Value and zero issues. Cancellation/I/O errors propagate instead of being reported as content success.

Default semantic rules cover ContentPackage (complete dependency/reference graph), ModManifest and ContentPackageManifest. Standalone leaf contracts receive schema/shape checks; graph reference semantics need the owning ContentPackage or an explicitly supplied validator. The registered CLR contract types are explicitly allowlisted. A supplied `semanticValidator` callback composes domain checks while sharing exactly the same raw schema pipeline.

`LocalizationDocuments.ReadValidatedAsync` composes that pipeline with `LocalizationValidator`, other loaded catalogs, the usage set and required languages. Supply English alongside a German catalog to validate placeholder parity; it never bypasses missing schemaVersion. Raw `ContentJson.ReadAsync` and direct System.Text.Json using ContentJson.Options also enforce JsonRequired members, but do not replace schema/graph validation. C# object-only validation cannot discover whether a caller previously discarded an absent wire field; validate the original bytes at the boundary.

## Phase 2 review additions (library 0.3.0)

`ObservationDefinition` is a neutral observation/panel/document code using the standard definition fields and `kind: "observation"`. Its standalone schema and content union are additive to v1. It does not satisfy examination relation endpoints and does not imply a playable workflow. All original v1 examples still validate. A new observation needs the 0.3.0 reader; unknown discriminators remain errors in older binaries. See [ADR 0006](architecture/0006-observation-definitions.md).

The importer now exports contextual HPO/MAxO claims in `medicalAnnotations` as well as the compatible source sidecar. Raw frequency and fields survive; MAxO disease/extension contexts are explicit and negation stays tri-state. No new medical relation or numeric assumption is inferred. Importer workspaces and reports have their own schemas under `schemas/importer/v1`; their optional rights scope, permission evidence, bulk audits and sourceClaims are documented in [the CLI guide](medical-importer.md).
