# Iryomera Tools

Open-source .NET tools and portable content formats for Iryomera, a planned free hospital management game built with Godot 4/C#. The game remains closed source; these libraries work independently. Own code and synthetic examples are MIT-licensed. Imported data will retain its original licenses.

**Status: Phase 1 foundation with targeted review remediation (0.2.0).** MedicalImporter, ContentStudio, ModKit and ModIo are planned and have no placeholder projects. No medical datasets or clinically approved content are included.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Iryomera.Content.SDK` | Twelve definition types, binary relations plus lossless medical annotations, provenance/review, manifests, balancing and shared schema/semantic validation; net8.0/net10.0 NuGet library |
| `src/Iryomera.Localization` | Separate de/en catalogs, English fallback, variables, plurals, culture formatting, translation diagnostics, engine-independent interface and .NET IStringLocalizer adapter; net8.0/net10.0 NuGet library |
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
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
# Optional locally; required in Windows/Linux CI (Python 3, downloads pinned Godot .NET):
python scripts/run-godot-smoke.py
```

`global.json` selects stable .NET 10 SDK feature bands. The initial restore needs NuGet access; execution is fully local and offline afterward. Ordinary content/library use needs no database, Godot installation, cloud account or credentials. The dedicated integration runner provisions Godot only for its test. CI performs build, both framework test targets, sample, pack and real Godot integration on Windows/Linux. Packages are created locally and as CI artifacts; nothing is automatically published to NuGet.

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

## Formats and architecture

- [JSON format reference and schema catalog](docs/formats.md): draft 2020-12 entry schemas for every public exchange contract, bundled offline in the SDK package; schema version `1.0.0`.
- [Architecture and boundaries](docs/architecture/0001-foundation.md) and [localization decision](docs/architecture/0002-localization.md).
- [Phase 1 implementation plan](docs/plans/phase-1.md), [verification report](docs/phase-1-verification.md) and [concrete Phase 2 plan](docs/plans/phase-2.md).
- [Lossless medical annotation decision](docs/architecture/0003-medical-annotations.md), [Godot runtime decision](docs/architecture/0004-godot-runtime.md) and [executable integration guide](docs/godot-integration.md).
- [Review remediation plan](docs/plans/foundation-review.md).
- [Contributing](CONTRIBUTING.md) and [binding agent rules](AGENTS.md).

Technical validation checks IDs, duplicates, references and endpoint types, namespace ownership, dependency versions/cycles, review audit metadata, balance ranges and translation consistency. Validation does not grant medical approval. Keep original data licenses and evidence with every imported assertion. Do not copy proprietary game databases or purchased assets into this repository.

## Remaining work

Phase 2 adds real, reproducible, licensed import adapters, starting with HPO and MAxO plus separate MAxO annotations, then ICD-10-GM, LOINC and OPS. Source release/license verification is an explicit prerequisite. Phase 3 adds the bilingual Blazor Interactive Server/EF Core/SQLite editor and review workflow. Phase 4 adds secure data-only mod archives, asset references and examples. Phase 5 adds the optional official mod.io REST integration with mock HTTP tests. Their architecture is reserved, but these tools are not implemented or advertised as available.

NuGet publication, medical expert review, the actual game integration/export matrix and production import/export approval policies remain outside this Foundation implementation. Verified Windows/Linux runs and the exact Godot smoke scenario are recorded in the verification report. The [MIT license](LICENSE) covers our work, not future third-party data.
