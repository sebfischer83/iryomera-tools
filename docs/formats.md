# JSON exchange formats v1

All public exchange contracts are in `schemas/v1/contracts.schema.json` (`$defs`). Each has a small `*.schema.json` entry file. Draft: JSON Schema 2020-12. `$id` values are stable identifiers under `https://schemas.iryomera.org/v1/`; they are **not a hosted service**. Resolve entry files against the bundled contracts schema locally; no network is needed. Regenerate using `python3 scripts/generate-schemas.py` and run the schema tests after any change.

## Encoding and compatibility

UTF-8 with LF formatting newlines on every platform, camelCase property names, case-sensitive identifiers, string enums only, strict JSON numbers. `schemaVersion` is `1.0.0`. Package and individual assertion versions are independent canonical SemVer 2 strings. Dependency ranges use NuGet.Versioning notation: `[1.0.0]` is exact, `[1.0.0,2.0.0)` is a bounded interval, `1.0.0` is a minimum. Prefer bounded intervals for published content.

Unknown additive properties are accepted and preserved by `JsonExtensionData` on contracts, including nested records. Do not use extension fields to bypass typed relationships or hide medical facts in gameplay. Unknown `kind` values and unknown enum values are errors. Duplicate JSON properties are rejected. Missing C# `required` members fail deserialization; explicit nulls are diagnosed by validation. Optional arrays/maps default to empty and optional nullable properties can be omitted. Always emit `schemaVersion`; defaults permit constructing current-version documents in code, but published JSON must pass the schema.

Use `ContentJson.Options` or `ContentJson.ReadAsync<T>` / `WriteAsync<T>`. Serialize definitions through the `ContentDefinition` base type to retain the `kind` discriminator; `WriteAsync` also handles a concrete definition argument. Deserialize standalone definitions with `ReadAsync<ContentDefinition>` and pattern-match the derived type. `ContentJson.ReadAsync<DiseaseDefinition>` also reads the discriminator and rejects a mismatching kind. Raw System.Text.Json callers should deserialize through the base type. Streams remain owned by the caller. There is no automatic approval, file I/O policy or network access.

## Contract catalog

| Contract | Fields and rules |
| --- | --- |
| ContentPackage | `schemaVersion`, `manifest`, `definitions`, `relations`, `medicalAnnotations`, optional `referenceRanges`, `diagnosticRecommendations`, `diagnosticSimulation`, `gameplay`; atomic exchange envelope |
| ContentPackageManifest | `schemaVersion`, `id`, `namespace`, `version`, `license`, `dependencies` |
| ContentDefinition | Abstract tagged union with `kind`, `id`, `nameKey`, optional `descriptionKey`, `version`, `provenance`, `classifications`, `synonymKeys` |
| Definition variants | `disease`, `symptom`, `finding`, `examination`, `observation`, `treatment`, `medicalProcedure`, `medicalDepartment`, `medicalEquipment`, `staffQualification`, `room`, `furniture`, `staffRole` |
| ContentRelation | `id`, `version`, `kind`, `fromId`, `toId`, `provenance`; directional assertion |
| Provenance | `source`, `externalId`, `sourceVersion`, `license`, `importedAt`, `reviewStatus` (default `unreviewed`), optional `reviewedBy`/`reviewedAt`, `evidence` |
| EvidenceReference | `citation`, optional absolute `uri` and `evidenceCode`; does not invent a grading system |
| ExternalIdentifier | `system`, `value`, `provenance`; external classification independent of internal ID |
| ContentDependency | `packageId`, `versionRange` |
| GameplayBalance | `definitionId`, optional normalized `rarity`, `severity`, `diagnosticDifficulty`, `simulatedSuccessProbability`; nonnegative `treatmentDurationSeconds`, `examinationDurationSeconds`, `examinationCost`, `treatmentCost`; nonnegative quantities in `requiredResources` |
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
| examinationObservation | ExaminationDefinition | ObservationDefinition |
| examinationRoom | ExaminationDefinition | RoomDefinition |
| examinationQualification | ExaminationDefinition | StaffQualificationDefinition |
| examinationPrerequisite | ExaminationDefinition | ExaminationDefinition (required first; acyclic) |
| treatmentQualification | TreatmentDefinition | StaffQualificationDefinition |
| roomFurniture | RoomDefinition | FurnitureDefinition |

Phase 1 does not infer department, room or staffing assignments for diseases. These should be added as sourced assertions or explicitly simulation requirements when a later phase needs them; they must not be guessed from unrelated facts. Several assertions with the same endpoints can coexist if they have distinct IDs and provenance.

## Validation layers

1. Parse via `ContentJson`: syntax, types, required members, duplicate properties and enum discriminators.
2. Validate JSON against the appropriate schema: structural shape, ID patterns, ranges, timestamps (enable `format` assertions), required audit metadata. The SDK bundles schemas and JsonSchema.Net; ContentDocuments composes raw schema and semantic validation offline.
3. Call `ContentValidator.Validate(package, installedPackages)` and `ManifestValidator.Validate(mod, installedPackages)`: global uniqueness, version ranges, namespace ownership, reference existence and endpoint types, dependency graph, review metadata and balance constraints.
4. Call `LocalizationValidator.Validate(catalogs, usedKeys)` for language completeness, key usage and parsed placeholder parity. `GetContentKeys(package)` provides definition name/description/synonym keys, result category labels, recommendation rationales and simulated text keys; add mod/UI/dynamic keys separately.

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

## Diagnostic content additions (library 0.4.0)

See [the diagnostic contract reference](diagnostic-content.md) and [ADR 0008](architecture/0008-diagnostic-content.md) for optional observation result descriptors, independently sourced intervals/recommendations, new examination relation kinds and explicitly simulation-only configuration. Dedicated entry schemas cover `observationResultDefinition`, `observationCategory`, `patientContextCriterion`, `observationReferenceRange`, `diagnosticRecommendation`, `simulatedObservationDistribution`, `simulatedManifestation`, `examinationResultPolicy` and `diagnosticSimulationSettings`. Original valid v1 content remains readable; new contracts require updated binaries.

Studio release audit files have their own versioned `schemas/studio/v1` bundle and `studioReleaseReport` entry. They record project revision, explicit rights evidence, approved record provenance and record-specific permission digests. SQLite snapshots are internal persistence, not a separately promised public format or game save format. A Studio ZIP is an approved content download, not a ModKit archive format.

## Mod assets and placement (library 0.5.0)

ContentDefinition has optional `assets` (AssetReference list: path/kind/license/attribution); FurnitureDefinition has optional `placement` with FurnitureFootprint and InteractionPoint data. Null fields are omitted to preserve legacy wire shape/digests. Explicit occupied cells are unique X/Z cells inside a positive rectangle; default occupancy fills it. Interaction qualifications use normal dependency-aware definition references. The ModKit profile additionally resolves resource keys containing a colon as content IDs; legacy SDK resource vocabularies stay opaque, and unqualified symbolic resource names remain valid. Standalone v1 schemas cover all five new contracts. See [the ModKit guide](modkit.md) and [ADR 0010](architecture/0010-data-only-mod-packages.md).

## Catalog asset references and mounting (library 0.7.0)

ContentDefinition has optional `catalogAssets`, omitted when null: CatalogAssetReference entries with `catalogId` (content ID syntax, for example `iryomera.assets:wall.notice_board`) and `kind` (`modelGlb`, `texture`, `icon`). They name assets that are never shipped, such as purchased base-game models; the game resolves them from its private catalog and falls back procedurally. They have no path, license or ModKit inventory entry, and the SDK never resolves them. Codes: `catalog-asset` (ID or kind), `duplicate-catalog-asset` (same ID and kind twice in one definition). `AssetReference` is unchanged; older readers keep `catalogAssets` as an additive property.

FurniturePlacement has optional `mount` (`floor`, `wall`, `ceiling`, `surface` since 0.9.0; absent means floor) and `mountHeightMeters`, both omitted when null. Wall: X along the wall, Z into the room (+Z), origin on the wall surface; the height is measured from the storey floor to the origin, requires `mount: wall`, must be nonnegative and has no SDK upper bound. Ceiling: origin at the ceiling, the model hangs towards −Y. Code: `furniture-mount`. Wall edges, doors, storey height and whether wall/ceiling footprints block floor cells are game rules. See [ADR 0013](architecture/0013-catalog-assets-and-mounting.md).

## Surfaces and equipment relations (library 0.9.0)

FurniturePlacement has optional `surfaces`, omitted when null: FurnitureSurface entries with `key` (stable, unique per placement), `heightMeters` (> 0) and the rectangle `x`, `z`, `width`, `depth` in local meters from the footprint's lower X/Z corner, Y up. The footprint covers the whole floor projection of the item, including overhanging tops, so a surface must lie inside the footprint rectangle (`width × cellSizeMeters` by `depth × cellSizeMeters`). Items placed on a surface use `mount: surface`: their origin is on the surface plane and they stand towards +Y; `mountHeightMeters` stays wall-only. Small items use a smaller `cellSizeMeters`. Code: `furniture-surface`. Whether an item fits a surface, stacking and ceiling clearance are game rules. Which items may stand on which surface is not part of this version.

New relation kinds `treatmentEquipment` (treatment → medical equipment) and `roomEquipment` (room → medical equipment). Several relations express several assignments. ContentRelation has an optional `requirement` (`required` or `optional`, absent means optional), allowed only on `roomEquipment`; the game counts a room as complete only when all required equipment is placed. Department-to-equipment links are derived by the game from a department's examinations and treatments, so there is no department relation. Code: `relation-requirement`. Older readers reject the new relation kinds, as with earlier relation additions; content without them stays readable. See [ADR 0014](architecture/0014-equipment-relations.md).

## Room contents and surface categories (library 0.10.0)

`requirement` is also allowed on `roomFurniture`. Room-type relations now describe what a player can place in a room and is shown: `required` items are shown and must be placed for the room to be complete, `optional` items are shown and may be placed, and items without a `roomFurniture` or `roomEquipment` relation to the room type are not offered there. Defaults differ to keep existing data: an absent value means **required** for `roomFurniture` (its meaning before 0.10.0) and **optional** for `roomEquipment`. `RoomContents.EffectiveRequirement` applies the default, and `RoomContents.Placeable(packages, roomId)` returns the offered items ordered by definition ID; an item linked several times (for example by the base game and a mod) is listed once and is required if any link requires it.

FurnitureSurface has optional `accepts` and FurniturePlacement optional `surfaceCategories` (only with `mount: surface`), both lists of unique stable keys omitted when null. A surface without `accepts` takes any surface-mounted item that fits; otherwise the item must share at least one category. `FurnitureSurfaces.Accepts(surface, item)` implements the rule. Code: `furniture-surface-category`. Categories are presentation data and can be extended by mods. See [ADR 0013](architecture/0013-catalog-assets-and-mounting.md) and [ADR 0014](architecture/0014-equipment-relations.md).

## Gameplay overrides (library 0.11.0)

ContentPackage has optional `gameplayOverrides`, omitted when null; older readers keep it as an additive property. A GameplayOverride has `schemaVersion`, `id` (owned by the package namespace), `version`, `simulationOnly: true`, `targetDefinitionId` and only the balancing fields it sets (the `GameplayBalance` fields and ranges; `requiredResources` merges per key and cannot remove keys). The target must be a definition of a declared dependency (base game or another mod); overriding your own definitions uses `gameplay` instead. Codes: `gameplay-override-own`, `duplicate-gameplay-override`, `gameplay-override-empty`, `gameplay-override`, plus the existing `balance-range`, `resource-key`, `missing-reference` and `undeclared-dependency`.

`GameplayResolver.Resolve(loadOrder)` returns the effective balance per definition and the conflicts. The caller (the game's mod settings) passes the load order; each definition starts from the gameplay record of its own package, then gameplay records for other packages' definitions and overrides apply field by field in load order, the last package winning. When two or more packages set the same field of the same target, a `gameplay-override-conflict` (target, field, packages, winner) is reported as a warning, separate from validation errors. ModKit `validate` prints these conflicts for the supplied dependencies and the mod; `inspect` lists override targets. Medical content, relations, name pools and formats are never overridden. See [ADR 0015](architecture/0015-gameplay-overrides.md).

## Movable furniture (library 0.12.0)

InteractionPoint has an optional `kind` (stable key). Standard kinds: `seat` (a seated person or patient), `lying` (bed, stretcher, couch), `handle` (where the pushing person stands), `operator` (where staff operates a device or treats at the item, also for fixed items) and `entry` (the side to approach before taking a seat or lying point; the game picks one when absent). Mods may add other keys. Keys stay unique per item, so two handles are `handle_left` and `handle_right` with kind `handle`.

`headingDegrees` is the facing of the person at the point: 0° looks along local +Z, increasing counterclockwise seen from above, so 90° is +X, 180° is −Z and 270° is −X (a right-handed rotation about +Y). For a `lying` point the heading is the direction from feet to head (clarified in 0.14.2; earlier versions left it open). Coordinates stay local meters from the footprint's lower X/Z corner, Y up; the game converts to its engine axes.

FurnitureDefinition has optional `mobility`, meaning movement during play rather than the player rearranging items (the game allows that for every item): `fixed` (default) stays where it was placed, `movable` is carried by staff to where it is needed without a patient on it (mobile device, IV stand, chair moved to a bed), and `pushable` is pushed by one person with a patient on it (wheelchair, mobile bed). The optional `transport` (`patientCapacity` ≥ 1, `posture` `seated` or `lying`, optional `requiredQualificationId` for the pushing staff) describes the patients it carries. Both fields are omitted when null. Rules (`furniture-transport`): transport requires `pushable` (since 0.12.1; 0.12.0 also allowed `movable`); it needs at least `patientCapacity` points of kind `seat` or `lying` matching the posture; `pushable` needs at least one `handle`. These are game and simulation rules, not medical statements. See [ADR 0016](architecture/0016-movable-furniture.md).

## Room rules, staff roles, routing and capacity (library 0.13.0)

RoomDefinition has optional `minimumCells` (minimum floor area in grid cells; rooms may have any shape made of cells) and `requiresDoor`, omitted when null (`room-size` for values below 1). These are game layout rules.

New relation kinds: `examinationStaffRole` (examination → staff role that performs it; additional qualifications stay on `examinationQualification`) and `diseaseDepartment` (disease → medical department) for routing. `diseaseDepartment` may carry an optional `priority` (≥ 1; 1 is the preferred department, higher numbers are fallbacks; every linked department is acceptable), allowed only on that kind (`relation-priority`). Older readers reject the new relation kinds.

GameplayBalance and GameplayOverride have an optional `capacity` (nonnegative, simulation only): patients a department or room handles at the same time; mods can override it field by field (ADR 0015). See [ADR 0017](architecture/0017-m7-content-gaps.md).

## Furniture equipment (library 0.14.0)

New relation kind `furnitureEquipment` (furniture → medical equipment): placing the item gives its room that equipment capability, so an examination's `examinationEquipment` can be met by placed furniture. One item may provide several capabilities and several items may provide the same one. `RoomContents.ProvidedEquipment(packages, furnitureIds)` lists the capabilities of placed items; whether a room then satisfies an examination is game logic. Older readers reject the new kind. See [ADR 0018](architecture/0018-demo-clinic.md).

## Purchase prices and wages (library 0.15.0)

GameplayBalance and GameplayOverride have optional `purchaseCost` (game currency to buy one item, typically furniture or equipment) and `wagePerHour` (game currency per game hour, typically for a staff role). Both are nonnegative (`balance-range`), simulation only and omitted when null. Mods override them field by field like the other balancing values, and competing overrides are reported as `gameplay-override-conflict` warnings (ADR 0015). They are game economy values, never real prices or wages. Construction costs for floors, walls and doors are a game setting, not content. See [ADR 0019](architecture/0019-price-balancing.md).

## Departments, rooms and patient flow (library 0.18.0)

New relation kinds `departmentRoom` (medical department → room type the department uses) and `departmentPrerequisite` (department → department that must already be open). `departmentRoom` takes `requirement` (absent means required, as for `roomFurniture`) and an optional `treatmentRoom: true` for the room type where the department treats patients; the flag is allowed only on that kind, the treatment room must be required, and a department has at most one (`relation-treatment-room`). Department prerequisites must be acyclic (`department-cycle`). `Departments.Rooms` and `Departments.Prerequisites` aggregate them over a package set. Older readers reject the new relation kinds.

GameplayBalance and GameplayOverride have optional `unlockCost` (game currency to open a department) and `unlockDischargedPatients` (discharged patients needed first; a whole number), both nonnegative (`balance-range`), simulation only and overridable by mods. Whether absent values mean open is game logic. `treatmentDurationSeconds` applies to any definition, including diseases (time to treat a case). See [ADR 0020](architecture/0020-departments.md).

RoomDefinition has optional `role` (`reception`, `waiting` or `ward`; absent means no role) and `shareable` (several departments may use one room of the type, such as an operating room). Room types without a `departmentRoom` relation belong to the whole clinic; a department may still list a waiting or reception room type so its patients wait there. Beds are ward furniture with a `lying` point; a department with a required ward room is inpatient.

GameplayBalance and GameplayOverride also have `openFromHour` and `openToHour` (whole game hours in [0,24], from before to; 0–24 is always open; they apply to new admissions and referrals), `arrival` (`walkIn` or `emergency`, how a disease's cases arrive) and `stayDurationSeconds` (inpatient stay after treatment; 0 or absent is outpatient). All are simulation only and overridable by mods; `arrival` conflicts are reported like numeric fields. The abstract department `capacity` stays valid but is no longer used by the game.

## Treatments and complications (library 0.19.0)

Relations `treatmentRoom` (treatment → room type) and `treatmentStaffRole` (treatment → staff role; at most one, `treatment-staff-role`) say where and by whom a treatment is done; `Treatments.Playable` is true with at least one room and exactly one role, otherwise the game keeps the disease's values. `diseaseTreatment` accepts `priority` (1 first choice); `Treatments.Options` orders a disease's treatments.

New definition kind `complication` with required `effect` (`extraStay`, `requiredExamination`, `followUpTreatment`) and `timing` (`duringTreatment`, `duringStay`). `treatmentComplication` (treatment → complication) links a risk; `complicationExamination` (complication → examination) and `complicationTreatment` (complication → treatment) are the effect's parameters, allowed only with the matching effect and required by it (`complication-effect`). Follow-up chains must not loop (`treatment-cycle`).

Balancing gains `stayChance` and `complicationChance` (0–1), `stayDurationMaxSeconds` (not below `stayDurationSeconds` in one record) and `extraStayDurationSeconds`. A balancing record or override may target a `diseaseTreatment` or `treatmentComplication` relation ID to set values for that pair (`gameplay-target` for other relations); mods override and conflict on them like on definitions. `Treatments.Effective` merges pair, treatment and disease values in that order and reports each source. Older readers reject the new kind and relation kinds. See [ADR 0021](architecture/0021-treatments-and-complications.md).

## Comfort and insurers (library 0.20.0)

Balancing gains `comfort` (whole number 0–10, typically on furniture, absent 0), `minimumReputation` (0–100, absent 0), `priceFactor` (above 0, absent 1) and `patientShare` (0–1, absent 0). New definition kind `insurer`; `Insurers.All` lists insurers with resolved terms and defaults, `Insurers.SelfPayShare` the rest of 1 for patients without an insurer. Range errors are `balance-range`; `insurer-share` reports shares above 1 in total over a package and its declared dependencies after overrides. Older readers reject the new kind. See [ADR 0022](architecture/0022-comfort-and-insurers.md).

ModKit authoring (`mod.json`) and archive integrity (`checksums.json`) have separate schemas in `schemas/modkit/v1`. No executable/script/plugin payload kind is defined. The archive is not a Godot PCK or game save format. The runtime game remains responsible for asset rendering and interaction behavior.

## Name pools and formats (library 0.6.0)

ContentPackage has optional `namePools` and `nameFormats` lists, omitted when null. Older readers keep them as unknown additive properties instead of rejecting the package. They are package-level records like reference ranges, not definition kinds: they have no relations, gameplay balance or medical review semantics. See [ADR 0011](architecture/0011-name-pools.md).

| Contract | Fields |
| --- | --- |
| NamePool | `schemaVersion`, `id`, `version`, `nameKey`, `country` (ISO 3166-1 alpha-2, uppercase), optional `region` (ISO 3166-2 code of that country), `part` (`given`/`family`), `provenance`, `sources` (NamePoolSource: `key`, `provenance`), `entries` |
| NameEntry | `key` (unique in the pool), `localized` (original script), optional `romanized` and `romanizationSourceKey`, `gender` (`female`/`male`/`unisex`/`unknown`; required for given names, optional for family names), `attestations` |
| NameAttestation | `sourceKey`, optional `externalId`, `rank`, `count`, `proportion`, `birthYearFrom`/`birthYearTo` (cohort; one year has equal bounds) |
| NameFormat | `schemaVersion`, `id`, `version`, `country`, optional `region`, `order` (`givenFamily`/`familyGiven`), `familyNameParts` (1–3, default 1), `localizedSeparator` (`" "` default or `""`; applies only to `localized` parts, romanized parts are always joined with one space), `provenance` |

Name strings are data, not localization keys; only the pool's display `nameKey` is translated and checked by the localization validator. Statistics are kept per source and never combined. Every entry needs at least one attestation of a declared pool source, so hand-authored mod entries also keep lineage. Name text must be NFC, at most 100 UTF-16 code units, without control, bidirectional control or non-space whitespace characters, and without surrounding or repeated spaces; zero-width joiners remain valid. Romanized values must use Latin letters, combining diacritics and name punctuation. These checks use fixed code point sets so net8 and net10 give the same result. Patronymic rules are not part of v1.

`NamePools.Select(packages, country, part, region)` returns country-wide pools plus pools of the requested region, ordered by pool ID and then entry key with ordinal comparison, independent of package or file order. The game's seeded selection depends on that order; changing it is a breaking change. `NamePools.ResolveFormat` returns the regional format, otherwise the country format. Both compare `country` and `region` ordinally, so callers pass uppercase codes. Whether romanized names of a family-first format are displayed in Western order is decided by the game; the format carries no field for it. Two formats for the same country/region anywhere in a validated package set report `name-format-conflict`; there is no last-wins override. Other name diagnostics use the `name-*` prefix (`name-text`, `name-romanized`, `name-source`, `name-attestation`, `name-gender`, `name-statistic`, `name-cohort`, `name-country`, `name-region`, `name-duplicate-key`, `name-duplicate-entry`, `name-empty-pool`, `name-format`). Standalone v1 schemas cover `namePool`, `namePoolSource`, `nameEntry`, `nameAttestation` and `nameFormat`. Name origin must never feed medical or balancing data.
