# Wikidata offline import

Source code/synthetic fixtures are our own work. Wikidata structured data is CC0-1.0; require the actual CC0 notice in the source license file. Source provenance records snapshot version/URL, actual retrieval timestamp and import timestamp separately. No live medical-source query is required or performed.

The adapter supports W3C SPARQL SELECT JSON (`head.vars`, `results.bindings`) and SPARQL CSV with a named header, using [the query profile](../examples/wikidata/diseases.rq). Arbitrary Wikidata JSON/entity dumps and truthy-only wide results without this profile are not advertised as equivalent formats. Save the exact original result and query privately. Record a stable snapshot/query version in --source-version; repeat the same bytes/options/timestamps to reproduce normalized staging.

```sh
iryomera-medical import wikidata --input ./saved-results.json \
  --source-version saved-query-2026-10-16 --source-url https://query.wikidata.org/ \
  --license CC0-1.0 --license-file ./wikidata-cc0-notice.txt \
  --attribution "Wikidata contributors; saved query and revision details" \
  --retrieved-at 2026-10-16T10:00:00Z --imported-at 2026-10-16T11:00:00Z \
  --workspace .iryomera/wikidata.json
iryomera-medical matches --identifiers --workspace .iryomera/wikidata.json
iryomera-medical validate --workspace .iryomera/wikidata.json
```

Required column: `item` as Q ID or canonical Wikidata entity URI. Optional bilingual columns: itemLabelEn/De, itemDescriptionEn/De, itemAliasEn/De; target equivalents use valueLabelEn/De, valueDescriptionEn/De, valueAliasEn/De. One alias per result row is losslessly grouped. A disease without actual en/de label produces a diagnostic instead of an invented label. Missing translations remain missing.

Statement columns: property (P780/P923/P924/P828 or property entity URI), statement URI, value (Q ID/URI or original binding), rank (Wikibase rank URI or normal/preferred/deprecated), reference/referenceProperty/referenceValue, qualifierProperty/qualifierValue. All extra columns and JSON binding metadata survive in SourceFields.rows. Optional valueKind explicitly classifies a target as symptom/finding/examination/treatment/medicalProcedure; P780/causes are not guessed from names. An unsupported/unlabelled/ambiguous target stays unresolved. JSON with unbound values remains a source claim requiring review.

Optional explicit identifier columns: hpoId, icd10, icd9, icd10cm, icd11Foundation, externalSystem/externalId. For targets, prefix and capitalize: valueHpoId, valueExternalSystem, valueExternalId etc. MAxO is accepted only as an explicit external-system binding, not an invented Wikidata property. matches --identifiers offers potential exact-identifier or ICD classification-family matches without merging IDs. Name candidates remain a separate explicitly selected matching mode and never create medical facts.

Sources list shows Wikidata alongside existing adapters. Existing review, controlled bulk review, rights and export commands apply. CC0 is not automatic medical approval and does not skip explicit redistribution audit. ReviewConcern diagnostics/exclusions identify deprecated, incomplete, unreferenced and unresolved statements. Qualifiers/rank/evidence stay attached; no unconditional diseaseExamination/diseaseTreatment relation or gameplay probabilities are inferred from Wikidata.

Approved definitions export actual bilingual names/descriptions. Independent aliases use language-qualified catalog keys in the definition's localizedAliasKeys extension (map language → key list), with original aliases retained in sourceClaims. No translation pairing is invented. A strict bilingual release still requires actual translations for shared name/description keys. The SDK's generic GetContentKeys does not interpret importer alias extensions; consumers add the chosen language's keys when checking alias usage.

Wikidata statements export in source-assertions.json with resolved approved endpoints and exact original fields. P828 references without supported target definitions remain in staging and block approval/export of that statement. No live download command is included; the bounded query can be run manually if desired. Full Wikidata coverage, Wikidata revision-history reconciliation, exhaustive RDF semantics, reference truth assessment and authenticated clinical review are not implemented.
