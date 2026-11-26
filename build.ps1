#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string] $Version = '',
    [switch] $Release,
    [switch] $RequireInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Utf8NoBom {
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Content
    )
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-NumericVersion {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory = $true)][string] $BuildVersion)
    $base = ($BuildVersion -replace '-.*$', '')
    foreach ($part in $base.Split('.')) {
        $value = [uint16]0
        if (-not [uint16]::TryParse($part, [ref]$value)) {
            throw "Version component $part is out of range."
        }
    }
    return $base
}

function Write-IntegrityManifest {
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory)][string] $ManifestPath,
        [Parameter(Mandatory)][string[]] $AssetPaths
    )

    $rows = [System.Collections.Generic.List[string]]::new()
    $rows.Add("name`tsha256`tbytes")
    foreach ($path in $AssetPaths) {
        $asset = Get-Item -LiteralPath $path
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
        $rows.Add("$($asset.Name)`t$hash`t$($asset.Length)")
        Write-Utf8NoBom -Path "$path.sha256" -Content "$hash  $($asset.Name)`n"
    }
    Write-Utf8NoBom -Path $ManifestPath -Content (($rows -join "`n") + "`n")
    Write-Host "Integrity manifest $ManifestPath"
}

Set-Location -LiteralPath $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $today = Get-Date -Format 'yyyy.M.d'
    $stateDir = Join-Path -Path $PSScriptRoot -ChildPath 'build'
    $statePath = Join-Path $stateDir 'local_build_state.json'
    $counter = 0
    if (Test-Path -LiteralPath $statePath) {
        $state = [IO.File]::ReadAllText($statePath) | ConvertFrom-Json
        if ($state.PSObject.Properties['date'] -and $state.PSObject.Properties['counter'] -and $state.date -eq $today) {
            $counter = [int]$state.counter + 1
        }
    }
    $null = New-Item -ItemType Directory -Force -Path $stateDir
    $uid = ([guid]::NewGuid().ToString('N').Substring(0, 4)).ToUpper()
    $Version = "$today.$counter-$uid"
    Write-Utf8NoBom -Path $statePath -Content (@{ date = $today; counter = $counter } | ConvertTo-Json -Depth 2)
}

if ($Version -notmatch '^\d{4}\.\d+\.\d+\.\d+(-[A-Za-z0-9][A-Za-z0-9.-]*)?$') {
    throw 'Use a version such as 2026.11.26.0 or 2026.11.26.0-beta.'
}

$numericVersion = Get-NumericVersion -BuildVersion $Version
Write-Utf8NoBom -Path (Join-Path $PSScriptRoot 'version.txt') -Content $Version
Write-Host "Building VRCInventoryManager $Version"

npm.cmd ci --no-audit --no-fund
if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
npm.cmd run build
if ($LASTEXITCODE -ne 0) { throw 'The frontend build failed.' }

$target = 'x86_64-pc-windows-msvc'
cargo build --release --locked --target $target
if ($LASTEXITCODE -ne 0) { throw 'The Rust release build failed.' }

if (-not $Release) {
    cargo test --workspace --all-targets --locked
    if ($LASTEXITCODE -ne 0) { throw 'The Rust tests failed.' }
    exit 0
}

$metadata = cargo metadata --no-deps --locked --format-version 1
if ($LASTEXITCODE -ne 0) { throw 'Could not locate the Cargo build output.' }
$targetDirectory = ($metadata | ConvertFrom-Json).target_directory
$binary = Join-Path $targetDirectory "$target/release/VRCInventoryManager.exe"
$licensePath = Join-Path $PSScriptRoot 'LICENSE'
$releaseDir = Join-Path $PSScriptRoot 'release'
$appDir = Join-Path $releaseDir 'app'
$null = New-Item -ItemType Directory -Force -Path $appDir
$publishedExe = Join-Path $appDir 'VRCInventoryManager.exe'
$publishedLicense = Join-Path $appDir 'LICENSE'
Copy-Item -LiteralPath $binary -Destination $publishedExe -Force
Copy-Item -LiteralPath $licensePath -Destination $publishedLicense -Force

$base = Join-Path $releaseDir "VRCInventoryManager-v$Version"
$zipPath = "$base-win-x64.zip"
$setupPath = "$base-Setup.exe"
$manifestPath = "$base.integrity.tsv"
$oldPaths = @($zipPath, $setupPath, $manifestPath, "$zipPath.sha256", "$setupPath.sha256")
foreach ($path in $oldPaths) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

Compress-Archive -LiteralPath $publishedExe, $publishedLicense -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Portable archive $zipPath"

$makensis = Get-Command makensis.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$makensisPath = if ($makensis) { $makensis.Source } else {
    foreach ($folder in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if (-not $folder) { continue }
        $candidate = Join-Path $folder 'NSIS/makensis.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $candidate
            break
        }
    }
}

$assets = [System.Collections.Generic.List[string]]::new()
$assets.Add($zipPath)
if ($makensisPath) {
    Push-Location -LiteralPath (Join-Path $PSScriptRoot 'install')
    try {
        & $makensisPath "/DVERSION=$Version" "/DNUMERIC_VERSION=$numericVersion" installer.nsi
        if ($LASTEXITCODE -ne 0) { throw "NSIS failed with exit code $LASTEXITCODE." }
        if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) { throw 'NSIS did not produce the installer.' }
        $assets.Add($setupPath)
    } finally {
        Pop-Location
    }
} elseif ($RequireInstaller) {
    throw 'Install NSIS to build the installer.'
} else {
    Write-Host 'Install NSIS to include an installer.'
}

Write-IntegrityManifest -ManifestPath $manifestPath -AssetPaths $assets
exit 0
