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
| `names-sigpwned` | `common-forenames-by-country.csv` or `common-surnames-by-country.csv` | Detected by header. Index/Rank become ranks, the forename `Year` a single-year cohort, surname `Percent` a proportion when within [0,1]. Free-text regional lists are staged country-wide with a `region-not-mapped` warning. Romanizations are marked as possibly machine-translated. |

License status of these and further candidate sources is recorded in [name source candidates](data-sources/names.md). Re-verify the exact release terms before every import and supply them with `--license` and `--license-file`.

Normalization is explicit and part of the adapter version: surrounding whitespace is removed (`name-trimmed`), text is converted to NFC (`name-normalized`), and names that still violate the SDK name rules are not staged (`name-text`). Romanized values that are not valid Latin-script name text are dropped (`romanized-dropped`). The original row values stay in `sourceFields`. Input must be UTF-8; invalid bytes reject the whole file.

## Pipeline

1. **Import** — `import <source> --input <file> --source-version <v> --source-url <url> --license <id> --license-file <file> --attribution <text> [--imported-at <ts>] [--birth-year <yyyy>]`. The batch ID is derived from the input SHA-256, the options and the adapter version; re-importing identical input is idempotent. Supply `--imported-at` for reproducible runs. Imports are always unreviewed.
2. **Review** — `review --batch <id> (--all | --id <record>...) --status approved|rejected|inReview|unreviewed --reviewer <label> --at <ts> --note <text>`. Decisions are append-only. A record decision overrides the batch-wide decision, so a batch can be approved while single records are rejected.
3. **Rights** — `rights --batch <id> --decision permitted|blocked|undecided [--permission-evidence <text>] --reviewer <label> --at <ts> --note <text>`. `permitted` requires evidence for the exact source release and intended use. Review and rights are independent.
4. **Export** — `export --output <new dir> --version <semver>`. Only approved records of batches with permitted redistribution are exported. Records are grouped into pools per country, region and part (`<ns>:us.given`); identical name strings and genders from several batches become one entry with one attestation per source record, so statistics stay per source. Entry keys depend only on the name string and gender and stay stable across releases. Differing romanizations of one entry block the export. The output directory contains `content.json`, `localization/en.json` and `de.json` (pool labels only), `NOTICE.md` with every source license and attribution, and `release-report.json` with aggregate counts. Rejected, unreviewed or blocked records never appear in any output. Existing outputs are never overwritten.

`validate` lists import errors and staged text that no longer satisfies the SDK rules; `status [--records]` summarizes review and rights per batch.

## Formats and limits

The workspace format is `schemas/names/v1` and rejects unknown properties, unlike the additive SDK format. Writes take a `.lock` file and replace the workspace atomically. Name formats (`nameFormats`) are not produced by the importer; author them as content. Further adapters (Wikidata name items, US Census surnames, INE, Statistik Austria) follow the plan. Exported names describe no real person; combinations are made by the game.
