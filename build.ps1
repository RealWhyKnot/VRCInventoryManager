param(
    [string] $Version = "",
    [switch] $Release,
    [switch] $RequireInstaller
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory=$true)][string] $Path,
        [Parameter(Mandatory=$true)][string] $Content
    )
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-NumericVersion {
    param([Parameter(Mandatory=$true)][string] $BuildVersion)
    $base = ($BuildVersion -replace '-.*$', '')
    $parts = @($base.Split('.') | ForEach-Object {
        $value = 0
        if ([int]::TryParse($_, [ref]$value)) { $value } else { 0 }
    })
    while ($parts.Count -lt 4) { $parts += 0 }
    return ($parts[0..3] -join '.')
}

$hooksPath = (& git config --get core.hooksPath 2>$null)
if ($hooksPath -ne ".githooks") {
    & git config core.hooksPath ".githooks"
    Write-Host "Activated .githooks/ via core.hooksPath"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $today = Get-Date -Format "yyyy.M.d"
    $stateDir = "build"
    $statePath = Join-Path $stateDir "local_build_state.json"
    $counter = 0
    if (Test-Path -LiteralPath $statePath) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($state.date -eq $today) {
            $counter = [int]$state.counter + 1
        }
    }
    New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
    $uid = ([guid]::NewGuid().ToString("N").Substring(0, 4)).ToUpper()
    $Version = "$today.$counter-$uid"
    Write-Utf8NoBom -Path $statePath -Content (@{ date = $today; counter = $counter } | ConvertTo-Json)
}

$numericVersion = Get-NumericVersion -BuildVersion $Version
Write-Utf8NoBom -Path "version.txt" -Content $Version
Write-Host "Build version: $Version"

dotnet restore VRCInventoryManager.slnx
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

dotnet build VRCInventoryManager.slnx --configuration Release --no-restore -p:Version=$numericVersion
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

dotnet run --project tests\VRCInventoryManager.Tests\VRCInventoryManager.Tests.csproj --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "test harness failed" }

if ($Release) {
    $releaseDir = Join-Path $PSScriptRoot "release"
    $appDir = Join-Path $releaseDir "app"
    if (Test-Path -LiteralPath $releaseDir) {
        Remove-Item -LiteralPath $releaseDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $appDir | Out-Null

    dotnet publish src\VRCInventoryManager\VRCInventoryManager.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $appDir `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        -p:Version=$numericVersion
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    $zipPath = Join-Path $releaseDir "VRCInventoryManager-v$Version-win-x64.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $appDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
    $zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    Write-Host "Release zip: $zipPath"
    Write-Host "SHA256:      $zipHash"

    $makensis = "C:\Program Files (x86)\NSIS\makensis.exe"
    if (-not (Test-Path -LiteralPath $makensis)) {
        $makensis = "C:\Program Files\NSIS\makensis.exe"
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
        throw "makensis.exe was not found. Install NSIS or let CI install it."
    } else {
        Write-Host "NSIS not found; installer build skipped."
    }
}
