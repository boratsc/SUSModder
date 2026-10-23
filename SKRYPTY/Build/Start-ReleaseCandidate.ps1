#Requires -Version 5.1
<#
.SYNOPSIS
  Dispatch the release workflow without losing Unicode in native stdin pipes.
.DESCRIPTION
  Reads an optional UTF-8 changelog and passes a UTF-8 JSON file to gh.
  Compatible with Windows PowerShell 5.1 and PowerShell 7. DryRun never dispatches.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateSet('beta', 'release')]
    [string]$Channel,

    [ValidateSet('github-draft+server', 'github-draft', 'none')]
    [string]$PublishTarget = 'github-draft+server',

    [ValidateSet('signpath', 'none')]
    [string]$SigningMode = 'signpath',

    [string]$ChangelogPath = '',
    [string]$DiscordTeaser = '',
    [string]$Ref = 'beta',
    [string]$Repo = 'boratsc/SUSModder',
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$changelog = ''
if (-not [string]::IsNullOrWhiteSpace($ChangelogPath)) {
    $resolvedPath = (Resolve-Path -LiteralPath $ChangelogPath).ProviderPath
    $changelog = [System.IO.File]::ReadAllText($resolvedPath, $utf8)
}

$payload = @{
    ref = $Ref
    inputs = @{
        version = $Version
        channel = $Channel
        publishTarget = $PublishTarget
        signingMode = $SigningMode
        changelogMarkdown = $changelog
        discordTeaser = $DiscordTeaser
    }
} | ConvertTo-Json -Depth 5 -Compress

if ($DryRun) {
    return $payload
}

$payloadPath = [System.IO.Path]::GetTempFileName()
try {
    # Never pipe JSON into gh: Windows PowerShell 5.1 defaults to ASCII stdin.
    [System.IO.File]::WriteAllText($payloadPath, $payload, $utf8)
    & gh api "repos/$Repo/actions/workflows/release-candidate.yml/dispatches" --input $payloadPath
    if ($LASTEXITCODE -ne 0) {
        throw "Release workflow dispatch failed (gh exit code $LASTEXITCODE)."
    }
}
finally {
    Remove-Item -LiteralPath $payloadPath -Force
}
