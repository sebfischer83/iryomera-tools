# Full official medical dataset integration tests

Verified locally on Linux, 2026-10-16, Debian 13, 2 logical processors, .NET SDK 10.0.402 / runtime 10.0.13, Release, adapter 1.1.0. Inputs remain private unchanged official release files. Only aggregate reports, release/checksum descriptors and license notices are committed. Nothing was clinically approved or exported as a medical content release.

## Pinned observed results

Each case imports the **entire** official file twice, computes normalized SHA-256 via streaming serialization, runs workspace semantic validation and checks exact counts/error histograms/output digest. Cases run sequentially in separate processes. Independent raw-JSON inspection confirmed 20,482 labelled native HPO CLASS nodes and 2,003 labelled native MAxO CLASS nodes. This verifies the documented native-term scope, not OWL reasoning or interpretation of every ontology axiom.

| Case | Input bytes | Terms | Source assertions | Import seconds, first / repeat | Validation seconds | Peak process MiB | Error diagnostics |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| hpo-json | 21,915,519 | 20,482 | 25,087 | 1.021 / 0.533 | 1.926 | 372.5 | 651 |
| hpo-obo | 10,863,613 | 20,482 | 25,087 | 2.034 / 0.790 | 1.729 | 309.2 | 651 |
| maxo-json | 9,856,275 | 2,003 | 16,082 | 1.046 / 0.440 | 1.416 | 253.3 | 25,970 |
| maxo-obo | 4,180,499 | 2,003 | 3,507 | 0.418 / 0.297 | 0.541 | 201.9 | 815 |

All four cases matched the pinned counts/errors/digests and produced byte-identical normalized output on repeated imports. [Machine-readable measured reports](verification/phase-2-full-datasets/) include complete input/output SHA-256, release URL, actual runtime/OS, counts by assertion kind, allocation volume, error-record count and diagnostic histogram. Peak working set includes runtime, graph, validation and repeated serialization/import. Allocated bytes are cumulative allocation, not retained/peak memory. Timing is observational, not a portable performance threshold; filesystem caches, JIT and GC affect the repeat. The default input-byte limit is not a process-memory bound.

**Matching the test baseline does not mean the full staged graph passes validation.** HPO has 651 unresolved source cross-reference diagnostics. MAxO JSON has 25,970 unresolved endpoint diagnostics across 13,390 assertion records; MAxO OBO has 815 across 815 records. The published JSON includes edges between foreign ontology nodes; the OBO term adapter emits edges/xrefs from native MAxO terms only. Neither reader invents foreign definitions. Different assertion counts are expected from these documented scopes; cross-format clinical equivalence is not asserted. Unresolved claims remain errors in staging and cannot be approved by controlled bulk review/exported without explicit resolved approved endpoints. Tests pin those errors to detect regressions instead of suppressing them.

HPO contains 588 obsolete labelled terms and 529 aliases also represented as obsolete primary records. MAxO contains 73 obsolete labelled terms, 13 shadowed aliases and 2 unlabelled obsolete nodes. Primary IDs preserve identity; no obsolete term is silently redirected to its alias owner. Unlabelled obsolete nodes retain original fields in source metadata and are not assigned invented labels. Warning counts are retained in reports.

## Reproduce

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
python -m unittest discover -s scripts -p 'test_*.py'
# Explicit opt-in: publicly downloadable ontology files only, no annotation data.
python scripts/run-full-dataset-tests.py --data-dir .iryomera/official --download-open-ontologies
# Offline repeat with the same inputs:
python scripts/run-full-dataset-tests.py --data-dir .iryomera/official
```

HPO v2026-09-01 (`hp.json`, `hp.obo`) and MAxO v2026-01-15 (`maxo.json`, `maxo.obo`) descriptors are in `tests/Iryomera.MedicalImporter.Integration/Datasets`. They record exact immutable URLs, input SHA-256, source metadata/fixed timestamp, original terms and expected output hash/counts/errors. A checksum mismatch fails before import. Missing input does not cause a download without the explicit switch. Custom descriptors never allow downloads, even if they add permission-looking fields. Inputs cannot escape the selected private data directory.

The .NET integration executable can also be used directly with two arguments: a case JSON and a new report path. `DatasetCase` contains `input`, `sha256`, full importer `options`, optional `expectedTerms`, `expectedAssertions`, `expectedErrors` and `expectedNormalizedSha256`. It has no source-network or review/export operation. Test configuration/report types are internal-to-tooling fixtures, not game exchange contracts. The normal CLI remains offline.

CI's `workflow_dispatch` input `full_datasets=true` enables these tests on both Windows and Linux. Standard push/PR CI remains synthetic/offline for medical data and tests the download boundary with mocked network calls. CI artifacts include only aggregate reports and software packages, not source files or normalized medical graphs. See [verification and actual run evidence](phase-2-verification.md).

## ICD-10-GM / OPS / LOINC and restricted annotations

No legitimately acquired full BfArM/LOINC files, exact release metadata and complete terms were supplied. Account-based or restricted originals were not downloaded, and no rights were accepted on the owner's behalf. Full-data results for these sources remain **not run**. HPOA/OMIM annotation production data was not downloaded either. Synthetic tests still cover all seven adapters, including full annotation context and restrictive rights gates.

For a local acquired source, copy a case descriptor **outside the built-in directory**, use a filename relative to the private data directory, exact SHA-256 and `options` (`source`, `sourceVersion`, `sourceUrl`, original `license`/full `licenseText` or relative `licenseFile`, `attribution`, fixed `importedAt`, `namespace`, supported `format`). Use `icd-10-gm`/`ops` with uncompressed ClaML XML or `loinc` with the original `Loinc.csv`, then run:

```sh
python scripts/run-full-dataset-tests.py --case /private/cases/loinc.json --data-dir /private/medical --output /private/reports
```

Omit expectations only during first private characterization; independently check actual counts/errors, then pin all expected fields for repeat tests. Do not commit cases containing confidential permissions or restricted source fields. The aggregate output alone cannot establish clinical adequacy, coding-engine completeness or permission to redistribute an adapted game dataset. Ontology reasoning, BfArM modifier expansion and supplementary LOINC tables remain outside implemented adapter scope.
