#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-TestGit {
    [CmdletBinding()]
    [OutputType([object[]])]
    param([string[]] $Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Write-TestFile {
    [CmdletBinding()]
    [OutputType([void])]
    param([string] $Path, [string] $Content)

    $fullPath = Join-Path (Get-Location) $Path
    $directory = Split-Path -Parent $fullPath
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        $null = New-Item -ItemType Directory -Force -Path $directory
    }
    [System.IO.File]::WriteAllText($fullPath, $Content, $utf8NoBom)
}

function Save-TestChange {
    [CmdletBinding()]
    [OutputType([void])]
    param([string] $Path, [string] $Content, [string] $Subject)

    Write-TestFile -Path $Path -Content $Content
    Invoke-TestGit -Arguments @('add', '--', $Path)
    Invoke-TestGit -Arguments @('commit', '-q', '-m', $Subject)
}

function Assert-Content {
    [CmdletBinding()]
    [OutputType([void])]
    param([string] $Text, [string] $Expected)

    if (-not $Text.Contains($Expected)) {
        $script:failures++
        [Console]::Error.WriteLine("Expected release notes to contain '$Expected'.")
    }
}

$script:failures = 0
$scriptRoot = $PSScriptRoot
$generator = Join-Path $scriptRoot 'Generate-ReleaseNotes.ps1'
$tempName = 'vrcinventory-release-notes-' + [guid]::NewGuid().ToString('N')
$tempRoot = Join-Path -Path ([IO.Path]::GetTempPath()) -ChildPath $tempName
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$pushed = $false

try {
    $null = New-Item -ItemType Directory -Force -Path $tempRoot
    Push-Location $tempRoot
    $pushed = $true

    Invoke-TestGit -Arguments @('init', '-q')
    Invoke-TestGit -Arguments @('config', 'core.autocrlf', 'false')
    Invoke-TestGit -Arguments @('config', 'user.name', 'WhyKnot')
    Invoke-TestGit -Arguments @('config', 'user.email', 'whyknot@example.invalid')

    $null = New-Item -ItemType Directory -Force -Path '.github/release-template'
    $links = "## Links`n`n- Download {zip-name}`n- Commit {commit-sha-short}"
    Write-TestFile -Path '.github/release-template/links.md' -Content $links
    Write-TestFile -Path '.github/release-template/install.md' -Content "## Install`n`nRun Setup.exe."
    Write-TestFile -Path '.github/release-template/uninstall.md' -Content ''
    Write-TestFile -Path '.github/release-template/what-you-need-to-do.md' -Content ''
    Write-TestFile -Path '.github/release-template/please-read.md' -Content ''

    Save-TestChange -Path 'README.md' -Content "base`n" -Subject 'chore: initial'
    Invoke-TestGit -Arguments @('tag', '-a', 'v2026.6.15.0', '-m', 'VRCInventoryManager v2026.6.15.0')
    Save-TestChange -Path 'app.txt' -Content "feature`n" -Subject 'feat(ui): add folder grid'
    Save-TestChange -Path 'app.txt' -Content "fix`n" -Subject 'fix(release): mark beta releases'
    $tagArguments = @('tag', '-a', 'v2026.6.16.0-beta', '-m', 'VRCInventoryManager v2026.6.16.0-beta')
    Invoke-TestGit -Arguments $tagArguments
    $tagObjectShort = (& git rev-parse --short=12 v2026.6.16.0-beta).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'git rev-parse v2026.6.16.0-beta failed' }
    $tagCommitShort = (& git rev-parse --short=12 'v2026.6.16.0-beta^{commit}').Trim()
    if ($LASTEXITCODE -ne 0) { throw 'git rev-parse v2026.6.16.0-beta^{commit} failed' }

    $generatorArguments = @{
        Tag           = 'v2026.6.16.0-beta'
        Repo          = 'RealWhyKnot/VRCInventoryManager'
        TemplateDir   = '.github/release-template'
        ZipName       = 'VRCInventoryManager-v2026.6.16.0-beta-win-x64.zip'
        SetupName     = 'VRCInventoryManager-v2026.6.16.0-beta-Setup.exe'
        IntegrityName = 'VRCInventoryManager-v2026.6.16.0-beta.integrity.tsv'
    }
    $notes = (& $generator @generatorArguments) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Generate-ReleaseNotes failed' }

    Assert-Content -Text $notes -Expected '# VRCInventoryManager v2026.6.16.0-beta'
    Assert-Content -Text $notes -Expected '### Features'
    Assert-Content -Text $notes -Expected 'feat(ui): add folder grid by [RealWhyKnot](https://github.com/RealWhyKnot)'
    Assert-Content -Text $notes -Expected '### Bug Fixes'
    Assert-Content -Text $notes -Expected 'compare/v2026.6.15.0...v2026.6.16.0-beta'
    Assert-Content -Text $notes -Expected 'VRCInventoryManager-v2026.6.16.0-beta.integrity.tsv'
    Assert-Content -Text $notes -Expected 'Download VRCInventoryManager-v2026.6.16.0-beta-win-x64.zip'
    Assert-Content -Text $notes -Expected "Commit $tagCommitShort"
    if ($tagObjectShort -ne $tagCommitShort -and $notes.Contains("Commit $tagObjectShort")) {
        throw 'Release notes should use the tagged commit, not the annotated tag object.'
    }

    if ($script:failures -eq 0) { Write-Host 'Generate-ReleaseNotes tests passed.' }
} finally {
    if ($pushed) {
        Pop-Location
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

if ($script:failures -gt 0) { exit 1 }
exit 0
