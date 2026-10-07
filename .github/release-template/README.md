# Release template

`Generate-ReleaseNotes.ps1` builds the GitHub release body from these Markdown snippets and replaces these tokens in them:

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

The snippets have to be ASCII. Notes for one release go in `.github/release-extras/<tag>.md`.
