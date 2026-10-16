# ADR 0006 — LOINC observation codes are separate from examinations

Status: accepted, additive SDK extension in package 0.3.0, 2026-10-16.

LOINC identifies observations, measurements, panels and documents. A code alone does not describe a hospital workflow, equipment, costs or a playable diagnostic examination. Automatically treating every row as ExaminationDefinition is misleading.

Add `ObservationDefinition : ContentDefinition`, wire discriminator `observation`, using the existing identity, localization, classification and provenance fields. LOINC adapter 1.1.0 maps every row to this neutral type and retains **all** original columns in SourceFields/sourceRecord/sourceClaims. Fields such as COMPONENT, PROPERTY, SYSTEM, SCALE_TYP, METHOD_TYP, CLASS and PanelType are original source metadata when present; do not infer panels or clinical functionality from missing fields. No diseaseExamination relation, balance, diagnostic threshold, clinical cutoff or success rate is synthesized.

The existing ExaminationDefinition continues to represent an explicitly authored examination. No new relation kind or automatic observation-to-examination mapping is introduced in Phase 2. A future authoring requirement may justify an independent sourced mapping object; it needs a separate decision and explicit human curation. Non-ACTIVE LOINC codes stay available in staging and are excluded from controlled approval/release.

Existing schema 1.0.0 contents, IDs and saved examinations remain readable unchanged. The v1 content union is additively expanded and a standalone observationDefinition schema is supplied. New observation content requires the 0.3.0 reader; old readers with a closed polymorphic discriminator reject an unknown type. This is forward-reader compatibility for old contents, not a claim that new types work in an old binary. No automatic rewrite of previously imported examinations occurs; reimport with adapter 1.1.0 into a separate workspace to compare changes. Never combine differing kinds under one content ID without resolving the explicit conflict.

Both SDK target frameworks (net8.0/net10.0) test serialization, schema loading and relation endpoint rejection. Godot still consumes the actual net8.0 package; no engine dependency is added.
