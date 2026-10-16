# Runnable synthetic importer pipeline

The inputs in `tests/Iryomera.MedicalImporter.Tests/Fixtures` are authored synthetic MIT fixtures, not excerpts of real ontologies or classifications. The adapters intentionally use genuine source *structures* with fictional identifiers/labels. LOINC fixture codes start with X to avoid confusion with assigned codes. Fixture terms make no clinical claims.

From the repository root, after the Release build:

```sh
python scripts/run-importer-example.py
```

This drives the actual CLI: source discovery → HPO JSON / HPOA TSV / MAxO OBO / MAxO annotations TSV → staging → reference validation → explicit synthetic-only review → explicit MIT-fixture redistribution decision → JSON release. It writes to a temporary directory and prints its location. `--output <new-directory>` chooses a local destination. `--tool <path-to-iryomera-medical>` runs the same pipeline through an installed tool package instead of `dotnet run`. The automatic approvals in this script apply **only to these hardcoded fictional fixtures**, not to user-supplied medical data.

The release contains five definitions (two findings, two fictional diseases, one procedure), one positive DiseaseFinding relation and three source assertions (one hierarchy, one HPO association, one full-context MAxO annotation). No DiseaseTreatment relation is invented. See the separate source-assertions sidecar for the action/phenotype/disease/extension context. Original licenses/notices and the selected audit history accompany the release. All source labels in this example are English; no German translations are fabricated.

The same test directory also contains ClaML ICD-10-GM/OPS, LOINC CSV, OBO aliases/obsolete terms, negative and unresolved HPOA fixtures. Unit/integration tests exercise those separately. See [the CLI guide](../../docs/medical-importer.md) and [source field/license review](../../docs/data-sources/README.md).
