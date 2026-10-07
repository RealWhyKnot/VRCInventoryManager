# Release workflow

Releases are built from pushed tags by `.github/workflows/release.yml`.

## Stable release

1. Check that `main` is green.
2. Push a tag shaped like `vYYYY.M.D.N`, where `N` is the next revision for that day.
3. `.github/workflows/release.yml` builds the compressed single-file win-x64 app, the NSIS installer and the integrity TSV.
4. It publishes the GitHub release and moves the `Unreleased` part of `CHANGELOG.md` under the new tag on `main`.

## Prerelease

A tag with a suffix, like `vYYYY.M.D.N-beta`, runs the same workflow. The GitHub release is marked as a prerelease and `CHANGELOG.md` isn't changed.

## Nightly beta

`.github/workflows/nightly-beta.yml` runs on a schedule and on demand. When there are commits after the latest reachable release tag, it creates the next `-beta` tag, and pushing that tag starts the release workflow.

## Nightly validation

`.github/workflows/nightly-tidy.yml` runs the full lint, the tests and the release packaging every night. It uploads the zip and installer as workflow artifacts and doesn't publish a GitHub release.
