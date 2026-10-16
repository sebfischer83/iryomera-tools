# Source formats, licensing and imported scope

Review date: **2026-10-16**. This is an engineering record of the official materials consulted, not a blanket permission to redistribute medical datasets. Source code and synthetic fixtures are MIT. Third-party data is **not** relicensed by the importer. No complete third-party dataset or purchased/proprietary content is committed.

The importer reads local, user-supplied files and makes no download request or acceptance of upstream terms. Every batch requires source/release URL, version, original license identifier, full terms/notices and attribution. Redistribution defaults to undecided; release export requires an explicit, auditable decision by the local operator for every contributing batch. This records the user's review rather than granting rights. Preserve additional permissions/notices in the supplied license file. Do not mark a batch permitted until its exact release, upstream components and intended use are covered.

## HPO ontology (`hpo`)

Official sources checked:

- [Repository and download guidance](https://github.com/obophenotype/human-phenotype-ontology), [release v2026-09-01](https://github.com/obophenotype/human-phenotype-ontology/releases/tag/v2026-09-01).
- Release assets include `hp.json`, `hp.obo`, `hp.owl`, and separate `phenotype.hpoa`. Use the exact release asset URL as source metadata, not an unversioned PURL alone.
- [Ontology repository license pointer](https://github.com/obophenotype/human-phenotype-ontology/blob/master/LICENSE.md) directs to the HPO website. The linked `hpo.jax.org/app/license` returned 404 during direct verification. The [official HPO web project's license-page source](https://github.com/TheJacksonLaboratory/hpo-web/blob/main/src/app/static/resources/license/license.component.html) and [historical official license page](https://human-phenotype-ontology.github.io/license.html) describe attribution/version disclosure and preservation of content/logical relationships. The web application's own software license is a different license and must not be applied to the ontology.

**Decision:** do not assert MIT, CC-BY or unrestricted modification for HPO. Local parsing is supported; verify exact-release data terms and their application to a translated/normalized game dataset before redistribution. Commercial use is not independently confirmed by a blanket license grant in the consulted ontology pointer. The tool preserves original records/relationships and records user-supplied terms; it does not authorize editing the original ontology or removing attribution.

Implemented: OBO Graph JSON (`graphs[].nodes/edges`) and OBO 1.x term stanzas. `HP:` classes become generic `FindingDefinition`, not guessed symptoms/diagnoses. Preserve labels, definitions, synonyms (including their original source scopes), explicit alternate IDs, obsolete status, xrefs, hierarchy and other term attributes. OBO Graph metadata and OBO headers are retained. No hierarchy becomes a disease relationship. Foreign ontology nodes and non-class entities are not reclassified.

Not implemented: OWL reasoning/RDF import, inference, multilingual `hp-international` interpretation, gene associations and semantic disease matching. The OBO reader is a bounded term/tag reader, not a full ontology reasoner or continuation/macro processor. Full raw JSON nodes and OBO tag values remain available in staging; unsupported source syntax must not be represented as a successful clinical interpretation.

## HPO disease annotations (`hpo-annotations`)

[Official phenotype.hpoa format](https://github.com/obophenotype/human-phenotype-ontology/blob/master/docs/annotations/phenotype_hpoa.md) documents twelve TSV columns: `database_id`, `disease_name`, `qualifier`, `hpo_id`, `reference`, `evidence`, `onset`, `frequency`, `sex`, `modifier`, `aspect`, `biocuration`. The ontology release also lists this as a separate asset.

**License warning:** annotation rights must be checked separately; the official format documentation explicitly flags additional OMIM permission for IEA annotations derived from OMIM. Do not treat every annotation as freely redistributable because ontology files are downloadable. The adapter flags IEA rows, and the export gate additionally recomputes record-specific rights requirements for IEA/OMIM-related assertions and their sourced disease claims, even in old workspaces. Batch permission cannot release them; an explicit record decision with permission evidence is required. Non-IEA OMIM references are conservatively gated too, without claiming that every OMIM identifier necessarily requires a separate license.

Imported: disease identifier/name as a minimal sourced disease definition; all twelve columns remain verbatim in each source assertion. References/evidence also populate provenance. Explicit HPO external IDs and alternate IDs resolve against loaded terms; unresolved targets remain diagnostics. Only a reviewed positive `P`-aspect assertion with empty qualifier/frequency/onset/sex/modifier fields can additionally project to a context-free DiseaseFinding relation. Any frequency or other context remains in the source assertion and canonical SDK MedicalAnnotation rather than interpreting ontology frequency terms or dropping qualifiers. All context/frequency strings remain source data, never simulation probabilities. Negative/non-P assertions remain exact source assertions, not positive medical relations.

## MAxO ontology (`maxo`)

- [Official repository](https://github.com/monarch-initiative/MAxO), [release v2026-01-15](https://github.com/monarch-initiative/MAxO/releases/tag/v2026-01-15).
- [License](https://github.com/monarch-initiative/MAxO/blob/master/LICENSE): **CC-BY-4.0**. Commercial reuse/adaptation is permitted by that license subject to attribution, license link, indication of changes and its other terms; this is not MIT. Retain release provenance and review upstream included content notices.

Implemented: MAxO OBO Graph JSON and OBO 1.x; same retained fields as HPO. MAxO contains varied medical actions, so terms become generic MedicalProcedureDefinition, without guessing which are treatments, examinations or recommendations. No action is linked to a disease from a name or ontology superclass. OWL and clinical action classification remain unsupported.

## MAxO annotations (`maxo-annotations`)

Checked independently: [repository](https://github.com/monarch-initiative/maxo-annotations), [license](https://github.com/monarch-initiative/maxo-annotations/blob/main/LICENSE), [current data file](https://github.com/monarch-initiative/maxo-annotations/blob/main/data/maxo-annotations.tsv), and [format description](https://github.com/monarch-initiative/maxo-annotations#annotation-format).

The annotation repository separately declares **CC-BY-4.0**; retain attribution and exact commit/release provenance, and check source-specific evidence/third-party notices. A statement about the MAxO ontology alone is not used as the annotation license.

The implemented current repository schema has fifteen columns: `disease_id`, `disease_name`, `source_id`, `maxo_id`, `maxo_name`, `hpo_id`, `relation`, `evidence`, `extension_id`, `extension_name`, `comment`, `other`, `author`, `last_updated`, `created`. The actual header was checked from the official file; its latest file-change commit observed during this review was `dacb19ca161573247ee9848125eba8326b5df474`. Older twelve-column `maxo_relation` layouts are **not supported** and fail with missing-header errors. Select a release with the documented layout; do not assume every historical asset has it.

Imported: explicit disease ID/name, action/phenotype references, optional disease/extension context, exact predicate, evidence, source reference and all original columns. The SDK MedicalAnnotation and normalized sidecar preserve the full multi-way assertion. `TREATS`, `PREVENTS` and other predicates are not collapsed into an unconditional diseaseTreatment relation. Extension terms may be external chemical/ontology identifiers; they remain explicitly external and are not invented SDK definitions.

## ICD-10-GM (`icd-10-gm`) and OPS (`ops`)

Official sources checked: [BfArM classifications](https://www.bfarm.de/DE/Kodiersysteme/Klassifikationen/_node.html), [2026 ICD-10-GM terminology publication](https://terminologien.bfarm.de/CodeSystem-icd10gm-2026.html), [2026 OPS terminology publication](https://terminologien.bfarm.de/ValueSet-ops-2026.html), and [official download terms (2025 document)](https://www.bfarm.de/SharedDocs/Downloads/DE/Kodiersysteme/downloadbedingungen-2025.pdf?__blob=publicationFile). The official terminology pages describe ClaML as their source format. Historical official download pages were also consulted to establish that terms vary across editions.

**Restrictions:** these are not MIT datasets. The consulted newer official terms refer to protection of unmodified codes/texts, attribution, edition-specific appendices and (for ICD) advertising restrictions. Older terms additionally restrict passing on the acquired raw format. Do not infer a universal redistribution right from a historical edition or from availability on a terminology server. The attempted guessed 2026 ClaML download-detail URLs returned soft error pages; they are not documented as verified download links. Retrieve the exact official file and accompanying current terms through BfArM's catalog locally and record that URL/version/terms. No automatic download/consent is implemented. Rights for the proposed normalized/game use require exact-edition review.

Implemented: ClaML XML `Class kind="category"`, `code`, preferred `Rubric/Label` (original language, normally de), superclass references, title/version, and the complete original class XML. ICD category records become sourced diagnosis definitions; OPS categories generic procedures. Chapters/blocks are identified as classification groups and not diseases/procedures. External DTD declarations are ignored; no external resolver/network access is allowed.

Not implemented: positional BfArM metadata/alpha CSV, FHIR, modifier expansion, post-coordination, coding rules, ICD-to-HPO equivalence or disease-treatment recommendations. Inclusion/exclusion/modifier markup is preserved as source XML, not interpreted as clinical criteria. This is a category/label import, not a complete medical coding engine.

## LOINC (`loinc`)

Official sources: [license, updated 2026-08-06](https://loinc.org/kb/license), [Loinc.csv table structure](https://loinc.org/kb/users-guide/loinc-database-structure/loinc-table-structure), [official download/access page](https://loinc.org/get).

LOINC permits commercial and non-commercial use under its own terms, with restrictions on changing source fields/creating a competing observation standard and requirements for attribution, original identifiers/display names and third-party content. Redistribution is conditional, not MIT. Release-specific terms and third-party notices must accompany permitted extracts; retain their full original text via `--license-file`. No LOINC download/account access is attempted. User supplies the legitimately acquired uncompressed CSV and exact release version.

Implemented: comma-separated CSV with quoted/multiline fields, named headers `LOINC_NUM`, `LONG_COMMON_NAME`, `STATUS`; every original column/value is retained unchanged. Observation, panel and document codes become neutral ObservationDefinition (adapter 1.1.0); original code/display label/status/copyright notices remain associated. `DEPRECATED` terms are staged but cannot be approved; other non-ACTIVE statuses generate diagnostics. Descriptions, when supplied in `DefinitionDescription`, remain original source text. No disease indications, cutoffs, performance, clinical interpretation, panel decomposition, RELMA mappings, answer files or probabilities are inferred. Localization escapes literal braces for safe rendering while source field content remains unchanged.

## Practical export policy

1. Keep real data outside the repository. Supply exact versions, source URL, full original license/notices and attribution.
2. Review import diagnostics, unresolved references and synonym candidates. Exact source identifiers/alternate IDs are the only automatic reference resolution; text matching proposes candidates only.
3. Review records independently from rights. No clinical approval follows from technical validation.
4. Make a source-specific rights decision only after the intended use is covered. Different releases may have different conditions. A manual `permitted` label is an audit decision, not automated legal verification.
5. Export preserves full approved assertion context and original notices. Missing translations remain missing. The public repository's example approvals concern fictional MIT test data only.

## Full-dataset verification in this review

The public HPO v2026-09-01 and MAxO v2026-01-15 ontology release files were downloaded unchanged for local technical tests in JSON and OBO. Full input hashes, exact URLs, counts, errors, repeated-output hashes and measured resources are in [the integration report](../full-dataset-tests.md). HPO license conditions permit downloading/using the original ontology; the tests retain it privately without alterations, public display or release export. This is not an authorization to translate or redistribute a game adaptation. The complete official HPO web license-page source and exact-release MAxO license are recorded under `terms/` solely as licensing notices, not data.

HPOA/OMIM annotation production datasets, BfArM ICD-10-GM/OPS and LOINC production files were **not** downloaded automatically, acquired via accounts or redistributed. No legitimately acquired local full inputs/configuration were supplied for the latter three sources; complete-file tests remain open. Use private input descriptors with exact SHA-256, release metadata and terms; the runner cannot download custom or restricted sources. The standard importer remains entirely offline.

## Wikidata (`wikidata`)

Official [structured-data licensing](https://www.wikidata.org/wiki/Wikidata:Licensing), [statements](https://www.wikidata.org/wiki/Help:Statements), [ranks](https://www.wikidata.org/wiki/Help:Ranking) and [SPARQL documentation](https://www.wikidata.org/wiki/Wikidata:SPARQL_tutorial) checked 2026-10-16; supported P-property identities were checked through the official wbgetentities API. Structured data is CC0-1.0, permitting commercial reuse, modification and redistribution subject to the actual waiver/legal code. This does not relicense linked articles, images or other third-party material; those are not downloaded/copied. Import requires the actual CC0 notice, snapshot/source URL/version and retrieval timestamp. CC0 never implies medical approval.

Supported: saved SPARQL SELECT JSON/CSV in [the documented profile](../wikidata-import.md), Q identities, actual en/de labels/descriptions/aliases, P780/P923/P924/P828, statement identities, ranks, references, qualifiers and explicit HPO/ICD identifiers. MAxO matches require a verified explicit system/ID binding; no property was invented. Unsupported or untyped values remain unresolved. Entity source claims keep entity metadata; full medical statement rows stay on separately reviewed assertions so approving a disease label cannot release an unapproved statement. No live medical SPARQL download occurred in implementation/tests. All committed Wikidata-shaped fixture rows are fictional MIT examples, not a published source extract.
