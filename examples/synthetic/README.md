# Synthetic foundation content

All definitions, terms, relations and simulation values here were authored for software tests under MIT. They are fictional, **not medical knowledge** and not medical recommendations. All provenance remains unreviewed. No HPO, MAxO, ICD, LOINC, OPS or other third-party dataset is embedded.

`content.json` has 22 definitions covering all twelve kinds, 22 relations covering all eight relation kinds, and one separate gameplay record. Names are in `en.json` and `de.json`, each containing 27 keys. `mod-manifest.json` demonstrates only the Phase 1 metadata contract; it is not a ready-to-install ModKit archive. Its name key is included in both language catalogs.

| Kind | Example ID | English | German |
| --- | --- | --- | --- |
| Disease | `synthetic:amber_syndrome` | Synthetic Amber syndrome | Synthetisches Bernsteinsyndrom |
| Symptom | `synthetic:amber_signal` | Synthetic Amber signal | Synthetisches Bernsteinsignal |
| Finding | `synthetic:amber_reading` | Synthetic Amber reading | Synthetischer Bernsteinmesswert |
| Examination | `synthetic:amber_scan` | Synthetic Amber scan | Synthetische Bernsteinuntersuchung |
| Treatment | `synthetic:amber_session` | Synthetic Amber session | Synthetische Bernsteinbehandlung |

Azure and Copper variants provide two further fictional disease/symptom/finding/examination/treatment groups. Supporting records represent a demo procedure, department, equipment, qualification, room, furniture and staff role. Relations are authored purely to exercise the formats. Gameplay duration (120 seconds), costs and success probability (0.8) are arbitrary simulation test values, separate from medical assertions.

Run the actual validation/localization example from the repository root:

```sh
dotnet run --project examples/Iryomera.Foundation.Sample
```
