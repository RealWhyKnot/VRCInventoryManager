#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [int] $ProcessId = 0,
    [string] $SetupPath = '__SETUP_PATH__',
    [string] $SetupArguments = '__SETUP_ARGUMENTS__',
    [string] $StagingDirectory = '__STAGING_DIRECTORY__',
    [string] $InstallDirectory = '__INSTALL_DIRECTORY__',
    [string] $ExecutablePath = '__EXECUTABLE_PATH__',
    [string] $LogPath = '__LOG_PATH__',
    [string] $FreshPath = '__FRESH_PATH__'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-UpdateFailure {
    [CmdletBinding()]
    [OutputType([void])]
    param([string] $Path, [string] $Step, [int] $Code)
    $message = "$(Get-Date -Format s) $Step ($Code)" + [Environment]::NewLine
    try {
        $log = [IO.FileInfo]::new($Path)
        if ($log.Exists -and $log.Length + $message.Length -gt 2MB) {
            [IO.File]::Delete("$Path.1")
            if ($log.Length -le 2MB) {
                [IO.File]::Move($Path, "$Path.1")
            } else {
                [IO.File]::Delete($Path)
            }
        }
        [IO.File]::AppendAllText($Path, $message)
    } catch [IO.IOException], [UnauthorizedAccessException] {
        [Console]::Error.WriteLine($message)
    }
}

$lease = [IO.File]::Open((Join-Path $StagingDirectory 'lease'), 'CreateNew', 'ReadWrite', 'None')
$swap = $null
$applied = $false
$appClosed = $false
$step = 'waiting for app exit'
try {
    [IO.File]::WriteAllText((Join-Path $StagingDirectory 'ready'), '')
    Wait-Process -Id $ProcessId -Timeout 300 -ErrorAction SilentlyContinue
    if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        throw [InvalidOperationException]::new('The app is still running.')
    }
    $appClosed = $true
    if ($FreshPath) {
        $step = 'replacing executable'
        $swap = $ExecutablePath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        [IO.File]::Copy($FreshPath, $swap)
        [IO.File]::Replace($swap, $ExecutablePath, "$ExecutablePath.old")
        $applied = $true
    } else {
        $step = 'starting setup'
        $start = @{
            FilePath     = $SetupPath
            ArgumentList = $SetupArguments
            Wait         = $true
            PassThru     = $true
            WindowStyle  = 'Hidden'
        }
        $setup = Start-Process @start
        $applied = $setup.ExitCode -eq 0
        if (-not $applied) {
            Write-UpdateFailure -Path $LogPath -Step 'setup exit code' -Code $setup.ExitCode
        }
    }
} catch [IO.IOException], [UnauthorizedAccessException], [InvalidOperationException], [ComponentModel.Win32Exception] {
    Write-UpdateFailure -Path $LogPath -Step $step -Code $_.Exception.HResult
} finally {
    try {
        if ($swap) { [IO.File]::Delete($swap) }
    } catch [IO.IOException], [UnauthorizedAccessException] {
        Write-UpdateFailure -Path $LogPath -Step 'removing temporary executable' -Code $_.Exception.HResult
    }
    $lease.Dispose()
    Remove-Item -LiteralPath $StagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
if ($appClosed) {
    try {
        Start-Process -FilePath $ExecutablePath -WorkingDirectory $InstallDirectory
    } catch [InvalidOperationException], [ComponentModel.Win32Exception] {
        Write-UpdateFailure -Path $LogPath -Step 'reopening app' -Code $_.Exception.HResult
        exit 1
    }
}
if ($applied) { exit 0 }
exit 1
