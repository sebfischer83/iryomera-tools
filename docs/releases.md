# Releases and package feed

Released packages are published to the GitHub Packages NuGet feed of the repository owner: `https://nuget.pkg.github.com/sebfischer83/index.json`. Nothing is published to nuget.org. See [ADR 0012](architecture/0012-github-packages-feed.md).

| Package | Use |
| --- | --- |
| `Iryomera.Content.SDK` | net8.0/net10.0 content contracts and validation (game and tools) |
| `Iryomera.Localization` | net8.0/net10.0 en/de catalogs and formatting (game and tools) |
| `Iryomera.MedicalImporter` | .NET tool `iryomera-medical` |
| `Iryomera.ModKit` | .NET tool `iryomera-mod` |
| `Iryomera.NameImporter` | .NET tool `iryomera-names` |

All five share the single `<Version>` in `Directory.Build.props`.

## Publishing a release

1. Raise `<Version>` in `Directory.Build.props` in a pull request, together with the change that needs it, and merge it into `main` after CI passes.
2. Tag the merged commit on `main` and push the tag:

   ```sh
   git tag v0.6.0
   git push origin v0.6.0
   ```

3. The tag runs the full Tools CI on Windows and Linux. Only if both pass, the `publish` job takes the five `.nupkg` files built and tested by the Ubuntu job, checks that they carry exactly the tag version, and pushes them with the workflow's `GITHUB_TOKEN`. A tag that does not match `<Version>` fails without publishing. ContentStudio is not published; on tag runs its browser smoke test is reported but does not block publishing, while pull requests and branch pushes still require it to pass.

A published version is immutable. Fix mistakes with a new version; never move or reuse a release tag. Re-running the workflow for the same tag skips packages that already exist.

## Consuming the feed

GitHub Packages requires authentication for every NuGet install, including public packages. Use a personal access token (classic) with only the `read:packages` scope. Never commit the token.

Example `nuget.config` for a consumer such as the game. Package source mapping keeps every other package on nuget.org and is required with central package management (NU1507):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="iryomera" value="https://nuget.pkg.github.com/sebfischer83/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
    <packageSource key="iryomera"><package pattern="Iryomera.*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

Store the credentials outside the repository, for example in the user-level NuGet configuration:

```sh
dotnet nuget update source iryomera --username <github-user> --password <token> --store-password-in-clear-text --configfile ~/.nuget/NuGet/NuGet.Config
```

In a GitHub Actions workflow of another repository, use a repository secret holding such a token, or grant that repository read access to the packages and use its `GITHUB_TOKEN`.

Install a tool from the feed:

```sh
dotnet tool install Iryomera.ModKit --version 0.20.3 --tool-path ./tools
```

Packaging and publication are technical distribution steps. They grant no clinical approval and no rights to third-party data; packages contain only MIT code and synthetic examples.
