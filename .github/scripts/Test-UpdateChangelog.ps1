[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('vrc-changelog-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null
try {
    @'
# Changelog

## Unreleased

### Added
- Example entry (abcdef0)

---
'@ | Set-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Encoding utf8

    & (Join-Path $PSScriptRoot 'Update-Changelog.ps1') -Mode Promote -Version 'v2026.6.13.0' -RepoRoot $root
    $notes = & (Join-Path $PSScriptRoot 'Update-Changelog.ps1') -Mode Notes -ForVersion -Version 'v2026.6.13.0' -RepoRoot $root
    if (($notes -join "`n") -notmatch 'Example entry') {
        throw 'Promoted changelog notes were not readable.'
    }
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Changelog scripts passed.'
