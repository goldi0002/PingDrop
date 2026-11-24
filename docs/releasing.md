# Releasing

PingDrop is released by pushing a version tag. GitHub Actions does the rest.

## Continuous integration

`.github/workflows/ci.yml` runs on pushes to `main`/`master` and on pull requests:

- restore, Release build, `PingDrop.Tests`, and `dotnet pack` (the test results and the `.nupkg`/`.snupkg` are uploaded as artifacts);
- the JS client tests (`node --test test/*.test.js` in `clients/js`) on Node 20 and 22.

## Version conventions

Versions follow [SemVer](https://semver.org). Tags are the version prefixed with `v`:

| Tag | NuGet version | GitHub release |
| --- | --- | --- |
| `v1.0.0-preview.1` | `1.0.0-preview.1` | pre-release |
| `v1.0.0-rc.1` | `1.0.0-rc.1` | pre-release |
| `v1.0.0` | `1.0.0` | release |

The `<Version>` in `PingDrop/PingDrop.csproj` is only the local/default version. The release workflow passes `-p:Version=<tag without v>`, which overrides it, so you do not need to edit the csproj to release. Tags that are not valid SemVer fail the workflow.

## Required secret

Add a repository secret named `NUGET_API_KEY` (Settings, Secrets and variables, Actions). Create the key on nuget.org with push permission scoped to the `PingDrop` package. If the secret is missing, the workflow still builds, tests, packs and creates the GitHub release, and skips the NuGet push with a notice.

## Cutting a release

```
git checkout main && git pull
git tag v1.0.0-preview.1
git push origin v1.0.0-preview.1
```

`.github/workflows/release.yml` then builds, tests, packs with the tag's version, pushes the `.nupkg` and `.snupkg` to NuGet.org (`--skip-duplicate`, so re-runs are safe) and creates a GitHub release with the packages attached and generated notes. If a release for the tag already exists, delete it before re-running.

## Multi-targeting (not enabled yet)

The library targets `net8.0` only. To add `net9.0`: change `<TargetFramework>` to `<TargetFrameworks>net8.0;net9.0</TargetFrameworks>` in the library and test project csproj files, and add `9.0.x` to the `dotnet-version` list in both workflows (setup-dotnet accepts multiple lines).
