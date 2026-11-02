#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [int] $ProcessId = 0,
    [string] $SetupPath = '__SETUP_PATH__',
    [string] $SetupArguments = '__SETUP_ARGUMENTS__',
    [string] $StagingDirectory = '__STAGING_DIRECTORY__',
    [string] $InstallDirectory = '__INSTALL_DIRECTORY__',
    [string] $ExecutablePath = '__EXECUTABLE_PATH__',
    [string] $LogPath = '__LOG_PATH__'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-UpdateFailure {
    [CmdletBinding()]
    [OutputType([void])]
    param([string] $Path, [System.Management.Automation.ErrorRecord] $Failure, [switch] $Setup)
    if ($Setup) {
        [IO.File]::AppendAllText($Path, "$(Get-Date -Format s) update failed" + [Environment]::NewLine)
    }
    [IO.File]::AppendAllText($Path, ($Failure | Out-String) + [Environment]::NewLine)
}

$applied = $false
try {
    Wait-Process -Id $ProcessId -Timeout 300 -ErrorAction SilentlyContinue
    $start = @{
        FilePath     = $SetupPath
        ArgumentList = $SetupArguments
        Wait         = $true
        PassThru     = $true
    }
    $setup = Start-Process @start
    if ($setup.ExitCode -ne 0) {
        throw [InvalidOperationException]::new("setup exited with code $($setup.ExitCode)")
    }
    $applied = $true
} catch [System.ComponentModel.Win32Exception], [InvalidOperationException] {
    Write-UpdateFailure -Path $LogPath -Failure $_ -Setup
} finally {
    try {
        Start-Process -FilePath $ExecutablePath -WorkingDirectory $InstallDirectory
    } catch [System.ComponentModel.Win32Exception], [InvalidOperationException] {
        Write-UpdateFailure -Path $LogPath -Failure $_
    } finally {
        try {
            Remove-Item -LiteralPath $StagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
        } finally {
            if ($applied) { exit 0 }
        }
    }
}
exit 1
