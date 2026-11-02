#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-TestGit {
    [CmdletBinding()]
    [OutputType([object[]])]
    param([string] $RepoRoot, [string[]] $Arguments)

    Push-Location $RepoRoot
    try {
        $output = & git @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
        }
        return @($output)
    } finally {
        Pop-Location
    }
}

function New-TestRepo {
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $tempName = 'vrcinventory-nightly-beta-' + [guid]::NewGuid().ToString('N')
    $root = Join-Path -Path ([IO.Path]::GetTempPath()) -ChildPath $tempName
    $null = New-Item -ItemType Directory -Path $root
    $null = Invoke-TestGit -RepoRoot $root -Arguments @('init', '-q', '.')
    $null = Invoke-TestGit -RepoRoot $root -Arguments @('config', 'user.name', 'VRCInventoryManager Tests')
    $null = Invoke-TestGit -RepoRoot $root -Arguments @('config', 'user.email', 'vrcinventory-tests@example.invalid')
    [IO.File]::WriteAllText((Join-Path -Path $root -ChildPath 'sample.txt'), 'base' + [Environment]::NewLine)
    $null = Invoke-TestGit -RepoRoot $root -Arguments @('add', '.')
    $null = Invoke-TestGit -RepoRoot $root -Arguments @('commit', '-q', '-m', 'initial')
    return $root
}

function Read-Output {
    [CmdletBinding()]
    [OutputType([hashtable])]
    param([string] $Path)

    $values = @{}
    foreach ($line in [IO.File]::ReadLines($Path)) {
        if ($line -match '^([^=]+)=(.*)$') {
            $values[$Matches[1]] = $Matches[2]
        }
    }
    return $values
}

function Invoke-Planner {
    [CmdletBinding()]
    [OutputType([hashtable])]
    param([string] $RepoRoot)

    $outputPath = Join-Path $RepoRoot 'outputs.txt'
    $instant = [datetime]::Parse('2026-06-16T12:00:00Z', [Globalization.CultureInfo]::InvariantCulture)
    & $planner -RepoRoot $RepoRoot -NowUtc $instant -OutputPath $outputPath | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Get-NightlyBetaPlan failed' }
    return Read-Output -Path $outputPath
}

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
$scriptRoot = $PSScriptRoot
$planner = Join-Path $scriptRoot 'Get-NightlyBetaPlan.ps1'

$tempRoots = New-Object System.Collections.Generic.List[string]
try {
    $repo = New-TestRepo
    $null = $tempRoots.Add($repo)
    $first = Invoke-Planner -RepoRoot $repo
    Assert-Condition -Condition ($first['has_changes'] -eq 'true') -Message 'Planner should publish when no tag exists.'
    $assertion = @{
        Condition = ($first['next_tag'] -eq 'v2026.6.16.0-beta')
        Message   = "Planner chose unexpected first beta tag: $($first['next_tag'])"
    }
    Assert-Condition @assertion

    $null = Invoke-TestGit -RepoRoot $repo -Arguments @('tag', 'v2026.6.16.0-beta')
    Remove-Item -LiteralPath (Join-Path $repo 'outputs.txt') -Force
    $sameHead = Invoke-Planner -RepoRoot $repo
    $assertion = @{
        Condition = ($sameHead['has_changes'] -eq 'false')
        Message   = 'Planner should skip when latest tag is at HEAD.'
    }
    Assert-Condition @assertion

    [IO.File]::WriteAllText((Join-Path -Path $repo -ChildPath 'sample.txt'), 'changed' + [Environment]::NewLine)
    $null = Invoke-TestGit -RepoRoot $repo -Arguments @('add', '.')
    $null = Invoke-TestGit -RepoRoot $repo -Arguments @('commit', '-q', '-m', 'feat: new nightly input')
    Remove-Item -LiteralPath (Join-Path $repo 'outputs.txt') -Force
    $changed = Invoke-Planner -RepoRoot $repo
    $assertion = @{
        Condition = ($changed['has_changes'] -eq 'true')
        Message   = 'Planner should publish after commits since latest tag.'
    }
    Assert-Condition @assertion
    $assertion = @{
        Condition = ($changed['next_tag'] -eq 'v2026.6.16.1-beta')
        Message   = "Planner chose unexpected second beta tag: $($changed['next_tag'])"
    }
    Assert-Condition @assertion

    if ($script:failures -eq 0) { Write-Host 'Nightly beta planner tests passed.' }
} finally {
    foreach ($tempRoot in $tempRoots) {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }
}

if ($script:failures -gt 0) { exit 1 }
exit 0
