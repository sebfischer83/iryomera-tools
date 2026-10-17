# Name source candidates

Review date: **2026-10-17**. This is a pre-implementation engineering record of the official materials consulted for a future name importer (see [plan](../plans/names.md) and [ADR 0011](../architecture/0011-name-pools.md)). It is not a permission grant. Every adapter must re-verify the exact release, terms URL and access date at implementation time and record them in batch metadata. MIT covers only our code and synthetic fixtures.

Common rules for every source:

- Import name *items* (strings plus aggregate statistics), never records of real persons. Do not query Wikidata humans or count people per name from person items.
- Never import ethnicity, race, nationality or similar columns, even where a source publishes them. Name origin must not become a medical or balancing input.
- Keep source-specific weights per source; counts from different sources are not comparable and are not merged into one number.
- Keep original spelling/script exactly; romanizations keep their stated origin.

## Summary

| Source | Content | License found | Decision |
|---|---|---|---|
| sigpwned popular-names-by-country | Top given/family names, 106/75 countries | CC0 claimed; data derived from Wikipedia | Low-priority fallback only, flagged |
| Wikidata name items | Given/family name items, language, script | CC0 (Wikidata structured data) | Candidate, primary variety source |
| US SSA baby names | Given names by birth year and sex since 1880 | CC0 on data.gov | Candidate |
| US Census 2010 surnames | 162,253 surnames, counts | CC0 on data.gov | Candidate, drop race columns |
| INE Spain | Given names by birth decade, surnames | CC BY 4.0 per INE legal notice | Candidate with attribution |
| Statistik Austria | Given names by birth year since 1984 | CC BY 4.0 | Candidate with attribution |
| City open data DE (Berlin, Köln) | Newborn given names since ~2010/2012 | CC BY 3.0 DE (dataset-level metadata inconsistent) | Verify per dataset |
| BFS Switzerland | Given names of population by birth year | Open use, attribution, commercial use needs permission | User-supplied only |
| DFD (German surnames dictionary) | ~200k German surnames | Terms not found | Not usable until terms are confirmed |

## sigpwned popular-names-by-country (`names-sigpwned`)

- [Repository](https://github.com/sigpwned/popular-names-by-country-dataset). Data collected from Wikipedia lists of most common given names and surnames in the week of 2023-07-08; the maintainer states no plan to update it.
- Files: `common-forenames-by-country.csv` (columns `Country, Country Group, Region, Population, Note, Year, Romanization, Index, Name Group, Gender, Localized Name, Romanized Name`) and `common-surnames-by-country.csv` (`Country, Rank, Index, Name Group, Localized Name, Romanized Name, Count, Percent`).
- Observed coverage (2026-10-17): DE 20 given names (2022 newborns) and 10 surnames; US 40 given names (1990 and 2022) and 100 surnames; JP 39 given and 112 surnames; ES 60 given (2021) and 40 surnames. Counts present for some surname rows only.
- The repository declares CC0 1.0. The underlying Wikipedia pages are CC BY-SA and themselves cite national statistics of varying terms; some romanizations are stated to come from Google Translate.

**Decision:** usable as a low-priority fallback for broad country coverage. Record the CC0 declaration *and* the Wikipedia derivation in provenance. Romanizations from this source are marked unverified. Given names are newborn rankings and must carry their year, never be presented as population-wide.

## Wikidata name items (`names-wikidata`)

- Reuses the existing Wikidata SPARQL infrastructure ([ADR 0009](../architecture/0009-wikidata.md)). Wikidata structured data is CC0.
- Query items that are instances of given name (Q202444, including subclasses such as male/female/unisex given name) or family name (Q101352), with native label, writing system, language and transliteration statements. Retain Q/P/statement identities and references as in ADR 0009.
- No frequency information; useful for variety and script coverage, not weighting.

**Decision:** candidate primary variety source. Never derive weights from counts of human items using a name.

## US SSA baby names (`names-us-ssa`)

- [SSA limits/readme page](https://www.ssa.gov/oact/babynames/limits.html), national file `https://www.ssa.gov/oact/babynames/names.zip` (about 7 MB). Name, sex, birth year and count from Social Security card applications since 1880; names with fewer than 5 occurrences are suppressed.
- [data.gov catalog entry](https://catalog.data.gov/dataset/baby-names-from-social-security-card-applications-national-data) lists CC0 1.0. SSA pages themselves state no license.

**Decision:** candidate. Gives birth-year cohorts for older patients. Record that sex is the binary value reported in the source.

## US Census 2010 surnames (`names-us-census`)

- [Census page](https://www.census.gov/topics/population/genealogy/data/2010_surnames.html): surnames occurring at least 100 times, full list of 162,253 names (zipped CSV/Excel) plus methodology PDF.
- [data.gov catalog entry](https://catalog.data.gov/dataset/2010-decennial-census-of-population-and-housing-surnames) lists CC0 1.0.
- The file contains race/Hispanic-origin percentage columns.

**Decision:** candidate. Import only name, rank, count and proportion; drop all race/ethnicity columns at adapter level and record that in the adapter identity.

## INE Spain (`names-es-ine`)

- [INE names/surnames application](https://www.ine.es/daco/daco42/nombyapel/nombyapel2.htm) and [methodology note](https://ine.es/daco/daco42/nombyapel/nota_nombres.pdf): based on the population register; top 50 given names per birth decade nationally, top 20 per province; names with fewer than 20 holders nationally and cells under 5 suppressed; latest data 2025-01-01, published 2026-04-23. Excel/CSV export.
- [INE legal notice](https://ine.es/dyngs/AYU/index.htm?cid=125): statistical information whose original source is INE may be reused under CC BY 4.0, including commercially, citing the source and the last-update date, without implying INE endorsement (Real Decreto 1495/2011). The page footer shows CC BY-SA 4.0, which conflicts with the body text; record both and confirm before redistribution.
- Nationality breakdowns exist and must not be imported.

**Decision:** candidate with attribution and update date; resolve the BY vs BY-SA footer inconsistency before release export.

## Statistik Austria (`names-at-statistik`)

- [Open data portal](https://data.statistik.gv.at/web/meta.jsp?dataset=OGDEXT_VORNAMEN_1), dataset `OGDEXT_VORNAMEN_1`: given names of live births by year, sex and district since 1984 (1984–2009 Austrian citizens only, all births since 2010). Rare names only as national yearly counts.
- License shown on the dataset page: CC BY 4.0.

**Decision:** candidate. Best openly licensed cohort source for German-language given names found so far (birth years from 1984). District data is not needed.

## German city open data (`names-de-city`)

- Berlin "Liste der häufigen Vornamen" (Landesamt für Bürger- und Ordnungsangelegenheiten): given name, count, sex, position per district and year. Secondary sources cite CC BY 3.0 DE; registry metadata seen for 2017 is inconsistent ("cc-by" and "No License Provided").
- Köln ["Vornamen 2010-2018"](https://ckan.open.nrw.de/dataset/vornamen-2010-2018-k) (Standesamt Köln); the city's open data is generally CC BY 3.0 DE, but no license field was found on the dataset entry.

**Decision:** verify the license on each concrete dataset page before adding an adapter. Newborns only (2010+), so these add recent variety, not older cohorts.

## BFS Switzerland (`names-ch-bfs`)

- [opendata.swiss](https://opendata.swiss/de/dataset/weibliche-vornamen-der-bevolkerung-nach-jahrgang-schweiz-2024/resource/7fcb6085-acff-41d7-ae09-e8e7fe05437c): male/female given names of the resident population by birth year (yearly editions). Terms: open use, source attribution required, commercial use requires the data owner's permission. Direct fetch of the dataset page returned HTTP 403 during review; terms taken from portal search excerpts.

**Decision:** would be the best cohort source for German-language names, but the commercial-use restriction blocks redistribution in a commercial game without permission. Allow only as local user-supplied input with redistribution undecided, unless the owner obtains permission.

## Gaps

- No openly licensed source for German given names of birth years before 1984 or for German surname frequencies was found. The DFD ([Mainz Academy project page](https://www.adwmainz.de/en/research/projects/details/das-digitale-familiennamenwoerterbuch-deutschlands-dfd.html)) has published no reuse terms we could find. Owner decision 2026-10-17: the DFD is not contacted and not used; German surnames start from Wikidata and the sigpwned fallback without frequency statistics.
- Japan: no official open frequency source found; Wikidata plus the sigpwned fallback only.
