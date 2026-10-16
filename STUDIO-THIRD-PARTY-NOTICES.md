# Third-party notices for ContentStudio

Our source/synthetic examples remain MIT. Published Studio output bundles the importer and SDK dependencies covered by `THIRD-PARTY-NOTICES.md`, plus these runtime components, unmodified:

- **Entity Framework Core / Microsoft.Data.Sqlite 10.0.13** and Microsoft.Extensions runtime support, Microsoft / .NET Foundation and contributors, MIT. Original EF Core license from official `dotnet/efcore` tag `v10.0.13` is `third-party/EFCore-LICENSE.txt`.
- **SQLitePCLRaw 2.1.13** core/provider/bundle/native packages, Copyright 2014–2024 SourceGear, LLC (NuGet metadata), Apache-2.0. Full license and upstream notice are `third-party/SQLitePCLRaw-LICENSE.txt` and `third-party/SQLitePCLRaw-NOTICE.txt`, from the available upstream `v2.1.12` source license/notice. The restored 2.1.13 package metadata independently declares Apache-2.0. These wrappers bundle the SQLite native engine; SQLite upstream dedicates its original code to the public domain, described at https://www.sqlite.org/copyright.html. Retain upstream notices when redistributing binaries.
- **SmartFormat 3.6.1**, Copyright 2011–2025 SmartFormat Project / axuno gGmbH, Scott Rippey and contributors, MIT. Original license from the package's source commit `ebf693558e4e511ae8542bdca527d55077296c4d`: `third-party/SmartFormat-LICENSE.txt`.
- **ZString 2.6.0**, Copyright 2020 Cysharp, Inc., MIT. Original license from official `Cysharp/ZString` tag `2.6.0`: `third-party/ZString-LICENSE.txt`.

EF Design/Roslyn, xUnit, Playwright and Chromium are development/test dependencies, not bundled Studio runtime components. Platform .NET/ASP.NET Core runtimes have their own Microsoft/open-source notices; this framework-dependent publish does not bundle a runtime. A self-contained distribution must also retain those runtime notices. Dependency versions and complete graphs are in the lock files. No original medical dataset or restricted source content is bundled.
