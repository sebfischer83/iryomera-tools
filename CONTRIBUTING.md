# Contributing

Read [AGENTS.md](AGENTS.md), the [architecture](docs/architecture/0001-foundation.md), and the current [phase plan](docs/plans/phase-1.md). Contributions are MIT-licensed; preserve upstream notices and data licenses. Do not include medical datasets, credentials or purchased assets in pull requests. Use synthetic or explicitly licensed fixtures with per-record lineage.

Install the .NET 10 SDK, then run from the repository root:

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build
dotnet run --project examples/Iryomera.Foundation.Sample -c Release --no-build
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
```

The first restore needs NuGet access; the applications and subsequent build/test/run with restored dependencies operate offline. Python 3 is only required to regenerate schema files, not to consume the libraries.

`Directory.Packages.props` controls dependency versions. Lock files are committed. For intentional updates, use `dotnet restore --force-evaluate`, review lock-file changes and dependency licenses, then rerun the checks. Do not add UI/database/distribution packages to the SDK.

For a public contract change, update the C# model, `scripts/generate-schemas.py`, generated `schemas/v1` files, format documentation, examples and semantic/schema tests together. Run `python3 scripts/generate-schemas.py`; CI checks for uncommitted schema changes. Breaking changes need a new schema version and migration ADR. Keep unknown additive fields round-trippable. Tests should cover both a valid use and relevant error cases.

PR descriptions should state the problem, resulting behavior, compatibility implications and actual validation results. If a check cannot run, report the blocker. Medical import/approval work must state precisely what the upstream source asserts; never infer clinical links or turn an import into a medical release.
