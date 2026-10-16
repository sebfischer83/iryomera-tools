# Iryomera Tools

Open-source .NET tools and portable content formats for Iryomera, a planned free hospital management game built with Godot 4/C#. The game remains closed source; these libraries work independently. Own code and synthetic examples are MIT-licensed. Imported data will retain its original licenses.

**Status: Foundation and Phase 2 MedicalImporter implemented for the documented source formats.** ContentStudio, ModKit and ModIo remain planned with no placeholder projects. No real medical datasets or clinically approved content are included. Exact source licenses and unsupported formats are documented explicitly.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Iryomera.Content.SDK` | All twelve definition types, independent relations, provenance/review, manifests, simulation balancing, JSON I/O and content/dependency validation; packable NuGet library |
| `src/Iryomera.Localization` | Separate de/en catalogs, English fallback, variables, plurals, culture formatting, translation diagnostics, engine-independent interface and .NET IStringLocalizer adapter; packable NuGet library |
| `src/Iryomera.MedicalImporter` | Local .NET CLI/tool: seven adapters, reproducible staging, explicit review/rights history, reference validation and approved JSON release export |
| `tests/Iryomera.MedicalImporter.Tests` | Synthetic adapter, pipeline, CLI, reproducibility, failure and schema integration tests |
| `tests/Iryomera.Foundation.Tests` | xUnit semantic/error-case tests, async round trips and independent JSON Schema verification |
| `examples/Iryomera.Foundation.Sample` | Runnable offline validation and German/English localization demonstration |

## Start

Install a [.NET 10 SDK](https://learn.microsoft.com/en-us/dotnet/core/install/) on Windows or Linux, then run from the repository root:

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build
dotnet run --project examples/Iryomera.Foundation.Sample -c Release --no-build
dotnet run --project src/Iryomera.MedicalImporter -c Release --no-build -- sources list
python scripts/run-importer-example.py
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
```

`global.json` selects stable .NET 10 SDK feature bands. The initial restore needs NuGet access; execution is fully local and offline afterward. No database, Godot installation, cloud account or credentials are required. CI performs build/test/sample/pack on Windows and Linux. Packages are created locally and as CI artifacts; nothing is automatically published to NuGet.

## Use the SDK

```csharp
using Iryomera.Content.SDK;

await using var input = File.OpenRead("examples/synthetic/content.json");
var package = await ContentJson.ReadAsync<ContentPackage>(input);
var issues = ContentValidator.Validate(package); // supply installed dependencies as second argument
foreach (var issue in issues)
    Console.WriteLine($"{issue.Code} {issue.Path}: {issue.Message}");
```

IDs such as `synthetic:amber_syndrome` stay stable across languages. Disease, examination and treatment examples are in [synthetic content](examples/synthetic/README.md): `synthetic:amber_syndrome`, `synthetic:amber_scan`, `synthetic:amber_session`. The package includes 22 definitions and 22 consistent relations. These are fictional fixtures, not clinical facts. Simulation balancing is stored separately in `gameplay`.

The key `synthetic.disease.amber_syndrome.name` renders as **Synthetic Amber syndrome** in English and **Synthetisches Bernsteinsyndrom** in German. Catalogs also demonstrate named variables, numeric formatting and plural forms. See [localization and the Godot integration interface](docs/localization.md) for working calls.

## Formats and architecture

- [JSON format reference and schema catalog](docs/formats.md): draft 2020-12 entry schemas for every public exchange contract, bundled offline in the SDK package; schema version `1.0.0`.
- [Architecture and boundaries](docs/architecture/0001-foundation.md) and [localization decision](docs/architecture/0002-localization.md).
- [Phase 1 implementation plan](docs/plans/phase-1.md), [verification report](docs/phase-1-verification.md) and [concrete Phase 2 plan](docs/plans/phase-2.md).
- [MedicalImporter guide](docs/medical-importer.md), [source field/license review](docs/data-sources/README.md), [Phase 2 architecture](docs/architecture/0003-importer.md) and [verification report](docs/phase-2-verification.md).
- [Contributing](CONTRIBUTING.md) and [binding agent rules](AGENTS.md).

Technical validation checks IDs, duplicates, references and endpoint types, namespace ownership, dependency versions/cycles, review audit metadata, balance ranges and translation consistency. Validation does not grant medical approval. Keep original data licenses and evidence with every imported assertion. Do not copy proprietary game databases or purchased assets into this repository.

## Remaining work

The importer supports HPO/MAxO OBO Graph JSON and OBO, HPOA/current MAxO TSV, ICD-10-GM/OPS ClaML categories and LOINC CSV. OWL, legacy MAxO layouts, modifier expansion, clinical reasoning and automatic translation are not implemented. Real-data redistribution still requires exact-release/license/clinical review; technical tests use only synthetic MIT fixtures. Phase 3 adds the bilingual Blazor Interactive Server/EF Core/SQLite editor and review workflow. Phase 4 adds secure data-only mod archives, asset references and examples. Phase 5 adds the optional official mod.io REST integration with mock HTTP tests. These later tools remain unimplemented.

NuGet publication, a real Windows CI result, medical expert review, Godot runtime adapters and authenticated approval workflows remain outside this local implementation. The CLI records trusted local operator decisions; it does not grant legal permission or medical authority. The [MIT license](LICENSE) covers our work, not future third-party data.
