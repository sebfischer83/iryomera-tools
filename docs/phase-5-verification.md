# Phase 5 verification

Version: 0.6.0. Verification date: 2026-10-16. [Plan](plans/phase-5.md), [ADR 0011](architecture/0011-optional-modio-distribution.md), [API/integration guide](modio.md).

## Merge and scope

Verified Phase 4 was merged into main at `56d9bfe` before work on `feature/phase-5-modio`. [Merge CI run 38964870268](https://github.com/sebfischer83/iryomera-tools/actions/runs/38964870268) completed successfully on Linux and Windows. Its initial Windows attempt reached a 30-second Studio browser selector timeout after a language/navigation change; the failed job passed on attempt 2 without code changes. Phase 5 strengthens document/culture/route readiness waits and includes synthetic failure-page diagnostics rather than hiding the observed flake.

Implemented scope is the optional portable REST client, email authentication, search/details, subscription/unsubscription, bounded remote dependency planning, file-identity update checks, verified HTTPS downloads, local/remote delivery/runtime abstractions and an optional ModKit installer/publisher. Existing ModKit application APIs are extracted into a normal portable Core library because an executable NuGet tool is not a usable library dependency. CLI/API namespaces and existing content/archive wire formats remain unchanged. Receipts have a separate versioned offline schema.

## Verification

Local host: Debian 13; .NET SDK 10.0.402, runtimes 8.0.32 / 10.0.13.

| Check | Observed result |
| --- | --- |
| Complete Release build | 0 warnings, 0 errors |
| Foundation net8 / net10 | 137 / 137 passed |
| Importer / Studio | 95 / 17 passed |
| ModKit net8 / net10 | 43 / 44 passed; the CLI-only case runs on net10 |
| ModIo mock HTTP net8 / net10 | 56 / 56 passed |
| ModIo–ModKit integration net8 / net10 | 9 / 9 passed |
| Total .NET test executions | 603 passed; prior 430 retained, portable suites execute on both runtimes |
| Python acquisition tests | 4 passed |
| Locked restore | Passed |
| Importer/ModKit/offline distribution examples | Passed; no mod.io socket or account operation |
| NuGet pack | Seven packages generated; existing non-packable Studio emits its expected pack warning |

New tests cover exact auth/form/multipart shape, credential/log/exception redaction, required/malformed JSON, size limits, Retry-After and cancellation, safe/unsafe redirects, checksums, partial/oversized downloads, cycle/depth/node/pagination errors, file-identity updates including rollbacks, independent local operation, validated remote delivery, receipt schemas/extensions and rejected publication before HTTP writes. Existing archive attack tests also run on net8 after the Core extraction.

The fresh locally installed CLI passed create/validate/pack/inspect/unpack with exact payload bytes using only the local NuGet source. Packed ModIo/Core/bridge net8 binaries passed a real Godot 4.7.2 .NET run: mock HTTP, receipt schema validation and actual local archive creation/installation on runtime 8.0.32. The published Studio browser smoke passed twice, including DE/EN switches, SQLite editing, SVG, review/rights and approved ZIP export with external application requests blocked.

No live mod.io account/game request is part of testing.

## GitHub Actions

Both runs completed successfully on 2026-10-16, testing implementation commit `c6514b5cc8ff5b3add7ad57b576c6cb712101df8` on `feature/phase-5-modio`. All four jobs passed on their first attempt.

| Run | Linux | Windows | Scope |
| --- | --- | --- | --- |
| [Push 38967511848](https://github.com/sebfischer83/iryomera-tools/actions/runs/38967511848) | Passed | Passed | Complete standard Release matrix |
| [Full-dataset 38967510834](https://github.com/sebfischer83/iryomera-tools/actions/runs/38967510834) | Passed | Passed | Complete Release matrix plus four pinned official HPO/MAxO ontology cases, each imported twice |

The matrix regenerates schemas/examples, restores in locked mode, builds and runs all 603 .NET test executions and four Python tests, executes Foundation/importer/ModKit/offline-distribution examples, publishes/tests the bilingual Studio in Chromium, packs seven NuGet packages, installs the CLI from only the local package feed and exercises portable normal packages in real Godot. The optional ontology regression preserves its existing expected diagnostic baselines; a pass is not a clean medical release or clinical approval. Raw datasets are not uploaded as artifacts.

The final verification-only commit leaves the tested implementation unchanged. Phase 4 is merged into main; Phase 5 remains separately reviewable on its feature branch.

## Limits and remaining work

- Email code authentication is implemented; platform/social logins, refresh, host consent UI and persistent platform secret stores are not. An explicitly configured host is responsible for mod.io terms/account/game permissions.
- Single-file uploads stay within the bounded archive limit. Multipart sessions for very large files, marketplace, moderation and per-platform selection are outside this implementation.
- Dependencies resolve to a bounded remote-ID graph. Actual SDK versions/identities remain separately validated; installed registry, remote-to-package mappings, activation/rollback and batch transactions are host responsibilities.
- No live server acceptance, account authentication, email delivery or publication is claimed without supplied configuration. Mock tests establish request/response behavior against reviewed official documentation.
- MD5/SHA-256/index checks provide integrity, not signatures, publisher trust, clinical approval, antivirus certification or a legal rights grant. Downloads require an explicit approved HTTPS host policy.
- Existing medical-source restrictions and pending complete-file ICD/OPS/LOINC validation remain unchanged. No restricted original dataset is downloaded or committed.
