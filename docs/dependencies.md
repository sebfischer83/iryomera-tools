# Dependency licensing

Our source and synthetic fixtures use the repository's MIT license. Third-party components keep their own licenses; MIT does not relicense dependencies or future medical datasets. NuGet packages are restored, not vendored. Versions and complete transitive graphs are pinned in `Directory.Packages.props` and per-project `packages.lock.json` files.

Direct package license expressions verified from the restored package metadata during Phases 1–3:

| Package | Version | License | Usage |
| --- | --- | --- | --- |
| Microsoft.EntityFrameworkCore.Sqlite / Design | 10.0.13 | MIT | Studio runtime / migration tooling |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.13 | MIT | Studio tests only |
| System.CommandLine | 2.0.0 | MIT | importer CLI |
| CsvHelper | 33.1.0 | MS-PL OR Apache-2.0 (Apache option used) | importer CSV/TSV |
| NuGet.Versioning | 7.0.0 | Apache-2.0 | semantic versions/ranges in SDK |
| SmartFormat | 3.6.1 | MIT | formatting/plurals |
| Microsoft.Extensions.Localization.Abstractions | 10.0.0 | MIT | .NET localization adapter |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | tests only |
| xunit | 2.9.3 | Apache-2.0 | tests only |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | tests only |
| JsonSchema.Net | 7.3.4 | MIT | offline SDK schema validation and tests |
| System.Text.Json | 10.0.13 | MIT | net8.0 SDK wire/API compatibility (net10.0 uses platform assembly) |

Refer to each NuGet package's license and notices for redistribution obligations. Library packages declare dependencies normally rather than embedding their binaries. The MedicalImporter tool bundles runtime dependencies and includes [third-party notices](../THIRD-PARTY-NOTICES.md) and original license texts. Dependency updates require review of license and security metadata as well as tests. Medical source license decisions are a separate gate in the Phase 2 plan; no third-party medical dataset is distributed here.

Studio's SQLite provider restores SQLitePCLRaw 2.1.13 (Apache-2.0) and the SQLite native engine with its own upstream terms. The published app bundles its runtime dependencies; retain their upstream license/NOTICE files when distributing binaries; [Studio notices](../STUDIO-THIRD-PARTY-NOTICES.md) and the original texts are copied with publish output. EF Design and its Roslyn dependencies are development tools, not dependencies of either portable SDK package. Python Playwright 1.62.0 (Apache-2.0) and downloaded Chromium are test-only setup dependencies, absent from Studio runtime. Inspect their upstream notices if redistributing test binaries. No proprietary library is vendored.
