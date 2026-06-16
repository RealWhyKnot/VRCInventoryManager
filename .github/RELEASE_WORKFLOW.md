# Release workflow

## Stable release

1. Make sure `main` is green.
2. Create and push a tag shaped like `vYYYY.M.D.N`, where `N` is the next same-day revision.
3. `.github/workflows/release.yml` builds the compressed single-file win-x64 app, the NSIS installer, and the integrity TSV.
4. The workflow publishes the GitHub release and promotes `CHANGELOG.md` from `Unreleased` to the tag section on `main`.

## Prerelease

Use a suffix tag such as `vYYYY.M.D.N-beta`. The same release workflow runs, but the GitHub release is marked as a prerelease and `CHANGELOG.md` promotion is skipped.

## Nightly beta

`.github/workflows/nightly-beta.yml` runs on schedule and on demand. It creates the next `-beta` tag only when there are commits after the latest reachable release tag. Pushing that tag triggers the normal release workflow.

## Nightly validation

`.github/workflows/nightly-tidy.yml` runs full lint, tests, and release packaging on schedule. It uploads the zip and installer as workflow artifacts without publishing a GitHub release.
