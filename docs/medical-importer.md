# MedicalImporter CLI and public integration API

Target: .NET 10, Windows/Linux, fully local operation. MedicalImporter does not download data, accept upstream terms or provide medical advice. All imports remain unreviewed until an explicit operator decision. See [source formats and legal restrictions](data-sources/README.md) before using real datasets.

## Build and execute

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet run --project src/Iryomera.MedicalImporter -c Release --no-build -- sources list
```

The project also packs as the `iryomera-medical` .NET tool:

```sh
dotnet pack src/Iryomera.MedicalImporter -c Release -o artifacts
dotnet tool install Iryomera.MedicalImporter --version 0.5.0 --add-source ./artifacts --tool-path ./artifacts/tools
# Windows: artifacts/tools/iryomera-medical.exe
# Linux: ./artifacts/tools/iryomera-medical
```

The package is generated locally; it is not claimed to be published on nuget.org. Package installation/restore may need dependencies initially; imported-file processing and tests never need network access. XML external entities/DTDs are not fetched. Inputs are uncompressed local files, limited to 512 MiB by default (`--max-input-bytes` changes this limit).

## Commands

Replace `iryomera-medical` below with the installed executable or `dotnet run --project src/Iryomera.MedicalImporter -c Release --no-build --`.

```sh
iryomera-medical sources list
iryomera-medical import hpo --input ./data/hp.json \
  --source-version 2026-09-01 \
  --source-url https://github.com/obophenotype/human-phenotype-ontology/releases/download/v2026-09-01/hp.json \
  --license LicenseRef-HPO-release-terms --license-file ./data/hpo-terms-and-notices.txt \
  --attribution "HPO Consortium; exact citation/version as required by this release" \
  --imported-at 2026-10-16T12:00:00Z --workspace .iryomera/workspace.json
iryomera-medical validate --workspace .iryomera/workspace.json
iryomera-medical status --workspace .iryomera/workspace.json
iryomera-medical matches --workspace .iryomera/workspace.json --limit 1000
```

The real-data command illustrates metadata fields, **not permission to redistribute HPO**. Check the exact release terms. CLI arguments are single shell values; quote paths/attribution. PowerShell uses its own line-continuation syntax; commands also work on one line.

`--format auto` sniffs JSON/XML/OBO/TSV/CSV and uses known extensions when comment headers fill the sniff buffer. Explicitly choose a supported format when needed. Unknown formats, unsupported source adapters and missing headers fail clearly. Original `SourceVersion` is always mandatory. Detected release metadata is retained separately; a mismatch with the configured version is an error. An import with record-level errors is still staged for inspection and returns exit 2; malformed/unsupported input leaves the old workspace intact.

For reproducibility supply a fixed `--imported-at`, identical source bytes, namespace, original source metadata and importer version. The normalized options and SHA-256 identify the batch; repeated identical imports are idempotent. A reimport with a different timestamp/version/license is a different auditable batch. Input location and output path are not part of the normalized identity. Source streams are hashed before and after parsing to detect changes during a run.

## Review and redistribution

`status` lists batch and record IDs. Review one record independently:

```sh
iryomera-medical review --workspace .iryomera/workspace.json \
  --batch <batch-id> --id <record-id> --status approved \
  --reviewer <reviewer-label> --at 2026-10-16T13:00:00Z --note "Decision and supporting review evidence"
```

`--all` is an explicit bulk operation for the chosen batch and cannot be combined with `--id`. It is useful for synthetic tests; do not confuse it with medical verification. Bulk approval fails atomically if it includes obsolete terms. Valid statuses: `unreviewed`, `inReview`, `approved`, `rejected`. Decisions append to history; timestamps cannot precede import or a previous decision. Current status comes from the latest recorded decision; raw import provenance remains unreviewed.

Rights are separate:

```sh
iryomera-medical rights --workspace .iryomera/workspace.json \
  --batch <batch-id> --decision permitted --reviewer <rights-reviewer> \
  --at 2026-10-16T13:00:00Z --note "Exact terms, intended use, required notices and any extra permissions checked"
iryomera-medical export --workspace .iryomera/workspace.json --output ./new-release
```

Valid rights decisions are `undecided`, `permitted`, `blocked`. No decision is inferred from a string such as MIT or CC-BY. The local CLI trusts the operator and records labels, timestamps and notes; it is not an authentication or legal-compliance service. Attach full terms and required extra notices at import. No original dataset is relicensed.

Export selects approved, nonobsolete terms and approved assertions only. Every selected assertion needs approved, uniquely resolved subject/object/disease endpoints. Missing endpoints block the entire export. Explicit external extension terms are preserved as external context. Unreviewed unresolved assertions can stay in staging without blocking an otherwise valid approved selection. Other import/structural/conflict errors still block release. Existing output directories are never overwritten, and failed/canceled exports leave no partial release.

## Release layout

```text
new-release/
  content.json                    # standard SDK ContentPackage
  source-assertions.json          # full approved source semantics and resolved IDs
  report.json                     # hashes, options, selected audit history, omitted counts
  localization/en.json            # only languages actually supplied by sources
  LICENSES/batch_<sha256>.txt      # attribution, source URL/version, full original terms
```

No rejected/unreviewed source records are copied into the report. The report contains only selected import metadata and review history plus diagnostic counts. Definition `sourceClaims` extensions retain every approved matching claim's source fields and provenance. Identical claims sharing an explicit external ID can coalesce without losing lineage; conflicting labels/types/descriptions remain errors for explicit resolution. Explicit alternate IDs are authoritative aliases; xrefs and text matches never silently merge terms.

Source-language catalogs are content for review. An ICD/OPS-only import may have `de` but no English translation; English HPO may have `en` but no German. The exporter never fabricates translations. `--require-languages en,de` blocks incomplete releases. The [local Studio](content-studio.md) authors/validates genuine translations before publishing a bilingual game-content package. Literal braces/backslashes in original text are escaped for the established SmartFormat dialect; original source fields remain unchanged.

## Normalization and matching

IDs encode the exact external identifier as UTF-8 hex under a configured namespace (`medical` by default). `HP:9999001` always maps to the same internal ID in that namespace, independently of language or import time. Names/descriptions/synonyms receive stable localization keys. No external identifier is replaced by a translated term. Synonym order is normalized for deterministic output while full original source fields retain order/scope/details.

`matches` compares Form-KC/trimmed/case-normalized labels and synonyms within the same language, returns bounded candidate pairs, and changes nothing. Similar names are never sufficient to assert an equivalence, disease association or treatment recommendation. Duplicate external IDs with conflicting claims are diagnostics. Use separate workspaces for comparing changing source releases until an explicit merge/resolution workflow is implemented in Studio.

## Public .NET API

- `ImportEngine.ReadAsync(path, ImportOptions, CancellationToken)` parses/hashes one source file into an immutable-provenance `ImportBatch`. `ImportEngine.Sources` lists registered `IImportAdapter` objects. Implement a new adapter with `ReadAsync(Stream, format, AdapterContext, token)` and register it centrally; test exact source formats.
- `AdapterContext.Term`, `Assertion`, `Diagnostic`, `HeaderComment` attach lineage and preserve raw source fields. `ImportIdentity.ContentId`/`AssertionId` are deterministic identifiers. Do not use label matching to call these methods with invented clinical claims.
- `WorkspaceStore.ReadAsync`, `AddAsync`, `UpdateAsync` implement atomic local persistence. Mutations take an exclusive file lock; concurrent writers fail instead of overwriting one another. Update callbacks must only mutate the supplied workspace, not make external side effects.
- `ImportDocuments.ReadAsync<T>` checks original workspace/report/sidecar JSON against embedded schemas before defaults.
- `RecordRights.Decide`, `AdditionalTerms`, `BlockReason` keep scoped rights separate from clinical review.
- `BulkReview.Preview`, `Apply` evaluate controlled selections and audit every exception atomically.
- `WorkspaceValidation.Validate`, `Status`, `Review`, `ReviewBatch` validate graphs and record explicit review decisions. `EnsureShape` rejects null contract graphs and unsupported versions.
- `TermMatching.Candidates(workspace, limit)` yields proposed duplicates; it never mutates content.
- `ReleaseExporter.ExportAsync(workspace, newDirectory, requiredLanguages, token)` validates the complete approved selection before writing and atomically publishes a complete new directory.
- `Program.CreateCommand` builds the real System.CommandLine tree used by tool execution and offline CLI integration tests.

Async operations accept cancellation; storage/parsing failures propagate typed exceptions through the API. The CLI reports useful errors to stderr. Exit codes: 0 success, 1 invocation/I/O/format/export failure, 2 staged validation errors, 130 handled cancellation. System.CommandLine also reports its own syntax errors.

## Versioned staging formats

`schemas/importer/v1/contracts.schema.json` bundles contracts for workspace, batch, terms, assertions, options, diagnostics, review/rights decisions, resolved assertions, match candidates and release report. Entry schemas resolve locally; their URIs are identifiers, not a hosted service. Generate with `python scripts/generate-importer-schemas.py`. Schema tests verify actual serialized staging/export documents independently of the semantic validator.

The importer v1 format is intentionally strict: unknown properties are rejected rather than silently discarded by a future editor. Preserve future importer versions using their own readers/migrations. The SDK's separate public content format remains additive/extensible. `SourceFields`/`SourceMetadata` are arbitrary raw JSON containers; original XML is stored as text. JSON Schema enforces shape while semantic validation checks identity, provenance, review audit, duplicates, actual reference closure and endpoint types.

Studio loads the atomic workspace snapshot, show original `SourceFields` and diagnostics next to projected definitions, edit reviewed normalized records in its own application layer, and append review decisions while preserving original source claims. The separate [Phase 3 Studio](content-studio.md) implements local UI and migrations; authentication remains outside this local tool.

## Reviewed Phase 2 additions (0.3.0 / adapters 1.1.0)

Batch permission is a necessary baseline, never a blanket override. HPOA IEA/OMIM-related terms/annotations, explicit additional source terms and LOINC `EXTERNAL_COPYRIGHT_NOTICE` require separate record-level review. Record rights default to unresolved for those conditions. Scope a decision to one exact record and retain its permission reference:

```sh
iryomera-medical rights --workspace .iryomera/workspace.json --batch <batch-id> \
  --id <record-id> --decision permitted --reviewer <rights-reviewer> \
  --at 2026-10-16T13:00:00Z --note "Exact intended use and notices checked" \
  --permission-evidence <reference-to-applicable-permission>
```

Do not use this example to claim OMIM permission you do not have. A nonempty reference is audited, not legally verified. A record-level blocked/undecided decision overrides batch permission. A batch decision never clears record restrictions, even in old workspaces. Every contributing duplicate claim retains its own review/rights gate.

Controlled bulk review previews eligibility by default:

```sh
iryomera-medical review-bulk --workspace .iryomera/workspace.json --batch <batch-id> \
  --status approved --reviewer <reviewer> --at 2026-10-16T13:00:00Z \
  --note "Scope and evidence reviewed" --exclusions ./exclusions.json
# Repeat the same command with --apply to commit under the workspace lock.
```

An exclusion file is a JSON object such as `{"medical:id_example":"Outside this release scope"}` using real staged IDs. It is optional. Obsolete/non-ACTIVE terms, unresolved/invalid assertions, missing eligible endpoints and record-specific rights restrictions are automatically excluded. Every exception is persisted in `bulkReviews`; approval runs append rejected decisions for excluded records, including previously approved ones. An invalid exclusion ID/reason fails atomically. Existing strict `review --all` retains its original behavior. Clinical approval does not grant batch redistribution rights.

New LOINC imports are `observation` definitions, not playable examinations. All source columns remain unchanged. Old `examination` imports are not silently rewritten; compare reimports in a separate workspace. Readers still accept adapter 1.0.0 identities; new import normalization uses 1.1.0. JSON schemaVersion remains 1.0.0 with optional additions. Old documents remain readable; old strict readers can reject new optional fields/discriminators.

Identical assertion claims across batches export once, with every approved claim in `sourceClaims` on the sidecar and canonical SDK annotation/relation. The report retains selected provenance and independent permissions. Qualifiers, negation, source frequency and MAxO context also populate SDK MedicalAnnotation. Distinct evidence/context rows remain distinct assertions; no clinical equivalence is inferred. See [ADRs](architecture/0005-record-rights-and-review.md) and [full-dataset tests](full-dataset-tests.md).

## Wikidata (library 0.4.0 / adapter 1.2.0)

The eighth adapter reads saved SPARQL SELECT JSON/CSV with stable Q IDs, actual en/de labels/descriptions/aliases, P780/P923/P924/P828 statement context and explicit external identifiers. Retrieval time and CC0 metadata are mandatory. Medical approval and scoped rights remain separate. `matches --identifiers` offers explicit-identifier candidates only. Deprecated/incomplete/unreferenced statements are excluded from controlled bulk approval. Statements retain ranks, qualifiers and references in the approved source sidecar rather than becoming unconditional clinical relations. See [the complete profile and offline CLI examples](wikidata-import.md) and [ADR 0009](architecture/0009-wikidata.md). Existing adapters retain 1.1.0 normalization and pinned full-dataset identities.
