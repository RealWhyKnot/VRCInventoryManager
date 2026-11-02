#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string] $Tag = $(if ($env:TAG_NAME) { $env:TAG_NAME } else { $env:GITHUB_REF_NAME }),
    [string] $Repo = $env:GITHUB_REPOSITORY,
    [string] $TemplateDir,
    [string] $Extras,
    [string] $ZipName,
    [string] $SetupName,
    [string] $IntegrityName,
    [string] $ReleaseTitle = 'VRCInventoryManager',
    [switch] $AllowEmpty,
    [switch] $SkipScrub
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Git {
    [CmdletBinding()]
    [OutputType([object[]])]
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git @Arguments 2>$null
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($LASTEXITCODE -ne 0) {
        return $null
    }
    return @($output)
}

function Get-FirstOutput {
    [CmdletBinding()]
    [OutputType([string])]
    param([object] $Value)

    $items = @($Value)
    if ($items.Count -eq 0) {
        return $null
    }
    return [string]$items[0]
}

function Test-IsPrereleaseTag {
    [CmdletBinding()]
    [OutputType([bool])]
    param([string] $Value)
    return $Value -match '^v?\d{4}\.\d+\.\d+\.\d+-.+'
}

function Resolve-PreviousTag {
    [CmdletBinding()]
    [OutputType([string])]
    param([string] $Value)

    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.AddRange([string[]]@('describe', '--tags', '--abbrev=0', '--match', 'v*'))
    if (-not (Test-IsPrereleaseTag -Value $Value)) {
        $arguments.AddRange([string[]]@('--exclude', '*-*'))
    }
    $arguments.Add("$Value^")
    $previous = Invoke-Git -Arguments $arguments
    $first = Get-FirstOutput -Value $previous
    if (-not [string]::IsNullOrWhiteSpace($first)) {
        return $first.Trim()
    }

    return $null
}

function Get-LogArgument {
    [CmdletBinding()]
    [OutputType([object[]])]
    param([string] $Value, [string] $PreviousTag)

    if ([string]::IsNullOrWhiteSpace($PreviousTag)) {
        return @($Value)
    }

    return @("$PreviousTag..$Value")
}

function Get-Category {
    [CmdletBinding()]
    [OutputType([hashtable])]
    param([string] $Subject)

    if ($Subject -match '^feat(\(.+?\))?!?:') { return @{ Order = 1; Name = 'Features' } }
    if ($Subject -match '^fix(\(.+?\))?!?:') { return @{ Order = 2; Name = 'Bug Fixes' } }
    if ($Subject -match '^perf(\(.+?\))?!?:') { return @{ Order = 3; Name = 'Performance' } }
    if ($Subject -match '^refactor(\(.+?\))?!?:') { return @{ Order = 4; Name = 'Refactors' } }
    if ($Subject -match '^revert(\(.+?\))?!?:') { return @{ Order = 5; Name = 'Reverts' } }
    if ($Subject -match '^docs(\(.+?\))?!?:') { return @{ Order = 6; Name = 'Documentation' } }
    if ($Subject -match '^style(\(.+?\))?!?:') { return @{ Order = 7; Name = 'Style' } }
    if ($Subject -match '^test(\(.+?\))?!?:') { return @{ Order = 8; Name = 'Tests' } }
    if ($Subject -match '^ci(\(.+?\))?!?:') { return @{ Order = 9; Name = 'CI' } }
    if ($Subject -match '^build(\(.+?\))?!?:') { return @{ Order = 10; Name = 'Build' } }
    if ($Subject -match '^chore(\(.+?\))?!?:') { return @{ Order = 11; Name = 'Chores' } }
    return @{ Order = 99; Name = 'Other Changes' }
}

function Expand-Token {
    [CmdletBinding()]
    [OutputType([string])]
    param([string] $Text, [hashtable] $Tokens)

    if ($null -eq $Text) {
        return $Text
    }

    foreach ($key in $Tokens.Keys) {
        $value = $Tokens[$key]
        if ($null -eq $value) {
            $value = ''
        }
        $Text = $Text.Replace($key, [string]$value)
    }

    return $Text
}

function Read-TemplateSection {
    [CmdletBinding()]
    [OutputType([string])]
    param([string] $Name, [string] $Directory, [hashtable] $Tokens)

    $path = Join-Path -Path $Directory -ChildPath "$Name.md"
    $path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($path)
    if (-not (Test-Path -LiteralPath $path)) {
        return $null
    }

    $rawContent = [IO.File]::ReadAllText($path)
    if ($null -eq $rawContent) {
        return $null
    }

    $content = $rawContent.Trim()
    if ([string]::IsNullOrWhiteSpace($content)) {
        return $null
    }

    return Expand-Token -Text $content -Tokens $Tokens
}

function ConvertTo-Ascii {
    [CmdletBinding()]
    [OutputType([string])]
    param([string] $Text)

    $subs = @(
        @{ Pattern = [string][char]0x2014; Replacement = '--' },
        @{ Pattern = [string][char]0x2013; Replacement = '-' },
        @{ Pattern = [string][char]0x2026; Replacement = '...' },
        @{ Pattern = [string][char]0x201C; Replacement = '"' },
        @{ Pattern = [string][char]0x201D; Replacement = '"' },
        @{ Pattern = [string][char]0x2018; Replacement = "'" },
        @{ Pattern = [string][char]0x2019; Replacement = "'" },
        @{ Pattern = [string][char]0x00A0; Replacement = ' ' },
        @{ Pattern = [string][char]0x00D7; Replacement = 'x' },
        @{ Pattern = [string][char]0x2192; Replacement = '->' }
    )

    foreach ($sub in $subs) {
        $Text = $Text.Replace($sub.Pattern, $sub.Replacement)
    }

    return $Text
}

function Assert-Ascii {
    [CmdletBinding()]
    [OutputType([void])]
    param([string] $Text)

    $lineNumber = 0
    foreach ($line in ($Text -split "`r?`n")) {
        $lineNumber++
        for ($i = 0; $i -lt $line.Length; $i++) {
            $code = [int][char]$line[$i]
            $allowed = ($code -ge 0x20 -and $code -le 0x7E) -or $code -eq 9
            if (-not $allowed) {
                $hexCode = '{0:X4}' -f $code
                throw "Non-ASCII character U+$hexCode in release body at line $lineNumber, column $($i + 1)."
            }
        }
    }
}

function Format-AuthorCredit {
    [CmdletBinding()]
    [OutputType([string])]
    param([string] $Author)

    if (-not $Author) { return '' }
    if ($Author -match '\[bot\]$') { return $Author }
    if ($Author -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$') { return $Author }
    return "[$Author](https://github.com/$Author)"
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
    throw 'No tag provided. Pass -Tag or set TAG_NAME / GITHUB_REF_NAME.'
}

if ([string]::IsNullOrWhiteSpace($TemplateDir)) {
    $TemplateDir = Join-Path (Get-Location) '.github/release-template'
}

if ([string]::IsNullOrWhiteSpace($Extras)) {
    $Extras = Join-Path (Get-Location) ".github/release-extras/$Tag.md"
}





















$previousTag = Resolve-PreviousTag -Value $Tag
$logArguments = @(Get-LogArgument -Value $Tag -PreviousTag $previousTag)
$rawLog = & git log @logArguments --no-merges --pretty=format:"%H`t%h`t%an`t%s"
if ($LASTEXITCODE -ne 0) {
    $rawLog = @()
}

$authorHandleMap = @{
    'WhyKnot' = 'RealWhyKnot'
}



$entries = New-Object System.Collections.Generic.List[object]
foreach ($line in @($rawLog)) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }
    if ($line -match '\[skip changelog\]') {
        continue
    }

    $parts = $line -split "`t", 4
    if ($parts.Count -lt 4) {
        continue
    }

    $author = $parts[2]
    if ($authorHandleMap.ContainsKey($author)) {
        $author = $authorHandleMap[$author]
    }

    $subject = $parts[3] -replace '\s*\(\d{4}\.\d+\.\d+\.\d+(-[A-Za-z0-9]+)?\)\s*', ' '
    $subject = ($subject.Trim() -replace '\s{2,}', ' ')
    if ([string]::IsNullOrWhiteSpace($subject)) {
        continue
    }

    $category = Get-Category -Subject $subject
    $null = $entries.Add([pscustomobject]@{
            Order    = $category.Order
            Category = $category.Name
            Short    = $parts[1]
            Author   = $author
            Subject  = $subject
        })
}

if ($entries.Count -eq 0 -and -not $AllowEmpty) {
    $rangeText = if ($previousTag) { "$previousTag..$Tag" } else { $Tag }
    throw "No release-note commits found in $rangeText. Use -AllowEmpty only for the first release."
}

$owner = ''
$repoShort = ''
if (-not [string]::IsNullOrWhiteSpace($Repo) -and $Repo.Contains('/')) {
    $split = $Repo -split '/', 2
    $owner = $split[0]
    $repoShort = $split[1]
} elseif (-not [string]::IsNullOrWhiteSpace($Repo)) {
    $repoShort = $Repo
}

$commitSha = ''
$commitShort = ''
$tagSha = Get-FirstOutput -Value (Invoke-Git -Arguments @('rev-parse', "$Tag^{commit}"))
if (-not [string]::IsNullOrWhiteSpace($tagSha)) {
    $commitSha = $tagSha.Trim()
    if ($commitSha.Length -ge 12) {
        $commitShort = $commitSha.Substring(0, 12)
    }
}

$tokens = @{
    '{tag}'              = $Tag
    '{version}'          = ($Tag -replace '^v', '')
    '{owner}'            = $owner
    '{repo}'             = $repoShort
    '{full-repo}'        = $Repo
    '{commit-sha}'       = $commitSha
    '{commit-sha-short}' = $commitShort
    '{prior-tag}'        = if ($previousTag) { $previousTag } else { '' }
    '{zip-name}'         = $ZipName
    '{setup-name}'       = $SetupName
    '{integrity-name}'   = $IntegrityName
}

$builder = New-Object System.Text.StringBuilder
$null = $builder.AppendLine("# $ReleaseTitle $Tag")
$null = $builder.AppendLine()
$null = $builder.AppendLine("## What's Changed")
$null = $builder.AppendLine()

if ($entries.Count -eq 0) {
    $null = $builder.AppendLine('_First release; see commit history for details._')
    $null = $builder.AppendLine()
} else {
    $groups = $entries | Group-Object Category | Sort-Object { ($_.Group | Select-Object -First 1).Order }
    foreach ($group in $groups) {
        $null = $builder.AppendLine("### $($group.Name)")
        foreach ($entry in $group.Group) {
            $credit = Format-AuthorCredit -Author $entry.Author
            $null = $builder.AppendLine("- $($entry.Subject) by $credit in $($entry.Short)")
        }
        $null = $builder.AppendLine()
    }
}

if (-not [string]::IsNullOrWhiteSpace($Repo) -and -not [string]::IsNullOrWhiteSpace($previousTag)) {
    $null = $builder.AppendLine("**Full Changelog**: https://github.com/$Repo/compare/$previousTag...$Tag")
    $null = $builder.AppendLine()
}

if (-not [string]::IsNullOrWhiteSpace($IntegrityName)) {
    $null = $builder.AppendLine('## File integrity')
    $null = $builder.AppendLine()
    $null = $builder.AppendLine("Release SHA256 hashes are attached as ``$IntegrityName``.")
    $null = $builder.AppendLine()
}

foreach ($templateName in @('links', 'install', 'uninstall', 'what-you-need-to-do', 'please-read')) {
    $section = Read-TemplateSection -Name $templateName -Directory $TemplateDir -Tokens $tokens
    if (-not [string]::IsNullOrWhiteSpace($section)) {
        $null = $builder.AppendLine($section)
        $null = $builder.AppendLine()
    }
}

$Extras = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Extras)
if (Test-Path -LiteralPath $Extras) {
    $extraContent = ([IO.File]::ReadAllText($Extras)).Trim()
    if (-not [string]::IsNullOrWhiteSpace($extraContent)) {
        $null = $builder.AppendLine('---')
        $null = $builder.AppendLine()
        $null = $builder.AppendLine('## Additional notes')
        $null = $builder.AppendLine()
        $null = $builder.AppendLine($extraContent)
    }
}

$body = ConvertTo-Ascii -Text $builder.ToString().TrimEnd()
if (-not $SkipScrub) {
    Assert-Ascii -Text $body
}

$body

exit 0
