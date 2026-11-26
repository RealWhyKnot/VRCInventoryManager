#!/usr/bin/env pwsh
#Requires -Version 7.4
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Set-Location -LiteralPath (Join-Path -Path $PSScriptRoot -ChildPath '../..')
npm.cmd run build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
npm.cmd run typecheck
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
npm.cmd run lint
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
cargo fmt --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
cargo clippy --workspace --all-targets --locked -- -D warnings
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
cargo test --workspace --all-targets --locked
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$rustFiles = @(git ls-files -- '*.rs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$awk = Get-Command -Name awk -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $awk) {
    $gitFolder = Split-Path -Path (Split-Path -Path (Get-Command -Name git -CommandType Application).Source)
    $awk = Get-Command -Name (Join-Path -Path $gitFolder -ChildPath 'usr/bin/awk.exe')
}
& $awk -f .github/lint/rust-style.awk @rustFiles
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$wideLines = @(Select-String -LiteralPath $rustFiles -Pattern '^.{101}' -Encoding utf8)
foreach ($wideLine in $wideLines) {
    Write-Host "$($wideLine.RelativePath($PWD.Path)):$($wideLine.LineNumber): wrap this line to 100 characters."
}
if ($wideLines.Count -gt 0) { exit 1 }
python -B .github/lint/style_check.py
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path -Path $PSScriptRoot -ChildPath 'Invoke-PSLint.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$testNames = @('Test-UpdateChangelog.ps1', 'Test-GenerateReleaseNotes.ps1', 'Test-NightlyBetaPlan.ps1')
foreach ($testName in $testNames) {
    $testPath = Join-Path -Path $PSScriptRoot -ChildPath "../scripts/$testName"
    pwsh -NoProfile -File $testPath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($env:OS -eq 'Windows_NT') {
        powershell -NoProfile -File $testPath
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
Write-Host 'lint: passed.'
exit 0
