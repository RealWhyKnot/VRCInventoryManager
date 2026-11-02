#!/usr/bin/env pwsh
#Requires -Version 7.4
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Set-Location -LiteralPath (Join-Path -Path $PSScriptRoot -ChildPath '../..')
dotnet restore VRCInventoryManager.slnx --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet format VRCInventoryManager.slnx --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build VRCInventoryManager.slnx -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test VRCInventoryManager.slnx -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run .github/lint/CSharpStyle.cs -- .
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
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
