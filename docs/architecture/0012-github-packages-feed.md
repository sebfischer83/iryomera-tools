# ADR 0012 — GitHub Packages as the release feed

Status: accepted on 2026-10-17.

## Context

The game will consume `Iryomera.Content.SDK` and `Iryomera.Localization` as pinned NuGet packages from its milestone M7, starting with 0.6.0 for name pools ([ADR 0011](0011-name-pools.md)). Until now packages existed only as CI artifacts and local pack folders. Publication to nuget.org is not intended.

## Decision

- Publish all packable projects (SDK, Localization and the MedicalImporter, ModKit and NameImporter tools) to the GitHub Packages NuGet feed of the repository owner.
- Publish only from a `v*` tag push. The `publish` job in Tools CI depends on the full Windows and Linux job, pushes exactly the `.nupkg` files the Ubuntu job built and tested, and fails unless the expected package set carries the tag version. No prerelease feed from `main`.
- Authenticate the push with the workflow's `GITHUB_TOKEN` and job-scoped `packages: write`; no stored publishing secret. `RepositoryUrl` links the packages to this repository, so they inherit its visibility.
- ContentStudio is not published. On tag runs its browser smoke test is non-blocking (`continue-on-error`), so a flaky UI test cannot hold back package publication; pull requests and branch pushes still require it. Amended 2026-10-17 after Playwright timeouts on Windows.
- Versions are immutable. `--skip-duplicate` makes workflow re-runs idempotent; it does not permit replacing a version.

## Consequences

Consumers need a personal access token with `read:packages` even for public packages, and a `nuget.config` with package source mapping. The process is documented in [releases and package feed](../releases.md). Publishing is distribution only: it does not change review status, clinical approval or data rights. mod.io distribution (Phase 6) remains separate.
