#!/usr/bin/env pwsh
#Requires -Version 7.4
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$binary = Join-Path $root 'release/app/VRCInventoryManager.exe'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$start = @{
    FilePath     = $binary
    ArgumentList = @('--serve-only', '--fixture', 'empty', '--port', $port)
    WindowStyle  = 'Hidden'
    PassThru     = $true
}
$process = Start-Process @start
$base = "http://127.0.0.1:$port"
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $state = $null
    do {
        if ($process.HasExited) { throw "The release executable exited with code $($process.ExitCode)." }
        try {
            $state = Invoke-RestMethod -Uri "$base/api/state" -TimeoutSec 2
        } catch [System.Net.Http.HttpRequestException], [System.Threading.Tasks.TaskCanceledException] {
            Start-Sleep -Milliseconds 250
        }
    } until ($state -or [DateTime]::UtcNow -ge $deadline)
    if (-not $state) { throw 'The release executable did not start its local server.' }

    $expectedVersion = [IO.File]::ReadAllText((Join-Path $root 'version.txt')).Trim()
    if ($state.version -ne $expectedVersion) {
        throw "The executable reported version $($state.version), expected $expectedVersion."
    }
    foreach ($asset in @('index.html', 'app.css', 'app.js')) {
        $response = Invoke-WebRequest -Uri "$base/$asset" -TimeoutSec 5
        $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($response.RawContentStream.ToArray()))
        $expected = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $root "web/dist/$asset")).Hash
        if ($actual -ne $expected) { throw "The executable embeds the wrong $asset." }
    }
    Write-Host "The release executable served the expected frontend and version $expectedVersion."
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    $process.Dispose()
}
exit 0
