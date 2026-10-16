# Phase 2 verification and remaining limits

Updated 2026-10-16 after the external review. The original successful 124-test revision and reviewed implementation are recorded separately. No technical test grants clinical approval or third-party rights.

## Original Phase 2: verified Windows and Linux evidence

Commit [`fb08269910f27ce4d03930bae115130b8b126e36`](https://github.com/sebfischer83/iryomera-tools/commit/fb08269910f27ce4d03930bae115130b8b126e36), published on `archive/phase-2-local`.

[GitHub Actions run 38931861423](https://github.com/sebfischer83/iryomera-tools/actions/runs/38931861423) completed successfully. Job statuses and actual logs were checked, including build/test summaries:

| Platform | Build | Tests | Full job |
| --- | --- | --- | --- |
| Windows | Release, 0 warnings/errors | 73 Foundation + 51 Importer = **124 passed**, no failures/skips | [117077570474](https://github.com/sebfischer83/iryomera-tools/actions/runs/38931861423/job/117077570474), 16:25:44–16:27:20 UTC |
| Linux | Release, 0 warnings/errors | 73 Foundation + 51 Importer = **124 passed**, no failures/skips | [117077570795](https://github.com/sebfischer83/iryomera-tools/actions/runs/38931861423/job/117077570795), 16:25:44–16:26:39 UTC |

Both jobs also passed locked restore, schema regeneration, runnable Foundation sample, synthetic CLI pipeline and NuGet tool/library packaging. This supersedes the earlier local-only statement that Windows results had not been observed. That archived revision predates the net8/net10 Foundation review and these Phase 2 corrections; it is not evidence for the new code.

## Reviewed Phase 2 implementation (0.3.0, adapters 1.1.0)

The review branch integrates the previously tested portable Foundation instead of reverting it. All original 51 importer tests and the original Foundation cases remain. The libraries still target net8/net10; the importer and opt-in integration harness target net10. There is no Phase 3 project or placeholder implementation.

Corrections under verification:

- Record-scoped rights decisions with explicit permission evidence; legacy workspaces cannot bypass HPOA IEA/OMIM or explicit per-record additional terms using batch permission. LOINC third-party copyright notices are also gated. Explicit record denial/revocation overrides batch permission.
- `review-bulk` preview/apply with reasoned manual exclusions, automatic obsolete/non-ACTIVE/rights/validation/endpoint exclusions, atomic decisions and complete exception audit. Exclusions revoke any previous approval on approval runs. Existing strict `review --all` remains available.
- Identical cross-batch assertions coalesce while preserving every approved provenance and separate rights decision. Conflicting same-ID claims and within-batch duplicates remain errors. Unapproved duplicate claims cannot leak into releases.
- Neutral SDK ObservationDefinition for new LOINC imports. Existing examination content and adapter 1.0.0 batches remain readable; no automatic migration, medical recommendation or playable examination is generated.
- Contextual HPO/MAxO claims also export as canonical SDK MedicalAnnotation; the compatible sidecar remains. Frequency/negation/qualifiers/multi-way context survive without inferred values.
- Original importer JSON passes offline embedded schemas before CLR defaults; missing schemaVersion fails. Additive schema fields, new schemas and ADRs document reader compatibility.
- Complete official HPO/MAxO JSON and OBO tests pin input/output hashes, counts and errors. Actual full files exposed shadowed obsolete primary IDs and unlabelled obsolete nodes, now handled explicitly without invented replacements/labels.

Local verification uses Debian 13, SDK **10.0.402**, runtimes **8.0.32 / 10.0.13**, Release. Local locked restore and Release build succeeded with **0 warnings/errors**. **106 Foundation tests per framework + 74 Importer tests = 286 xUnit executions**, all passed with no failures/skips, plus **4 Python boundary tests**. The original 124 cases remain in this expanded suite. The synthetic CLI pipeline and bilingual Foundation sample passed. All three 0.3.0 NuGet packages were created and inspected: SDK has both runtime assets and 28 schema files; the tool bundles the schema evaluator and complete runtime license notices. Completed review CI evidence is recorded below once observed. Historical runs above are not described as testing later changes.

The [full-dataset report](full-dataset-tests.md) records all four complete-file runs, measured time/memory and remaining unresolved references. Each full file imported twice with byte-identical normalized output and matching pinned errors. Expected source-reference errors remain staging errors; this is a reproducibility/scope test, not a valid clinical release. ICD-10-GM/OPS/LOINC full-file tests were not run without legitimately acquired local data and terms; their synthetic adapters remain tested. No restricted datasets were downloaded or published.

## Reproduce

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build --logger trx
python -m unittest discover -s scripts -p 'test_*.py'
python scripts/run-importer-example.py
dotnet run --project examples/Iryomera.Foundation.Sample -c Release --no-build
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
python scripts/run-godot-smoke.py
# Explicit additional public ontology-file tests:
python scripts/run-full-dataset-tests.py --data-dir .iryomera/official --download-open-ontologies
```

The managed environment uses writable `/tmp` SDK/CLI/NuGet locations and single-node builds with shared compilation disabled. Those are local accommodations. Ordinary importer execution stays offline; Godot testing and explicitly enabled full-file acquisition need initial downloads. CI uses current Foundation-reviewed Actions versions and Windows/Linux jobs, with optional complete-file cases through workflow_dispatch.

## Remaining risks and open work

- Exact-input licensing, third-party permissions and clinical suitability still require competent human decisions. Nonempty permission evidence is recorded, not legally authenticated. Conservative OMIM gates can over-exclude references; no policy claims every identifier requires a license. Unknown additional terms cannot be discovered perfectly by software.
- Full BfArM and LOINC production integration is unverified without local legitimate inputs. HPOA/OMIM production-file testing is also open. No complete real medical release has been clinically reviewed, translated or published.
- Foreign ontology/xref definitions remain unresolved unless explicitly supplied. Full files contain expected errors; do not approve the entire raw import blindly. No ontology reasoning, BfArM modifier/coding engine, clinical recommendations or invented equivalences are implemented.
- JSON/XML staging remains in memory. Reports measure actual process peak and repeated imports, not a bound for all source releases or machines. Very large workspaces, rights histories and approval/export of production-scale selections still need further end-to-end capacity testing.
- Claims with differing evidence/context keep distinct IDs; semantic equivalence across nonidentical source rows is not inferred. Conflicting releases/types require explicit resolution or separate workspaces.
- Schema v1 additions preserve old-to-new reading. Old strict importer readers or SDK binaries with closed definition discriminators may reject new fields/types. Adapter 1.1.0 identifies changed normalization; old adapter IDs remain unchanged.
- Local reviewer identities and permission references are trusted audit labels, not authentication or signatures. Authenticated review, edited translations and SQLite/Blazor workflows belong to later Studio work. Missing requested release languages still block export; no translations are fabricated.
- NuGet publication, Phase 3, ModKit and mod.io are outside this review.

See [ADRs](architecture/0005-record-rights-and-review.md), [source/license scope](data-sources/README.md), [LOINC decision](architecture/0006-observation-definitions.md), [CLI/API guide](medical-importer.md) and [implementation plan](plans/phase-2-review.md).
