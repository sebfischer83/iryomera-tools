# Phase 2 verification and remaining limits

Verified locally on Linux, 2026-10-16, .NET SDK **10.0.402**, target net10.0, Release.

| Check | Observed result |
| --- | --- |
| Locked restore | Successful; committed per-project lock files include the new importer/test dependencies |
| Solution build | Successful, **0 warnings / 0 errors** |
| Foundation tests | **73 passed**, unchanged behavior |
| Importer tests | **51 passed**, including real CLI invocation and independent staging/release/SDK/catalog schema evaluation |
| Total tests | **124 passed, 0 failed, 0 skipped** |
| Synthetic CLI pipeline | Source discovery, four imports, validation, candidate matching, explicit review, explicit rights decision and JSON export succeeded |
| Tool packaging | `Iryomera.MedicalImporter.0.1.0.nupkg` created alongside the two foundation library packages |
| Tool installation | Local `dotnet tool install` succeeded; installed `iryomera-medical sources list` listed all seven adapters, and the full synthetic pipeline also succeeded through the installed executable |
| Package inspection | Contains CLI/runtime dependencies, versioned importer schemas, README and software dependency license notices |
| Schema generation | Both generators reproduce checked-in candidates byte-for-byte |

## Implemented source scope

- HPO and MAxO: OBO Graph JSON / OBO term/edge subset, original metadata, definitions, synonyms, explicit alternate IDs, deprecated state and source assertions.
- HPOA: documented twelve-column phenotype.hpoa TSV, minimal disease claims, full annotation context, evidence, unresolved target diagnostics and conservative positive-relation projection.
- MAxO annotations: independently licensed current fifteen-column TSV; multi-way action/phenotype/disease/extension context stays intact in a versioned sidecar.
- ICD-10-GM and OPS: ClaML category/preferred-label imports in the original language with full original class XML; no inferred clinical criteria or modifier expansion.
- LOINC: named-column CSV with quoted/multiline fields, exact original columns and values, original display label and status; no thresholds or disease recommendations.

Tests cover reproducible import/output bytes; idempotence; source hashes/version mismatch; changed/malformed/unsupported source shapes; null/unknown staging fields; original provenance/license retention; duplicate/conflicting claims; explicit alternate IDs; synonym candidates without automatic links; reference/type checks; obsolete terms; negations and frequency contexts; review/rights gates; missing translations; CLI failures; canceled/failed atomic writes and exports; competing writers; no unapproved/rejected record leakage in reports; source-string escaping; and XML external-entity rejection.

The synthetic release contains five definitions, one unqualified DiseaseFinding relation and three source assertions. It is fictional software test content, not a medical release. No real medical dataset was committed, redistributed or approved. Source/license research used official metadata/documentation, not a licensed production dataset integration test.

## Reproduce

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build --logger trx
python scripts/run-importer-example.py
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
```

The managed environment used writable `/tmp` SDK/CLI/NuGet paths and single-node builds with shared compilation disabled. These are local environment accommodations, not tool dependencies. The workflow now exercises both foundation and importer on Windows/Linux, including the executable synthetic pipeline. Windows and GitHub Actions results have **not** been observed locally. No NuGet publication or repository push is claimed.

## Explicit remaining limitations

- Exact-release HPO/annotation/BfArM/LOINC redistribution rights and clinical appropriateness require human review for each real input and intended use. HPO's linked license page failed direct access; authoritative repository/web-page sources and the uncertainty are documented. Some guessed BfArM release-detail links returned soft error pages. Do not treat these as verified download URLs or a blanket license grant.
- No production-scale full dataset import/performance benchmark was run. Tests use our own synthetic source structures. Local source files are capped by the configured byte limit; ontology JSON/XML are parsed in memory. Ontology reasoning and modifier/coding engines are not implemented.
- OWL/RDF, legacy MAxO annotation TSV, BfArM positional metadata CSV/FHIR, LOINC supplementary tables and automatic disease-equivalence inference are unsupported and not advertised as implemented.
- Source-language catalogs are exported honestly. Missing en/de translations block the optional bilingual release gate; translation authoring/import workflow belongs to Studio. No UI exists in this phase.
- Review labels are local operator audit metadata, not authenticated clinical identity or automated legal verification. No automatic medical approval occurs.
- Changing source releases with conflicting term semantics remain explicit conflicts; there is no automatic source-version merge policy. Identical approved claims sharing the same explicit external ID retain all approved lineage.
- Medical action source assertions are exported separately rather than losing their qualifiers in a diseaseTreatment relation. A game consumer must explicitly implement the source predicate/context semantics it chooses to use.
- Staging JSON is strict/versioned; a future Studio must preserve the source claims and append review decisions, and provide explicit migrations for incompatible staging changes.
- ContentStudio, SQLite/EF migrations, secure ModKit packages and mod.io remain later phases.

See [source scope and license review](data-sources/README.md), [architecture](architecture/0003-importer.md) and [CLI/API guide](medical-importer.md).
