#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$generator = Join-Path $scriptRoot "Generate-ReleaseNotes.ps1"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("vrcinventory-release-notes-" + [System.Guid]::NewGuid().ToString("N"))
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$pushed = $false

function Invoke-TestGit {
    & git @args
    if ($LASTEXITCODE -ne 0) {
        throw "git $($args -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Write-TestFile {
    param([string] $Path, [string] $Content)

    $fullPath = Join-Path (Get-Location) $Path
    $directory = Split-Path -Parent $fullPath
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    [System.IO.File]::WriteAllText($fullPath, $Content, $utf8NoBom)
}

function Commit-TestChange {
    param([string] $Path, [string] $Content, [string] $Subject)

    Write-TestFile -Path $Path -Content $Content
    Invoke-TestGit add -- $Path
    Invoke-TestGit commit -q -m $Subject
}

function Assert-Contains {
    param([string] $Text, [string] $Expected)

    if (-not $Text.Contains($Expected)) {
        throw "Expected release notes to contain '$Expected'."
    }
}

try {
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    Push-Location $tempRoot
    $pushed = $true

    Invoke-TestGit init -q
    Invoke-TestGit config core.autocrlf false
    Invoke-TestGit config user.name WhyKnot
    Invoke-TestGit config user.email whyknot@example.invalid

    New-Item -ItemType Directory -Force -Path ".github/release-template" | Out-Null
    Write-TestFile -Path ".github/release-template/links.md" -Content "## Links`n`n- Download {zip-name}"
    Write-TestFile -Path ".github/release-template/install.md" -Content "## Install`n`nRun Setup.exe."
    Write-TestFile -Path ".github/release-template/uninstall.md" -Content ""
    Write-TestFile -Path ".github/release-template/what-you-need-to-do.md" -Content ""
    Write-TestFile -Path ".github/release-template/please-read.md" -Content ""

    Commit-TestChange -Path "README.md" -Content "base`n" -Subject "chore: initial"
    Invoke-TestGit tag -a v2026.6.15.0 -m "VRCInventoryManager v2026.6.15.0"
    Commit-TestChange -Path "app.txt" -Content "feature`n" -Subject "feat(ui): add folder grid"
    Commit-TestChange -Path "app.txt" -Content "fix`n" -Subject "fix(release): mark beta releases"
    Invoke-TestGit tag -a v2026.6.16.0-beta -m "VRCInventoryManager v2026.6.16.0-beta"

    $notes = (& $generator `
        -Tag v2026.6.16.0-beta `
        -Repo RealWhyKnot/VRCInventoryManager `
        -TemplateDir ".github/release-template" `
        -ZipName "VRCInventoryManager-v2026.6.16.0-beta-win-x64.zip" `
        -SetupName "VRCInventoryManager-v2026.6.16.0-beta-Setup.exe" `
        -IntegrityName "VRCInventoryManager-v2026.6.16.0-beta.integrity.tsv") -join "`n"

    Assert-Contains -Text $notes -Expected "# VRCInventoryManager v2026.6.16.0-beta"
    Assert-Contains -Text $notes -Expected "### Features"
    Assert-Contains -Text $notes -Expected "feat(ui): add folder grid by @RealWhyKnot"
    Assert-Contains -Text $notes -Expected "### Bug Fixes"
    Assert-Contains -Text $notes -Expected "compare/v2026.6.15.0...v2026.6.16.0-beta"
    Assert-Contains -Text $notes -Expected "VRCInventoryManager-v2026.6.16.0-beta.integrity.tsv"
    Assert-Contains -Text $notes -Expected "Download VRCInventoryManager-v2026.6.16.0-beta-win-x64.zip"

    Write-Host "Generate-ReleaseNotes tests passed."
}
finally {
    if ($pushed) {
        Pop-Location
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
