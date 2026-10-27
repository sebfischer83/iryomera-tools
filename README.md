# Iryomera Tools

Open-source .NET tools and portable content formats for Iryomera, a planned free hospital management game built with Godot 4/C#. The game remains closed source; these libraries work independently. Own code and synthetic examples are MIT-licensed. Imported data will retain its original licenses.

**Status: Foundation, MedicalImporter, local bilingual ContentStudio and data-only ModKit; name pool contracts with the NameImporter CLI, catalog asset references, furniture mounting and surfaces, treatment/room equipment relations, room contents and surface categories, balancing overrides for mods, movable furniture, room rules, staff roles and department routing, furniture equipment capabilities a generated synthetic demo clinic, purchase prices and wages as balancing, and departments with rooms, opening order, unlock values, opening hours, arrival routes and inpatient stays, treatments with complications, furniture comfort and fictional insurers (0.20.0), overlapping synthetic demo readings (0.20.1) shared demo symptoms and findings (0.20.2) and rebalanced demo diagnostics (0.20.3) discomfort and care levels as balancing (0.21.0) staff and service room roles (0.22.0) stations of examinations and treatments (0.23.0) and recurring diseases (0.24.0).** Released packages are published to [GitHub Packages](docs/releases.md). Reusable diagnostic content contracts are implemented; patient simulation belongs to the private game. ModIo remains planned with no placeholder project. No real medical datasets or clinically approved content are included. Exact source licenses and unsupported formats are documented explicitly.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Iryomera.Content.SDK` | Thirteen definition types, reusable typed examination outputs, sourced diagnostic contracts, independent relations/annotations, provenance/review, manifests, separate simulation configuration and schema/semantic validation; net8.0/net10.0 NuGet library |
| `src/Iryomera.Localization` | Separate de/en catalogs, English fallback, variables, plurals, culture formatting, translation diagnostics, engine-independent interface and .NET IStringLocalizer adapter; net8.0/net10.0 NuGet library |
| `src/Iryomera.MedicalImporter` | Local .NET CLI/tool: eight adapters including Wikidata SPARQL JSON/CSV, reproducible staging, explicit review/rights history, reference validation and approved JSON release export |
| `src/Iryomera.ContentStudio` | Local Blazor Interactive Server/SQLite editor, DE/EN UI, EF migrations, search/authoring, import review, separate balancing, audited rights and approved JSON export |
| `src/Iryomera.ModKit` | Local .NET CLI/tool and async APIs: create/edit/validate data mods, asset/layout references, explicit dependencies, bounded ZIP pack/inspect/unpack and integrity checks |
| `tests/Iryomera.ModKit.Tests` | Synthetic CLI/project/archive tests including traversal, links, corruption, limits, dependencies and cancellation |
| `tests/Iryomera.ContentStudio.Tests` | Real SQLite/service/export/HTTP integration tests; a real browser smoke script also runs in CI |
| `tests/Iryomera.MedicalImporter.Integration` | Opt-in pinned full official ontology import/reproducibility/performance runner; no clinical approval or source publication |
| `tests/Iryomera.MedicalImporter.Tests` | Synthetic adapter, pipeline, CLI, reproducibility, failure and schema integration tests |
| `tests/Iryomera.Foundation.Tests` | xUnit semantic/error-case tests, async round trips and independent JSON Schema verification |
| `tests/Iryomera.Godot.Integration` | Standalone real-engine net8.0 smoke test against packed libraries, Godot 4.7.2 .NET, Windows/Linux |
| `examples/Iryomera.Foundation.Sample` | Runnable offline validation and German/English localization demonstration |

## Start

Install a .NET 8 runtime/SDK for compatibility tests and a [.NET 10 SDK](https://learn.microsoft.com/en-us/dotnet/core/install/) on Windows or Linux, then run from the repository root:

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build
dotnet run --project examples/Iryomera.Foundation.Sample -c Release --no-build
dotnet run --project src/Iryomera.MedicalImporter -c Release --no-build -- sources list
python scripts/run-importer-example.py
python scripts/run-modkit-example.py
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
# Optional locally; required in Windows/Linux CI (Python 3, downloads pinned Godot .NET):
python scripts/run-godot-smoke.py
```

`global.json` pins the exact .NET SDK 10.0.402 (`rollForward: disable`) because the SDK contributes implicit ASP.NET package versions to the committed lock files; install that SDK, and update `global.json`, CI and lock files together when moving to a newer one. The initial restore needs NuGet access; execution is fully local and offline afterward. Ordinary content/library use needs no database, Godot installation, cloud account or credentials. The dedicated integration runner provisions Godot only for its test. CI performs build, both framework test targets, sample, pack and real Godot integration on Windows/Linux. Packages are created locally and as CI artifacts; nothing is automatically published to NuGet.

Start the [local editor](docs/content-studio.md) with `dotnet run --project src/Iryomera.ContentStudio -c Release --no-build` and open `http://127.0.0.1:5180`. Enter an operator label and explicitly load synthetic examples. All authoring runs locally after restore.

## Use the SDK

```csharp
using Iryomera.Content.SDK;

await using var input = File.OpenRead("examples/synthetic/content.json");
var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(input);
// Supply installedPackages for a package with dependencies.
foreach (var issue in result.Issues)
    Console.WriteLine($"{issue.Code} {issue.Path}: {issue.Message}");
```

IDs such as `synthetic:amber_syndrome` stay stable across languages. Disease, examination and treatment examples are in [synthetic content](examples/synthetic/README.md): `synthetic:amber_syndrome`, `synthetic:amber_scan`, `synthetic:amber_session`. The package includes 22 definitions and 22 consistent relations. These are fictional fixtures, not clinical facts. Simulation balancing is stored separately in `gameplay`.

The key `synthetic.disease.amber_syndrome.name` renders as **Synthetic Amber syndrome** in English and **Synthetisches Bernsteinsyndrom** in German. Catalogs also demonstrate named variables, numeric formatting and plural forms. See [localization and the Godot integration interface](docs/localization.md) for working calls.

The additional [diagnostic prototype](docs/diagnostic-content.md) defines three fictional diseases, five shared examinations, numeric/categorical/boolean/text results and separate game parameters. [ADR 0008](docs/architecture/0008-diagnostic-content.md) plans stable seeded patient state and diagnostic AI exclusively for the private game. No patient runtime is implemented here.

## Formats and architecture

- [Documentation index](docs/README.md) and [German diagnostic model overview](docs/diagnosemodell.md).
- [JSON format reference and schema catalog](docs/formats.md): draft 2020-12 entry schemas for every public exchange contract, bundled offline in the SDK package; schema version `1.0.0`.
- [Architecture and boundaries](docs/architecture/0001-foundation.md) and [localization decision](docs/architecture/0002-localization.md).
- [Phase 1 implementation plan](docs/plans/phase-1.md), [verification report](docs/phase-1-verification.md) and [concrete Phase 2 plan](docs/plans/phase-2.md).
- [Lossless medical annotation decision](docs/architecture/0003-medical-annotations.md), [Godot runtime decision](docs/architecture/0004-godot-runtime.md) and [executable integration guide](docs/godot-integration.md).
- [Review remediation plan](docs/plans/foundation-review.md).
- [MedicalImporter guide](docs/medical-importer.md), [source field/license review](docs/data-sources/README.md), [Phase 2 architecture](docs/architecture/0003-importer.md) and [verification report](docs/phase-2-verification.md).
- [Phase 2 review plan](docs/plans/phase-2-review.md), [record-rights decision](docs/architecture/0005-record-rights-and-review.md), [LOINC observation decision](docs/architecture/0006-observation-definitions.md) and [full-dataset tests](docs/full-dataset-tests.md).
- [Studio startup, workflows and limits](docs/content-studio.md), [Studio architecture](docs/architecture/0007-content-studio.md), [diagnostic formats](docs/diagnostic-content.md) and [Wikidata import](docs/wikidata-import.md).
- [ModKit guide](docs/modkit.md), [package decision](docs/architecture/0010-data-only-mod-packages.md), [synthetic mod](examples/modkit/README.md) and [Phase 4 verification](docs/phase-4-verification.md).
- [0.4.0 verification report and current limits](docs/phase-3-verification.md).
- [Contributing](CONTRIBUTING.md) and [binding agent rules](AGENTS.md).

Technical validation checks IDs, duplicates, references and endpoint types, namespace ownership, dependency versions/cycles, review audit metadata, balance ranges and translation consistency. Validation does not grant medical approval. Keep original data licenses and evidence with every imported assertion. Do not copy proprietary game databases or purchased assets into this repository.

## Remaining work

The importer supports HPO/MAxO OBO Graph JSON and OBO, HPOA/current MAxO TSV, ICD-10-GM/OPS ClaML categories, LOINC CSV and the documented saved Wikidata SPARQL profile. OWL, legacy MAxO layouts, modifier expansion, clinical reasoning and automatic translation are not implemented. Real-data redistribution still requires exact-release/license/clinical review; default CI tests use synthetic MIT fixtures; opt-in full HPO/MAxO ontology tests are documented with measured errors and resource use. Full ICD-10-GM/OPS/LOINC tests still require legitimately supplied inputs. Studio’s advanced diagnostic editing uses validated JSON; richer workflow widgets and large-data capacity tuning remain open. Phase 4 provides data-only archives, asset references and a synthetic mod; its graphical editor, publisher signatures and game runtime are future work. Phase 5 adds the optional official mod.io REST integration with mock HTTP tests and remains unimplemented.

NuGet publication, medical expert review and authenticated approval workflows remain outside this implementation. The CLI records trusted local operator decisions; it does not grant legal permission or medical authority. Verified CI and Godot runs are recorded in the verification reports. The [MIT license](LICENSE) covers our work, not third-party data.
