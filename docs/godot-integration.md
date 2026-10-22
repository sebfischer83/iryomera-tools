# Godot 4 C# integration and binary compatibility

The repository builds with a .NET 10 SDK. **Content.SDK and Localization ship separate net8.0 and net10.0 NuGet assets.** A net8.0 Godot game selects the net8.0 assets automatically; the tools select net10.0. A net10.0-only DLL was not directly referenceable from the planned game's default net8.0 project, so the original Phase 1 claim of engine independence did not establish binary compatibility.

The pinned integration target is Godot **4.7.2 .NET**, desktop Windows/Linux x64. Its official project generator uses net8.0 for desktop. The integration test uses a real Godot scene and actual packed dependencies, not a reference to Godot in either library. See [ADR 0004](architecture/0004-godot-runtime.md) for the multi-targeting evaluation and official sources.

## Run the integration test

Install a .NET 10 SDK for the build and a .NET 8 runtime/SDK for the minimum-runtime test, plus Python 3:

```sh
dotnet restore Iryomera.Tools.sln --locked-mode
dotnet build Iryomera.Tools.sln -c Release --no-restore
dotnet test Iryomera.Tools.sln -c Release --no-build
dotnet pack Iryomera.Tools.sln -c Release --no-build -o artifacts
python scripts/run-godot-smoke.py
```

The runner downloads the official pinned Godot .NET desktop archive into a temporary cache, verifies the published SHA-512 checksum, restores an isolated copy of `tests/Iryomera.Godot.Integration` using the locally packed libraries of the current `<Version>`, builds it with Godot.NET.Sdk/4.7.2 and runs the editor import/scene headlessly. Use `--godot <executable>` to supply an existing compatible .NET-enabled engine, or `--dotnet <executable>` for a custom SDK installation. Setup needs network access for packages/engine; loaded content, schema validation and localization run offline.

The test requires a .NET 8 runtime and sets `DOTNET_ROLL_FORWARD=Minor` in the game process environment. Godot's normal LatestMajor roll-forward could otherwise run a net8 game on .NET 10 and hide a minimum-runtime issue. The editor/MSBuild step retains its normal SDK environment and is checked for engine error output. The game scene asserts the actual CLR major and both assemblies' TargetFrameworkAttribute values.

Success requires `IRYOMERA_GODOT_SMOKE_OK` and exit 0. The scene proves:

- NuGet selected net8.0 assets for **both** libraries and their dependency graph loads in the actual Godot host.
- Bundled JSON Schema and semantic validation load a content package without external schema requests.
- HPO-/MAxO-shaped synthetic annotations, negation, source frequency and context survive JSON round trips.
- The diagnostic prototype loads five shared examinations, all four observation result kinds, separate simulation configuration and complete DE/EN category/rationale/text keys.
- Mod furniture asset references, footprints and interaction-point contracts load from the synthetic mod package; no model is executed or rendered by the smoke test.
- English/German labels and plural formatting render correctly.

The two libraries contain no Godot, Blazor, database or distribution dependency. A game should reference both NuGet packages normally so the matching System.Text.Json/schema/formatting dependencies are included; copying a single DLL manually does not reproduce the tested setup.

## Consumer usage

In a Godot net8.0 C# project, reference `Iryomera.Content.SDK` and `Iryomera.Localization` 0.20.3 from the GitHub Packages feed or a local pack folder; see [releases and package feed](releases.md). Nothing is published to nuget.org.

```csharp
await using var input = File.OpenRead(ProjectSettings.GlobalizePath("res://content.json"));
var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(input);
if (!result.IsValid)
    throw new InvalidDataException(string.Join("; ", result.Issues.Select(x => x.Message)));

IContentLocalizer localizer = new ContentLocalizer([english, german]);
label.Text = localizer.Format(result.Value!.Definitions[0].NameKey,
    CultureInfo.GetCultureInfo("de"));
```

The source catalogs come from your own resource loader and retain stable keys/IDs. No Godot adapter is required inside the libraries; the small integration project is a tested host example.

## Limits

This proves the pinned Windows/Linux desktop scenario. It does not establish support for every Godot 4 version, older .NET 6 game targets, Android/iOS, browser export, trimming or NativeAOT. Changing the game's exact engine/target/export settings should rerun the integration test and may require a separate decision. .NET 10 remains the toolchain's primary target; the net8.0 asset is an explicit game compatibility target, not a downgrade of the tooling.
