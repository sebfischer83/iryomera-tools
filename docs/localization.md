# Localization and Godot integration

`examples/synthetic/en.json` and `de.json` are complete reference catalogs for the synthetic package. English is the technical reference; every catalog namespace needs an English file. Required default languages are `en` and `de` and may be configured in the validator. Stable IDs and keys remain identical across languages.

```json
{
  "schemaVersion": "1.0.0",
  "namespace": "synthetic",
  "language": "de",
  "entries": {
    "synthetic.disease.amber_syndrome.name": "Synthetisches Bernsteinsyndrom",
    "synthetic.ui.patients": "{count:plural:Ein Demo-Patient|{count} Demo-Patienten}",
    "synthetic.ui.cost": "Simulationskosten: {amount:N2}"
  }
}
```

The English disease name is `Synthetic Amber syndrome`; the plural template is `{count:plural:One demo patient|{count} demo patients}`. With `count = 2`, the results are `2 demo patients` and `2 Demo-Patienten`. With `amount = 1234.5m`, en-US formats `1,234.50` and de-DE `1.234,50`.

## Use from .NET or a future Godot adapter

```csharp
await using var stream = File.OpenRead("de.json");
var german = await ContentJson.ReadAsync<LocalizationDefinition>(stream, cancellationToken);
// Also load en.json and any mod catalogs with the same method.
IContentLocalizer text = new ContentLocalizer([english, german]);
var label = text.Format(definition.NameKey, CultureInfo.GetCultureInfo("de"));
var countLabel = text.Format("synthetic.ui.patients", CultureInfo.GetCultureInfo("de-DE"),
    new Dictionary<string, object?> { ["count"] = 2 });
```

The interface has two methods: `TryGetTemplate(key, culture, out template)` performs fallback and indicates whether the key exists; `Format(key, culture, arguments)` renders with the requested culture. No engine types cross this boundary. A Godot 4 adapter can implement its own resource loading, feed UTF-8 streams into the SDK, and set label text from this interface. Do not translate or persist display names in place of IDs. Both libraries ship net8.0/net10.0 assets; the [real Godot integration test](godot-integration.md) verifies packaged net8.0 binaries in Godot 4.7.2 .NET. No engine dependency is introduced into the interface. Other engine/runtime/export combinations still need separate verification.

## Formatting contract

Named variables: `{name}`. Numbers: `{amount:N2}`. Dates: `{date:d}`. Plurals: `{count:plural:one item|{count} items}` for en/de. SmartFormat supplies the language-specific plural rules; consult its documentation for the branch counts of additional languages. The portable supported dialect allows explicit plural formatting, standard .NET formats and nested flat variables. No property access, scripts, reflection or arbitrary formatter extensions are supported. Escaping follows the pinned SmartFormat backslash dialect (`\{`, `\}` and `\\` for literal braces/backslash; double the backslashes in JSON).

English/German placeholder *names* must agree, including inside plural branches. Formatting specifiers and word order can differ. Validation parses templates but does not know runtime argument types; incorrect number/date values still raise runtime formatting errors. Missing variables are errors. Missing translation keys return the key after English fallback. Whitespace-only translations are invalid.

Use `LocalizationValidator.Validate(catalogs, usedKeys)` before release. Missing translations are reported even though fallback works at runtime. Supply all UI and dynamic usage keys to get meaningful unused-key diagnostics. Translation import/export uses `ContentJson` and separate catalogs, never an in-place merge that loses namespace ownership. Overlapping namespace/language catalogs are rejected; explicitly combine files before constructing the localizer.

`ContentStringLocalizer` implements `IStringLocalizer` for later Blazor tools. Its argument overload expects positional templates (`{0}`, `{1:N2}`); named game templates use `IContentLocalizer.Format` with a dictionary. Register services in the host's DI container and configure its culture selection. There is no Blazor application in Phase 1.

Use `LocalizationDocuments.ReadValidatedAsync(input, otherCatalogs, usedKeys, requiredLanguages, token)` at JSON boundaries to combine raw schema checks (including mandatory schemaVersion) with the existing catalog/placeholder diagnostics.
