# Third-party notices for the MedicalImporter binary tool

The .NET tool package bundles unmodified dependencies. Our own source remains MIT; the following components retain their own licenses and copyright notices:

- **CsvHelper 33.1.0**, Copyright © 2009-2024 Josh Close and contributors; upstream package declares `MS-PL OR Apache-2.0`. This distribution uses the **Apache-2.0** option. Original dual-license text is included in `third-party/CsvHelper-LICENSE.txt`.
- **System.CommandLine 2.0.0**, Microsoft / .NET Foundation and contributors, **MIT**. Upstream license in `third-party/System.CommandLine-LICENSE.txt`; the restored package additionally states © Microsoft Corporation. All rights reserved.
- **JsonSchema.Net 7.3.4**, **JsonPointer.Net 5.3.1**, **Json.More.Net 2.1.1**, .NET Foundation and contributors, **MIT**; bundled offline schema validation. Original license in `third-party/JsonEverything-LICENSE.txt`.
- **Humanizer.Core 2.14.1**, .NET Foundation and contributors (Mehdi Khalili, Claire Novotny), **MIT**; transitive JsonPointer dependency. Original license in `third-party/Humanizer-LICENSE.txt`.
- **NuGet.Versioning 7.0.0**, .NET Foundation and contributors, **Apache-2.0**. The restored package additionally states © Microsoft Corporation. All rights reserved. Upstream notice in `third-party/NuGet.Versioning-LICENSE.txt` and the full Apache license in `third-party/Apache-2.0.txt`.

Source locations: https://github.com/JoshClose/CsvHelper (tag 33.1.0), https://github.com/dotnet/command-line-api, https://github.com/NuGet/NuGet.Client. Pinned binaries and package metadata are recorded in `packages.lock.json` and `Directory.Packages.props`. These notices cover bundled runtime dependencies; test-only dependencies remain normal development restores and are listed in `docs/dependencies.md`.

These are software dependency notices, not medical data licenses. Every permitted medical-data export must separately retain the exact source licenses, attribution and any additional third-party terms. No real medical dataset is bundled with this tool.
