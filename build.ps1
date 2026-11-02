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
    $parts = [System.Collections.Generic.List[int]]::new()
    foreach ($part in $base.Split('.')) {
        $value = 0
        if ([int]::TryParse($part, [ref]$value)) {
            $parts.Add($value)
        } else {
            $parts.Add(0)
        }
    }
    while ($parts.Count -lt 4) { $parts.Add(0) }
    return ($parts[0..3] -join '.')
}

function Write-IntegrityManifest {
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory = $true)][string] $ReleaseDir,
        [Parameter(Mandatory = $true)][string] $BuildVersion
    )

    $manifestPath = Join-Path $ReleaseDir "VRCInventoryManager-v$BuildVersion.integrity.tsv"
    $assets = @(Get-ChildItem -LiteralPath $ReleaseDir -File | Where-Object {
            $_.Name -like '*.zip' -or $_.Name -like '*Setup.exe'
        } | Sort-Object Name)

    if ($assets.Count -eq 0) {
        return
    }

    [IO.File]::WriteAllText($manifestPath, "name`tsha256`tbytes" + [Environment]::NewLine)
    foreach ($asset in $assets) {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $asset.FullName).Hash.ToLowerInvariant()
        [IO.File]::AppendAllText($manifestPath, "$($asset.Name)`t$hash`t$($asset.Length)" + [Environment]::NewLine)
    }
    Write-Host "Integrity:   $manifestPath"
}
Set-Location $PSScriptRoot







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

$numericVersion = Get-NumericVersion -BuildVersion $Version
Write-Utf8NoBom -Path (Join-Path -Path $PSScriptRoot -ChildPath 'version.txt') -Content $Version
Write-Host "Build version: $Version"

dotnet restore VRCInventoryManager.slnx
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

$buildArguments = @(
    'build', 'VRCInventoryManager.slnx', '--configuration', 'Release', '--no-restore',
    "-p:Version=$numericVersion", "-p:InformationalVersion=$Version"
)
dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

if (-not $Release) {
    dotnet test VRCInventoryManager.slnx --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'test harness failed' }
}

if ($Release) {
    $releaseDir = Join-Path $PSScriptRoot 'release'
    $appDir = Join-Path $releaseDir 'app'
    if (Test-Path -LiteralPath $releaseDir) {
        Remove-Item -LiteralPath $releaseDir -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Force -Path $appDir

    $publishArguments = @(
        'publish', 'src/VRCInventoryManager/VRCInventoryManager.csproj',
        '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true', '--output', $appDir,
        '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:PublishReadyToRun=false',
        '-p:DebugType=None', '-p:DebugSymbols=false', "-p:Version=$numericVersion", "-p:InformationalVersion=$Version"
    )
    dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

    $publishedFiles = @(Get-ChildItem -LiteralPath $appDir -File)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'VRCInventoryManager.exe') {
        throw 'Release publish should produce exactly one executable file.'
    }

    $licensePath = Join-Path $PSScriptRoot 'LICENSE'
    if (-not (Test-Path -LiteralPath $licensePath)) {
        throw 'LICENSE is required for release packaging.'
    }
    Copy-Item -LiteralPath $licensePath -Destination $appDir -Force

    $zipPath = Join-Path $releaseDir "VRCInventoryManager-v$Version-win-x64.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    $exeSizeMb = [Math]::Round($publishedFiles[0].Length / 1MB, 2)
    Write-Host "Release exe: $($publishedFiles[0].FullName) ($exeSizeMb MB)"
    Write-Host "Release zip: $zipPath"
    Write-Host "SHA256:      $zipHash"

    $makensis = Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath 'NSIS/makensis.exe'
    if (-not (Test-Path -LiteralPath $makensis)) {
        $makensis = Join-Path -Path $env:ProgramFiles -ChildPath 'NSIS/makensis.exe'
    }

    if (Test-Path -LiteralPath $makensis) {
        Push-Location install
        try {
            & $makensis "/DVERSION=$Version" "/DNUMERIC_VERSION=$numericVersion" installer.nsi
            if ($LASTEXITCODE -ne 0) { throw "makensis failed with exit $LASTEXITCODE" }
        } finally {
            Pop-Location
        }
    } elseif ($RequireInstaller) {
        throw 'makensis.exe was not found. Install NSIS or let CI install it.'
    } else {
        Write-Host 'NSIS not found; installer build skipped.'
    }

    Write-IntegrityManifest -ReleaseDir $releaseDir -BuildVersion $Version
}

exit 0
