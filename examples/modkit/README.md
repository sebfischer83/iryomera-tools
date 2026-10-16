# Synthetic ModKit example

Everything under `example-mod` is own MIT software test content, including the generated cube GLB and 1×1 PNG images. All medical-looking records are fictional and unreviewed. There are no real medical claims, third-party datasets or purchased assets.

The mod demonstrates all thirteen definition kinds, shared examination/observation references, room/furniture and personnel/equipment requirements, a 2×1 footprint and service interaction point, original asset licenses, en/de catalogs and separate simulation balancing. Edit text with any JSON editor or use the validated `edit` CLI. See [the full guide](../../docs/modkit.md).

From the repository root after a Release build:

```sh
dotnet run --project src/Iryomera.ModKit -c Release --no-build -- validate --input examples/modkit/example-mod --game-version 0.2.0
dotnet run --project src/Iryomera.ModKit -c Release --no-build -- pack --input examples/modkit/example-mod --output artifacts/example-mod.zip
dotnet run --project src/Iryomera.ModKit -c Release --no-build -- inspect --input artifacts/example-mod.zip
dotnet run --project src/Iryomera.ModKit -c Release --no-build -- unpack --input artifacts/example-mod.zip --output artifacts/example-mod
python scripts/run-modkit-example.py
```

Output targets must not already exist. The Python runner creates its own temporary authoring project and exercises the actual new/edit/validate/pack/inspect/unpack commands, byte preservation and deterministic ZIP output. No network is used. Regenerate the committed fixture with `python scripts/generate-modkit-example.py`.
