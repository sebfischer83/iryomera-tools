# ContentStudio local guide

ContentStudio is an offline ASP.NET Core/Blazor Interactive Server editor using SQLite/EF Core 10. It consumes the SDK and Phase 2 importer APIs; portable libraries remain independent of the UI/database. See [ADR 0007](architecture/0007-content-studio.md).

## Start and data storage

From the repository root, with .NET 10 installed:

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet run --project src/Iryomera.ContentStudio -c Release --no-build
```

Open `http://127.0.0.1:5180`. English/Deutsch links change the UI cookie and content display language. All styles/scripts are local. Restore initially needs NuGet; authoring needs no cloud, account, internet connection or real dataset. Press **Load synthetic examples / Synthetische Beispiele laden** after entering an operator label to create an editable own MIT fixture project. Nothing loads or receives approval automatically.

The default database is `.iryomera/studio/studio.db` relative to the process working directory (normally the Studio project directory with `dotnet run`). To choose a predictable private location:

```sh
dotnet run --project src/Iryomera.ContentStudio -c Release --no-build -- --Studio:DataDirectory /absolute/private/studio-data
```

PowerShell/Windows paths work with the same configuration argument. The directory is created and actual EF migrations run at startup. SQLite uses WAL and short-lived contexts. Stop the app before copying the **entire data directory** for backup, including any database sidecars. A content release ZIP contains the approved selection, not a complete authoring-database backup. Keep source workspaces and original licensed files separately.

## Author and review

1. Open a project. Search by stable ID or DE/EN label; filter type/status. Search results page in groups of 30.
2. Create/edit diseases, symptoms, findings, examinations, observations, treatments, departments, equipment, qualifications, rooms, furniture, roles and procedures. IDs/types stay immutable. Names, descriptions and synonyms have separate DE/EN fields. Original source identity/license is preserved. Classifications use schema-validated structured JSON.
3. Relationships shows an SVG overview of the first 12 relationships and a complete editable list. Select/create a typed relation; the server checks IDs and endpoint types. Contextual annotations retain source fields and use validated JSON editing.
4. Gameplay has separate simulation costs, duration, probabilities, difficulty and resource fields. They do not rewrite medical source evidence or approve clinical facts.
5. Validation lists graph/translation errors and potential duplicate labels. Candidates never merge automatically. Import genuine DE/EN catalog JSON here. Advanced panels edit observation results, sourced intervals, recommendations and explicitly simulation-only diagnostic settings; the corresponding package graph is revalidated.
6. Record reviewer/reason and choose review status individually or select an explicit list in **Review & export**. Medical/translation edits reset affected approval and project-rights confirmation; gameplay changes remain separate. New content starts unreviewed. Source review and legal rights are independent decisions.
7. Confirm project rights with evidence. Additional record terms (including OMIM/IEA indicators) require a separate record permission tied to the content digest. Blocking/revoking permission blocks export. The audit page retains revisions and edit/review reasons.
8. Download a release. It contains only approved, reference-closed content, actual DE/EN catalogs, required source notices and review/permission records. Missing translations, references, approval or permissions produce an error. These ZIPs are fixed-path downloads, not Phase 4 mod packages; the app never extracts an uploaded archive.

For the definition-only diagnostic prototype, upload `examples/synthetic/diagnostics.json`, then import `diagnostics.en.json` and `diagnostics.de.json` on the validation page. The package is entirely fictional. See [diagnostic contracts](diagnostic-content.md).

## Phase 2 imports

Upload a saved `ImportWorkspace` JSON on **Imports**, up to 32 MiB. Raw files first go through the importer schema boundary. The original snapshot is immutable; current reviews/rights are separate audited revisions. Browse/filter/paginate terms/assertions, original claims, diagnostics, matches and controlled bulk-review previews. Existing HPOA/OMIM/obsolete-term/deprecated-Wikidata gates apply.

Record clinical reviews and rights using the same importer APIs as the CLI. **Create editable project** first requires a valid, approved, right-cleared importer release. The resulting authored project starts unreviewed and preserves a source binding. Compare it with imports by exact ID, label and type. Importer changes never silently overwrite project edits or combine names. Before each authored release, the exporter rechecks current source selection/rights, including revocations, and scopes the source proof to selected records. A project-level permission cannot bypass upstream record terms.

Raw source-file parsing remains in the importer CLI. Studio does not download medical datasets. Keep large full-ontology workspaces in the CLI/integration harness until a measured Studio capacity/import strategy is implemented.

## Concurrency, trust and limits

An aggregate revision protects concurrent browser tabs; a stale edit fails with a reload instruction instead of overwriting newer content. Package/index/audit writes share a transaction. An invalid edit rolls back. Application identities are trusted local operator labels, not authenticated medical credentials. Bind loopback by default; authenticated multi-user/network hosting is not implemented.

This first editor uses forms for normal definitions/relationships/balancing and validated JSON for advanced contracts. Rich graphical diagnostic workflow planning, bulk translation authoring, automatic conflict reconciliation, deletion/refactoring, installed dependency-package management and large-dataset performance tuning remain open. It does not perform patient simulation, clinical diagnosis, medical approval, license verification or ModKit installation.

## Publish and verify

```sh
dotnet publish src/Iryomera.ContentStudio -c Release --no-restore -o artifacts/studio
# Start from artifacts/studio when distributing the published folder:
# dotnet Iryomera.ContentStudio.dll --Studio:DataDirectory /absolute/private/studio-data
dotnet test tests/Iryomera.ContentStudio.Tests -c Release
```

Published output includes local assets, SDK/importer dependencies, schemas and synthetic samples. Run from its publish directory when moving that output to another machine; a matching .NET 10 ASP.NET Core runtime is required. For a self-contained distribution choose a specific Windows/Linux runtime at publish time and preserve dependency notices.

Optional browser checks (also run in Windows/Linux CI):

```sh
python -m pip install -r scripts/requirements-browser.txt
python -m playwright install --with-deps chromium
python scripts/run-studio-smoke.py
# Or test the standalone publish output:
python scripts/run-studio-smoke.py --published-directory artifacts/studio
```

Only test setup downloads browser binaries. The smoke session blocks external app requests and exercises real interactive DE/EN editing, SQLite persistence, SVG selection, explicit approval/rights and release download with own synthetic data.
