# Contributing to VRCInventoryManager

I built VRCInventoryManager for my own use first and I maintain it alone. I'm picky about how the code is structured, how it's styled and how features fit together. If you hit a bug or want a change, please open an [issue](https://github.com/RealWhyKnot/VRCInventoryManager/issues/new) instead of sending a surprise pull request. A description of the problem, the steps to reproduce it or what you're trying to do is more useful to me than an unannounced PR.

If you'd like to help maintain VRCInventoryManager over time, especially if I'm committing to it less often, or you have a substantial change you want to build, open an issue or reach out first. I'm happy to talk through the approach and agree on code style and commit conventions before you write any code.

## If we agree on a PR

Link the issue where we settled the approach in the PR description.

### Setting up

Set up Rust, Node.js and Pageframe using the [build instructions](README.md#build-from-source). Turn on the repository's hooks after cloning.

```powershell
git config core.hooksPath .githooks
.\build.ps1
```

### Checks

Run `npm ci` first, then `./.github/lint/lint.ps1` to build the frontend and run the Rust tests and frontend, Rust and PowerShell lints. The pre-push hook also checks shell style, Rust style, workflows and dependencies. Those checks need Git Bash, ShellCheck, shfmt, actionlint, zizmor, typos, Gitleaks and cargo-deny on PATH. The PowerShell linter needs PSScriptAnalyzer and the common style check uses Python.

### Commits

Commit subjects look like `type(scope): summary`, where `type` is one of `feat`, `fix`, `chore`, `ci`, `docs`, `refactor`, `revert`, `test`, `perf`, `diag` or `style`. The commit-msg hook checks the format. Releases are described in [.github/RELEASE_WORKFLOW.md](.github/RELEASE_WORKFLOW.md).
