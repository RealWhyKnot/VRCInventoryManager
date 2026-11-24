# Contributing to VRCInventoryManager

I built VRCInventoryManager for my own use first and I maintain it alone. I'm picky about how the code is structured, how it's styled and how features fit together. If you hit a bug or want a change, please open an [issue](https://github.com/RealWhyKnot/VRCInventoryManager/issues/new) instead of sending a surprise pull request. A description of the problem, the steps to reproduce it or what you're trying to do is more useful to me than an unannounced PR.

If you'd like to help maintain VRCInventoryManager over time, especially if I'm committing to it less often, or you have a substantial change you want to build, open an issue or reach out first. I'm happy to talk through the approach and agree on code style and commit conventions before you write any code.

## If we agree on a PR

Link the issue where we settled the approach in the PR description.

### Setting up

You need the .NET SDK version in `global.json` on 64-bit Windows, plus PowerShell 7 and Git. Turn on the repository's hooks after cloning:

```powershell
git config core.hooksPath .githooks
.\build.ps1
```

### Checks

Run `./.github/lint/lint.ps1`. It is the same gate CI runs, and it includes `dotnet test VRCInventoryManager.slnx`.

### Commits

Commit subjects look like `type(scope): summary`, where `type` is one of `feat`, `fix`, `chore`, `ci`, `docs`, `refactor`, `revert`, `test`, `perf`, `diag` or `style`. The commit-msg hook checks the format. Releases are described in [.github/RELEASE_WORKFLOW.md](.github/RELEASE_WORKFLOW.md).
