#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest


function Assert-Condition {
    [CmdletBinding()]
    [OutputType([void])]
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        $script:failures++
        [Console]::Error.WriteLine($Message)
    }
}

$script:failures = 0
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('vrc-changelog-test-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Force -Path $root
try {
    $changelog = @'
# Changelog

## Unreleased

### Added
- Example entry (abcdef0)

---
'@
    $changelogPath = Join-Path -Path $root -ChildPath 'CHANGELOG.md'
    [IO.File]::WriteAllText($changelogPath, $changelog + [Environment]::NewLine)

    $scriptPath = Join-Path -Path $PSScriptRoot -ChildPath 'Update-Changelog.ps1'
    $instant = [datetime]::Parse('2026-06-16T01:30:00Z', [Globalization.CultureInfo]::InvariantCulture)
    & $scriptPath -Mode Promote -Version 'v2026.6.13.0' -RepoRoot $root -NowUtc $instant
    if ($LASTEXITCODE -ne 0) { throw 'Update-Changelog Promote failed' }
    $promoted = [IO.File]::ReadAllText($changelogPath)
    $assertion = @{
        Condition = ($promoted -match '## v2026\.6\.13\.0 - 2026-06-15')
        Message   = 'Promoted changelog date did not use Central release date.'
    }
    Assert-Condition @assertion
    $notes = & $scriptPath -Mode Notes -ForVersion -Version 'v2026.6.13.0' -RepoRoot $root
    if ($LASTEXITCODE -ne 0) { throw 'Update-Changelog Notes failed' }
    $assertion = @{
        Condition = (($notes -join "`n") -match 'Example entry')
        Message   = 'Promoted changelog notes were not readable.'
    }
    Assert-Condition @assertion
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:failures -gt 0) { exit 1 }
Write-Host 'Changelog scripts passed.'

exit 0
