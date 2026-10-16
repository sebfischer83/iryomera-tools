# Contributing

Read [AGENTS.md](AGENTS.md), the [architecture](docs/architecture/0001-foundation.md), and the current [phase plans](docs/plans/phase-3.md) and [diagnostic plan](docs/plans/diagnostic-content.md). Contributions are MIT-licensed; preserve upstream notices and data licenses. Do not include medical datasets, credentials or purchased assets in pull requests. Use synthetic or explicitly licensed fixtures with per-record lineage.

Write authoritative repository documentation, public API documentation, ADRs and format specifications in English. Label supplementary translations and link their English reference; update the English reference first and keep translations aligned. See the [documentation index](docs/README.md). This documentation policy does not change the required English/German UI and content localization support.

Install the .NET 10 SDK and .NET 8 runtime/SDK, then run from the repository root:

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build
dotnet run --project examples/Iryomera.Foundation.Sample -c Release --no-build
python scripts/run-importer-example.py
python -m pip install -r scripts/requirements-browser.txt
python -m playwright install --with-deps chromium
python scripts/run-studio-smoke.py
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
python scripts/run-godot-smoke.py
```

The first restore needs NuGet access; the applications and subsequent build/test/run with restored dependencies operate offline. The dedicated Godot integration runner additionally provisions a pinned official .NET engine build and isolated package consumer. Python 3 is required for schema/example generators and integration runners, not for normal Studio/library use. The browser runner uses the pinned test-only `scripts/requirements-browser.txt`; install Chromium with `python -m playwright install --with-deps chromium`.

`Directory.Packages.props` controls dependency versions. Lock files are committed. For intentional updates, use `dotnet restore --force-evaluate`, review lock-file changes and dependency licenses, then rerun the checks. Do not add UI/database/distribution packages to the SDK.

For a public contract change, update the C# model, `scripts/generate-schemas.py`, generated `schemas/v1` files, format documentation, examples and semantic/schema tests together. Run `python3 scripts/generate-schemas.py` and `python3 scripts/generate-importer-schemas.py`; also regenerate Studio schemas with `python scripts/generate-studio-schemas.py` and diagnostics with `python scripts/generate-diagnostic-example.py`. CI checks generated schemas/examples. Breaking changes need a new schema version and migration ADR. Keep unknown additive fields round-trippable. Tests should cover both a valid use and relevant error cases.

PR descriptions should state the problem, resulting behavior, compatibility implications and actual validation results. If a check cannot run, report the blocker. Medical import/approval work must state precisely what the upstream source asserts; never infer clinical links or turn an import into a medical release.
