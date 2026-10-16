# Diagnostic content contract plan

Analyze and retain the existing reusable ExaminationDefinition, neutral ObservationDefinition, independent relations and separate GameplayBalance. No private Godot/game dependency or patient simulator will be added.

- Extend observations with optional typed result descriptors: numeric/unit, boolean, categorical result options (optional sourced finding links), or text. Add separately sourced reference ranges with explicit patient-context conditions.
- Add typed independent examination-to-observation, room, qualification and prerequisite relations; retain the existing examinationEquipment relation. Disease links remain reusable references, never embedded duplicate examination lists.
- Add DiagnosticRecommendation as an optional independently sourced/reviewed disease/examination-context assertion. It does not enforce an AI sequence or constitute an automatic clinical recommendation.
- Keep fabricated observation distributions and visibility/repeat policy in explicit simulation-only configuration. Reuse existing balance costs/difficulty and add examination duration; do not attach gameplay probabilities to medical facts or imported HPO/MAxO/Wikidata/LOINC evidence.
- Validate value/type/unit/range/category consistency, IDs, duplicate IDs, endpoint kinds/dependencies, patient-context bounds, source/review metadata and prerequisite cycles. Existing v1 content must remain readable without new fields.
- Supply a separate bilingual fictional package with at least three diseases, five shared examinations and numeric/categorical/boolean/text outcomes. No real reference ranges or clinical associations are claimed.
- ADR explains handoff to Studio and Godot, private seeded state generation, stable cached patient observations, visibility, repeat reasons, diagnostic decisions and an AI planning approach using simulated information gain/cost/resource/urgency. None of that runtime is implemented here.
- Extend generated schemas and both net8/net10 tests, Godot package checks and mod namespace/dependency examples; rerun all existing tests.
