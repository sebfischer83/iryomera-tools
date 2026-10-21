# Documentation

English is the authoritative language for repository documentation, public API documentation, architecture decisions and format specifications. Additional translations are welcome; identify their English reference and update it first when behavior or contracts change. German documentation is supplementary. The tools' user interface and content localization continue to support both English and German.

## Examination and diagnostic model

- [Diagnostic model and format reference](diagnostic-content.md): definitions, relationships, result types, JSON examples, simulation boundaries and additive v1 extensions.
- [ADR 0008](architecture/0008-diagnostic-content.md): architecture decision and private-game runtime plan.
- [JSON exchange formats](formats.md) and [schema files](../schemas/v1/).
- [Demo clinic](../examples/demo-clinic/README.md) generated from game fixtures.
- [Synthetic examples](../examples/synthetic/README.md) and [verification report](phase-3-verification.md).
- [Supplementary German model overview](diagnosemodell.md).

## Tools and integration

- [Content Studio](content-studio.md): local authoring, review and export.
- [ModKit](modkit.md), [data-only package architecture](architecture/0010-data-only-mod-packages.md) and [Phase 4 verification](phase-4-verification.md).
- [Medical Importer](medical-importer.md), [Wikidata](wikidata-import.md) and [source formats/licenses](data-sources/README.md).
- [Localization](localization.md) and [Godot integration](godot-integration.md).
- [Name importer](name-importer.md) (Phase 5, first adapters): [plan](plans/names.md), [ADR 0011](architecture/0011-name-pools.md) and [name source candidates](data-sources/names.md).
- [Furniture asset catalog references and mounting](plans/furniture-assets.md), [ADR 0013](architecture/0013-catalog-assets-and-mounting.md) and [equipment relations, ADR 0014](architecture/0014-equipment-relations.md).
- [ADR 0015, mods overriding gameplay balancing](architecture/0015-gameplay-overrides.md) [ADR 0016, movable furniture](architecture/0016-movable-furniture.md) [ADR 0017, room rules, staff roles and routing](architecture/0017-m7-content-gaps.md) [ADR 0018, demo clinic and furniture equipment](architecture/0018-demo-clinic.md) [ADR 0019, purchase prices and wages](architecture/0019-price-balancing.md) [ADR 0020, departments](architecture/0020-departments.md) [ADR 0021, treatments and complications](architecture/0021-treatments-and-complications.md) and [ADR 0022, furniture comfort and insurers](architecture/0022-comfort-and-insurers.md).
- [Releases and package feed](releases.md) and [ADR 0012](architecture/0012-github-packages-feed.md).
- [Architecture foundations](architecture/0001-foundation.md) and [binding development rules](../AGENTS.md).
