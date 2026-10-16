# ADR 0002: Separate catalogs and a constrained SmartFormat dialect

Status: accepted.

Use one JSON file per namespace/culture. English (`en`) is the reference catalog; German (`de`) is required. Additional cultures require no SDK model change. Stable keys are lowercase dotted identifiers owned by the catalog namespace, e.g. `base.disease.appendicitis.name`. Mod namespaces isolate keys and prevent silent overrides.

`IContentLocalizer` accepts an explicit `CultureInfo`, key and optional dictionary of values. It has no engine dependency and is the integration seam for Godot 4. Catalogs become private snapshots when loaded. Per-call formatter construction avoids mutable shared formatting state in future Interactive Server sessions.

Use SmartFormat's parser, plural rules and .NET culture formatting instead of implementing plural rules. The portable supported subset consists of flat named or positional placeholders, standard number/date formats, nested placeholders and the explicit `plural` formatter. Property traversal, reflection-based value lookup and arbitrary formatting extensions are excluded. This keeps the dialect small and reproducible across tools. Syntax is SmartFormat, not ICU MessageFormat. Missing variables or invalid runtime value formats raise formatting exceptions; bad content is not silently replaced with a success-looking string.

Fallback order is requested culture → parent cultures → English → key. Formatting uses the requested culture even when the text came from English. Technical validation still reports incomplete German catalogs. A localizer can serve incomplete non-English catalogs for fallback, but refuses malformed templates, differing placeholders, duplicate catalogs and missing English reference keys. Unused-key analysis requires an explicit complete usage set, including UI/dynamic keys; it cannot infer runtime use from code.

The .NET `ContentStringLocalizer` adapter follows `CurrentUICulture` and supports positional arguments (`{0}`); no-argument lookup returns the raw template as expected for IStringLocalizer. Future Blazor hosts should register `ContentLocalizer`/`IContentLocalizer` and `ContentStringLocalizer`/`IStringLocalizer` in their composition root, supply request culture handling, and keep their own UI catalogs. No web UI is shipped in Phase 1.
