# Release template

`Generate-ReleaseNotes.ps1` reads these markdown snippets when composing a GitHub release body.

Supported tokens:

- `{tag}`
- `{version}`
- `{owner}`
- `{repo}`
- `{full-repo}`
- `{commit-sha}`
- `{commit-sha-short}`
- `{prior-tag}`
- `{zip-name}`
- `{setup-name}`
- `{integrity-name}`

Keep snippets ASCII-only. Per-release notes belong in `.github/release-extras/<tag>.md`.
