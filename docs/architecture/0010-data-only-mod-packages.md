# ADR 0010 — explicit data-only mod inventories and atomic ZIP processing

Status: accepted for Phase 4 implementation.

## Contracts and boundaries

Reuse SDK ModManifest identity, namespace, name key, author, SemVer/game range, dependencies, content files, languages and license. The new ModKit v1 `mod.json` envelope adds an explicit file inventory with kind, path, license and optional attribution. Existing bare SDK manifests remain valid metadata, but are not advertised as complete installable packages. Each payload ContentPackage uses the same identity/namespace/version/dependencies as the mod; multiple files form one virtual validation graph while original bytes and unknown metadata remain unchanged in the archive.

SDK definitions gain optional asset references (GLB model, texture, icon) and furniture gains optional grid placement. These are portable data contracts, not Godot resources. Footprints describe integer X/Z cells with positive cell size in meters; omitted occupied cells mean the whole rectangle. Interaction points use local X/Y/Z meters (Y up) and optional heading/qualification. No scripts or custom runtime callbacks are part of these formats. Existing roomFurniture and examination/resource relations remain authoritative links rather than redundant embedded requirement lists.

The CLI and reusable ModKit application APIs target .NET 10 and depend on SDK/Localization, not Studio, EF, Godot, MedicalImporter or mod.io. The SDK retains net8/net10 assets for the game. Mod.io can later distribute the same archives; local create/validate/unpack works independently. Patient simulation remains private.

## File and archive policy

V1 supports SDK content JSON, localization JSON, binary GLB, PNG/JPEG/WebP textures/icons and text/Markdown notices. Use portable ASCII relative paths with forward slashes; reject device names, traversal, trailing dots/spaces, links/reparse points and case-insensitive collisions. Paths are identifiers within a package, not arbitrary filesystem paths. Asset references require matching declared kind/path/license. No external asset URIs are fetched. GLB framing and embedded-resource checks are boundary validation, not a full glTF conformance or rendering certification. Image signatures are checked, not decoded by the tool.

ZIP contains `mod.json`, only declared payload files, and `checksums.json` with schemaVersion, exact SHA-256 and sizes for every other entry. Unknown/duplicate entries and directory/link/special-file entries are rejected. Hashes detect corruption and changes, not authenticity: an attacker can rewrite an unsigned index. Signature/trusted-publisher verification is outside Phase 4.

Apply configured compressed archive, expanded total, per-entry, entry-count and compression-ratio ceilings before extraction and while streaming actual bytes. Validate paths and parent/file conflicts before writing. Inspect/unpack use fresh private staging, fully verify hashes and content/asset/dependency rules, then publish a new directory atomically; existing destinations are never overwritten. Failures/cancellation leave no partially installed target. Source directories and ancestors must not be links, and paths are checked on access. This is a local operator tool; a hostile concurrent writer with equivalent filesystem privileges is outside its isolation guarantee.

Pack captures bounded files into staging, compares bytes to validated hashes, writes sorted entries with fixed timestamps/attributes and atomically creates a new archive. Repeating identical inputs on the same compressor/runtime produces identical ZIP bytes. Cross-runtime compressor output equality is not promised; content hashes and semantic checks remain portable. Edit operations validate a candidate snapshot before atomically replacing one file and use a cooperative project lock.

## Review, licensing and compatibility

Per-file declarations and per-information provenance retain original licenses; neither MIT tool code nor technical validation relicenses imported medical content or purchased assets. Packaging is not clinical review or legal permission. Existing importer/Studio reviewed release gates remain unchanged, and unreviewed provenance is not automatically promoted. The bundled example is entirely own synthetic MIT data and assets.

SDK changes are optional additions within schemaVersion 1.0.0; library capability becomes 0.5.0. ModKit's public authoring/integrity formats have their own `schemas/modkit/v1` bundle. English documentation is authoritative; CLI descriptions are English, source catalogs and example content support en/de. A future GUI consumes the same APIs rather than embedding packaging in UI code.

Official implementation references: [.NET ZipArchive](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchive?view=net-10.0), [ZipArchiveEntry.ExternalAttributes](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchiveentry.externalattributes?view=net-10.0), [Khronos glTF 2.0 specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html). No external medical dataset or account service is needed.
