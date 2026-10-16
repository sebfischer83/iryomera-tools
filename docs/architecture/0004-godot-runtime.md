# ADR 0004: .NET 10 tooling and net8.0/net10.0 consumer binaries

Status: accepted during the Phase 1 review remediation.

## Verified compatibility issue

The original libraries targeted only net10.0. A game targeting net8.0 cannot directly reference those binaries; installing a .NET 10 SDK does not change a game's target framework or runtime contract. The official [Godot 4.7.2 project generator](https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/editor/GodotTools/GodotTools.ProjectEditor/ProjectGenerator.cs) still selects net8.0 for desktop and net9.0 for Android. The [4.5 generator](https://github.com/godotengine/godot/blob/4.5-stable/modules/mono/editor/GodotTools/GodotTools.ProjectEditor/ProjectGenerator.cs) has the same minimum desktop TFM. [Godot's official C# setup documentation](https://github.com/godotengine/godot-docs/blob/4.7/tutorials/scripting/c_sharp/c_sharp_basics.rst) distinguishes SDK/build tools from runtime requirements.

## Decision

Build Content.SDK and Localization for **net8.0;net10.0**, while using the .NET 10 SDK/C# toolchain to build the repository. Build/run the foundation tests on both frameworks. NuGet selects the compatible net8.0 asset for a net8.0 game, and the net10.0 asset for the tools. Keep Godot types and packages out of both libraries.

Use the supported **System.Text.Json 10.0.13** package on the net8.0 SDK asset. Its documented net8.0 target provides the JSON APIs used by the existing format (out-of-order discriminator support, duplicate-property rejection). This avoids a different hand-written serializer or weaker validation on the game target. net10.0 uses the platform assembly. JsonSchema.Net supplies schema evaluation on both targets; no UI/database/engine dependency is introduced. The actual package dependencies travel through NuGet, not a manually copied SDK DLL alone.

Do not choose netstandard2.0 merely to claim all Godot 4 versions are supported. It would broaden the runtime/API contract and dependency matrix without proving older engine support. Godot versions with older .NET runtimes, mobile export, browser export, trimming and NativeAOT remain separate compatibility decisions. The minimal integration test verifies a specific current desktop engine build, not every Godot 4 release/platform.

## Executable proof

`tests/Iryomera.Godot.Integration` is a standalone net8.0 Godot.NET.Sdk/4.7.2 project. It is deliberately outside the ordinary solution: the libraries and their unit tests do not require Godot. `scripts/run-godot-smoke.py` restores this project in an isolated temporary directory using the **actual locally packed NuGet artifacts**, not project references. It downloads the pinned official Godot 4.7.2 .NET Windows/Linux x64 archive and checks the official SHA-512 sums (or uses an explicitly supplied compatible executable).

The editor import uses the normal .NET 10 SDK environment (MSBuild 10 requires it); only the game runtime is constrained. The Godot scene asserts that both loaded library assets target net8.0 and that the actual runtime is .NET 8. Godot normally allows LatestMajor roll-forward; the test sets DOTNET_ROLL_FORWARD=Minor for the actual game process so installing .NET 10 alongside .NET 8 cannot accidentally hide a minimum-runtime incompatibility. It then performs bundled-schema/semantic content loading, a lossless medical annotation JSON round trip, and English/German/plural rendering. A nonzero exit, load failure, missing success marker or timeout fails CI. A console test alone would not prove the custom Godot host resolves the package dependency graph, so the real engine test is required.

The test downloads SDK/engine prerequisites during setup. The loaded libraries and content operations themselves are offline. The engine/version and package targets are pinned so a future Godot/framework migration requires a conscious update to the test and this decision.
