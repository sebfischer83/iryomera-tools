# Name importer

`Iryomera.NameImporter` (tool command `iryomera-names`) stages local name source files in a strict workspace, records review and redistribution decisions, and exports approved names as an SDK content package with `namePools` ([ADR 0011](architecture/0011-name-pools.md), [plan](plans/names.md)). It depends only on the SDK and Localization. It makes no network requests, never downloads data, and never approves names or grants rights on its own. Name origin is never linked to medical or balancing data.

## Run

```sh
dotnet run --project src/Iryomera.NameImporter -c Release -- sources
```

The tool is also packed as `Iryomera.NameImporter` and published with tagged releases ([releases](releases.md)). The default workspace is `.iryomera/names-workspace.json`; pass `--workspace` to choose another file.

## Adapters

| Source | Input | Notes |
| --- | --- | --- |
| `us-ssa` | One uncompressed `yobYYYY.txt` from the SSA national baby names file (`Name,Sex,Count`, no header) | Requires `--birth-year`. Country US, given names, sex mapped to `female`/`male` as reported. Counts kept; no rank or proportion is derived. |
| `us-census-surnames` | `Names_2010Census.csv` from the Census 2010 surname file (`name,rank,count,prop100k,...`) | Country US, family names. Race and Hispanic-origin columns are never read or staged. Uppercase names are capitalized (first letter and letters after `-`, `'` or space), which can differ from real spellings such as McDonald; originals stay in the source fields. `prop100k` becomes a proportion. The `ALL OTHER NAMES` row is skipped. |
| `at-statistik` | `OGDEXT_VORNAMEN_1.csv` (semicolon CSV: year, district, sex, normalized name, count) | Country AT, given names, single-year cohorts. Rows are summed per year, sex and name across districts; rare names exist only as Austria-wide rows, so nothing is counted twice. District rows stay in the source fields. CC BY 4.0: keep the attribution from `NOTICE.md`. |
| `wikidata-names` | Saved SPARQL SELECT JSON from the query profile [`examples/wikidata/names.rq`](../examples/wikidata/names.rq) (bindings `item`, `type`, `statement`, `rank`, `name`, optional `romanized`) | Name items only, never people. Requires `--license CC0-1.0`, `--retrieved-at` and at least one `--language-country ja=JP`. **The country comes only from your explicit mapping of the label language**; it is stored in the batch identity, and labels in unmapped languages are not staged. Item types give part and gender (given/female/male/unisex, family). Deprecated statements are not staged. No frequencies exist, so entries have no rank or count. Q IDs become attestation external IDs; all bindings stay in the source fields. |
| `names-sigpwned` (local only) | `common-forenames-by-country.csv` or `common-surnames-by-country.csv` | **Never released:** the license is not safe (declared CC0, but derived from Wikipedia lists under CC BY-SA, with partly machine-translated romanizations). Its batches cannot be set to `permitted` and are never exported. Detected by header. Index/Rank become ranks, the forename `Year` a single-year cohort, surname `Percent` a proportion when within [0,1]. Free-text regional lists are staged country-wide with a `region-not-mapped` warning. Romanizations are marked as possibly machine-translated. |

Only sources with a safe license (CC0/public domain or CC BY 4.0 with attribution) are released (owner decision 2026-10-17). License status of these and further candidate sources is recorded in [name source candidates](data-sources/names.md). Re-verify the exact release terms before every import and supply them with `--license` and `--license-file`.

Normalization is explicit and part of the adapter version: surrounding whitespace is removed (`name-trimmed`), text is converted to NFC (`name-normalized`), and names that still violate the SDK name rules are not staged (`name-text`). Romanized values that are not valid Latin-script name text are dropped (`romanized-dropped`). The original row values stay in `sourceFields`. Input must be UTF-8; invalid bytes reject the whole file.

## Pipeline

1. **Import** — `import <source> --input <file> --source-version <v> --source-url <url> --license <id> --license-file <file> --attribution <text> [--imported-at <ts>] [--birth-year <yyyy>]`. The batch ID is derived from the input SHA-256, the options and the adapter version; re-importing identical input is idempotent. Supply `--imported-at` for reproducible runs. Imports are always unreviewed.
2. **Review** — `review --batch <id> (--all | --id <record>...) --status approved|rejected|inReview|unreviewed --reviewer <label> --at <ts> --note <text>`. Decisions are append-only. A record decision overrides the batch-wide decision, so a batch can be approved while single records are rejected.
3. **Rights** — `rights --batch <id> --decision permitted|blocked|undecided [--permission-evidence <text>] --reviewer <label> --at <ts> --note <text>`. `permitted` requires evidence for the exact source release and intended use. Review and rights are independent.
4. **Export** — `export --output <new dir> --version <semver> [--max-entries-per-pool <n>]`. Only approved records of batches with permitted redistribution are exported. Records are grouped into pools per country, region and part (`<ns>:us.given`); identical name strings and genders from several batches become one entry with one attestation per source record, so statistics stay per source. Entry keys depend only on the name string and gender and stay stable across releases. Differing romanizations of one entry block the export. The output directory contains `content.json`, `localization/en.json` and `de.json` (pool labels only), `NOTICE.md` with every source license and attribution, and `release-report.json` with aggregate counts. Rejected, unreviewed or blocked records never appear in any output. Existing outputs are never overwritten. With `--max-entries-per-pool` each pool keeps at most n entries: counts are compared only within one dataset (sources with the same source URL and version, such as the birth years of one SSA release), the dataset with the largest total count ranks entries by their summed count, and entries without such counts follow in SHA-256 order of the entry key (stable, no alphabetical bias). Kept entries keep all attestations; `NOTICE.md` records the cap and `release-report.json` counts only exported records.

`validate` lists import errors and staged text that no longer satisfies the SDK rules; `status [--records]` summarizes review and rights per batch.

## Worked example (PowerShell)

The tool never downloads anything. Download each file yourself from the official source, keep it outside the repository, and re-check the terms on the day you import. All paths below are examples; nothing here may be committed (AGENTS.md).

Setup: install the tool from the feed ([releases](releases.md)) and prepare license files with the full terms you accepted, for example the CC0 legal code (`https://creativecommons.org/publicdomain/zero/1.0/legalcode.txt`) and the CC BY 4.0 legal code (`https://creativecommons.org/licenses/by/4.0/legalcode.txt`), each followed by a line recording where the dataset states its license and when you checked it.

```powershell
dotnet tool install Iryomera.NameImporter --version 0.8.0 --tool-path .\tools
$names = ".\tools\iryomera-names.exe"
$ws = "D:\iryomera-data\names-workspace.json"
$now = "2026-10-18T10:00:00Z"   # fixed timestamps make re-runs reproducible
```

**US SSA given names** — download `https://www.ssa.gov/oact/babynames/names.zip` (data.gov lists CC0-1.0), extract it, then import one file per birth year:

```powershell
foreach ($year in 1940..2025) {
  & $names import us-ssa --input "D:\iryomera-data\ssa\yob$year.txt" --birth-year $year `
    --source-version "<SSA release date>" --source-url "https://www.ssa.gov/oact/babynames/limits.html" `
    --license CC0-1.0 --license-file "D:\iryomera-data\licenses\cc0-ssa.txt" `
    --attribution "U.S. Social Security Administration, national baby names" --imported-at $now --workspace $ws
}
```

**US Census 2010 surnames** — download the full surname file (zipped CSV) linked on `https://www.census.gov/topics/population/genealogy/data/2010_surnames.html` (data.gov lists CC0-1.0) and extract `Names_2010Census.csv`:

```powershell
& $names import us-census-surnames --input "D:\iryomera-data\census\Names_2010Census.csv" `
  --source-version "2010 Census surnames" --source-url "https://www.census.gov/topics/population/genealogy/data/2010_surnames.html" `
  --license CC0-1.0 --license-file "D:\iryomera-data\licenses\cc0-census.txt" `
  --attribution "U.S. Census Bureau, Frequently Occurring Surnames from the 2010 Census" --imported-at $now --workspace $ws
```

**Statistik Austria given names** — download `https://data.statistik.gv.at/data/OGDEXT_VORNAMEN_1.csv` (CC BY 4.0, see the dataset page `https://data.statistik.gv.at/web/meta.jsp?dataset=OGDEXT_VORNAMEN_1`):

```powershell
& $names import at-statistik --input "D:\iryomera-data\at\OGDEXT_VORNAMEN_1.csv" `
  --source-version "<dataset update date>" --source-url "https://data.statistik.gv.at/web/meta.jsp?dataset=OGDEXT_VORNAMEN_1" `
  --license CC-BY-4.0 --license-file "D:\iryomera-data\licenses\cc-by-4.0-statistik-austria.txt" `
  --attribution "Statistik Austria, Vornamen (OGDEXT_VORNAMEN_1), CC BY 4.0" --imported-at $now --workspace $ws
```

**Wikidata name items** — run [`examples/wikidata/names.rq`](../examples/wikidata/names.rq) on `https://query.wikidata.org/`, adjusting the language filter, and save the result as SPARQL JSON. Record the actual retrieval time and your language-to-country decision:

```powershell
& $names import wikidata-names --input "D:\iryomera-data\wikidata\names-ja.json" `
  --language-country ja=JP --retrieved-at "2026-10-18T09:30:00Z" `
  --source-version "names.rq v1, 2026-10-18" --source-url "https://query.wikidata.org/" `
  --license CC0-1.0 --license-file "D:\iryomera-data\licenses\cc0-wikidata.txt" `
  --attribution "Wikidata contributors" --imported-at $now --workspace $ws
```

**Review, rights and export** — check errors and counts, then decide per batch. Take the batch IDs from the `id` field of each import output or from `status`:

```powershell
& $names validate --workspace $ws
& $names status --workspace $ws
& $names review --workspace $ws --batch <batch-id> --all --status approved --reviewer "<your name>" --at $now --note "Checked sample and diagnostics"
& $names review --workspace $ws --batch <batch-id> --id <record-id> --status rejected --reviewer "<your name>" --at $now --note "Unsuitable entry"
& $names rights --workspace $ws --batch <batch-id> --decision permitted --permission-evidence "<license URL and date checked>" --reviewer "<your name>" --at $now --note "CC0 / CC BY 4.0 verified"
& $names export --workspace $ws --output "D:\iryomera-data\release\names-1.0.0" --version 1.0.0
```

The export directory can then be packed with ModKit or shipped as SDK content. Keep `NOTICE.md` with it; CC BY 4.0 sources require the attribution to stay visible to players.

## Formats and limits

The workspace format is `schemas/names/v1` and rejects unknown properties, unlike the additive SDK format. Writes take a `.lock` file and replace the workspace atomically. Name formats (`nameFormats`) are not produced by the importer; author them as content. INE, German city data and BFS are not used until their terms are clear. Exported names describe no real person; combinations are made by the game.
