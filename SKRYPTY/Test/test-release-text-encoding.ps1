#Requires -Version 5.1
# Offline regression checks: no GitHub dispatch and no Discord request.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dispatchScript = Join-Path $root 'SKRYPTY/Build/Start-ReleaseCandidate.ps1'
$announceScript = Join-Path $root 'SKRYPTY/Build/Send-DiscordReleaseAnnouncement.ps1'
$utf8 = [System.Text.UTF8Encoding]::new($false, $true)

function Assert-Equal($Expected, $Actual, [string]$Label) {
    if ($Expected -cne $Actual) { throw "FAIL: $Label" }
}

# ASCII-only source so PowerShell 5.1 can read this test without a BOM.
$polish = ConvertFrom-Json '"Za\u017c\u00f3\u0142\u0107 g\u0119\u015bl\u0105 ja\u017a\u0144; \u0141\u00d3\u0044\u0179 \u0104\u0106\u0118\u0143\u015a\u0179"'
$changelog = "- $polish`n- English, quotes: `"test`", slash: \ and emoji: $([char]::ConvertFromUtf32(0x1F680))`n"
$teaser = "Next: $polish"
$testState = @{
    CapturedPayload = $null
    CapturedPayloadPath = $null
    DispatchCalls = 0
    FailDispatch = $false
    PostCalls = 0
    PostedPayload = $null
    ReleaseDraft = $false
}

function gh {
    $arguments = @($args)
    $global:LASTEXITCODE = 0
    if ($arguments[0] -eq 'api') {
        Assert-Equal 'repos/boratsc/SUSModder/actions/workflows/release-candidate.yml/dispatches' $arguments[1] 'dispatch endpoint'
        Assert-Equal '--input' $arguments[2] 'file input flag'
        if ($arguments[3] -eq '-') { throw 'JSON must not use native stdin.' }
        $testState.CapturedPayloadPath = $arguments[3]
        $bytes = [System.IO.File]::ReadAllBytes($testState.CapturedPayloadPath)
        if ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            throw 'JSON must be UTF-8 without BOM.'
        }
        $testState.CapturedPayload = $utf8.GetString($bytes) | ConvertFrom-Json
        $testState.DispatchCalls++
        if ($testState.FailDispatch) { $global:LASTEXITCODE = 1 }
        return
    }
    if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'view') {
        $tag = [string]$arguments[2]
        $isDraft = [bool]$testState.ReleaseDraft
        $releaseUrl = 'https://github.com/boratsc/SUSModder/releases/tag/' + $tag
        $downloadTag = $tag
        if ($isDraft) {
            $releaseUrl = 'https://github.com/boratsc/SUSModder/releases/tag/untagged-test'
            $downloadTag = 'untagged-test'
        }
        $payload = [ordered]@{
            isDraft = $isDraft
            url     = $releaseUrl
            assets  = @(
                [ordered]@{ name = 'SUSModder-Portable.zip'; url = "https://github.com/boratsc/SUSModder/releases/download/$downloadTag/SUSModder-Portable.zip" }
                [ordered]@{ name = 'SUSModder-Setup.exe'; url = "https://github.com/boratsc/SUSModder/releases/download/$downloadTag/SUSModder-Setup.exe" }
            )
        }
        return ($payload | ConvertTo-Json -Depth 5 -Compress)
    }
    throw 'Unexpected gh command in offline test.'
}

function Invoke-WebRequest {
    param($Uri, $Method, $ContentType, $Body, [switch]$UseBasicParsing)
    Assert-Equal 'https://example.invalid/webhook' $Uri 'mock webhook URL'
    Assert-Equal 'Post' $Method 'webhook method'
    Assert-Equal 'application/json; charset=utf-8' $ContentType 'webhook content type'
    if ($Body -isnot [byte[]]) { throw 'Discord body must be explicit UTF-8 bytes.' }
    $testState.PostedPayload = $utf8.GetString($Body) | ConvertFrom-Json
    $testState.PostCalls++
    return @{ StatusCode = 204 }
}

$changelogPath = [System.IO.Path]::GetTempFileName()
$savedChangelog = $env:CHANGELOG_MARKDOWN
$savedTeaser = $env:DISCORD_TEASER
try {
    [System.IO.File]::WriteAllText($changelogPath, $changelog, $utf8)
    foreach ($channel in @('beta', 'release')) {
        & $dispatchScript -Version '3.0.14' -Channel $channel -ChangelogPath $changelogPath -DiscordTeaser $teaser -PublishTarget none
        Assert-Equal $changelog $testState.CapturedPayload.inputs.changelogMarkdown 'UTF-8 changelog preserved in dispatch JSON'
        Assert-Equal $teaser $testState.CapturedPayload.inputs.discordTeaser 'Unicode teaser preserved'
        Assert-Equal $channel $testState.CapturedPayload.inputs.channel 'channel preserved'
        Assert-Equal '3.0.14' $testState.CapturedPayload.inputs.version 'version preserved'
        Assert-Equal 'none' $testState.CapturedPayload.inputs.publishTarget 'publish target preserved'
        Assert-Equal 'signpath' $testState.CapturedPayload.inputs.signingMode 'signing default preserved'
        if (Test-Path -LiteralPath $testState.CapturedPayloadPath) { throw 'Dispatch JSON temp file leaked.' }

        # Match the workflow environment handoff to the actual announcement script.
        if ($PSVersionTable.PSVersion.Major -ge 7) {
            $env:CHANGELOG_MARKDOWN = $testState.CapturedPayload.inputs.changelogMarkdown
            $env:DISCORD_TEASER = $testState.CapturedPayload.inputs.discordTeaser
            & $announceScript -Channel $channel -Version '3.0.14' -WebhookUrl 'https://example.invalid/webhook'
            if (-not $testState.PostedPayload.content.Contains($changelog.Trim())) { throw 'Discord changelog lost Unicode.' }
            if (-not $testState.PostedPayload.content.Contains($teaser)) { throw 'Discord teaser lost Unicode.' }
            Assert-Equal 'everyone' $testState.PostedPayload.allowed_mentions.parse[0] 'existing mentions policy'
        }
    }
    Assert-Equal 2 $testState.DispatchCalls 'two mock dispatch calls'
    $dryRunJson = & $dispatchScript -Version '3.0.14' -Channel beta -ChangelogPath $changelogPath -DryRun
    Assert-Equal $changelog ($dryRunJson | ConvertFrom-Json).inputs.changelogMarkdown 'dry-run Unicode preserved'
    Assert-Equal 2 $testState.DispatchCalls 'dry-run did not dispatch'

    # Draft/untagged releases must be rejected before any Discord POST.
    if ($PSVersionTable.PSVersion.Major -ge 7) {
        $testState.ReleaseDraft = $true
        $draftRejected = $false
        try {
            & $announceScript -Channel beta -Version '3.0.14' -WebhookUrl 'https://example.invalid/webhook'
        }
        catch {
            if ($_.Exception.Message -notlike '*draft*') { throw }
            $draftRejected = $true
        }
        if (-not $draftRejected) { throw 'Draft release must be rejected before Discord announcement.' }
        Assert-Equal 2 $testState.PostCalls 'draft release must not post'
        $testState.ReleaseDraft = $false
        $global:LASTEXITCODE = 0
    }

    $emptyJson = & $dispatchScript -Version '3.0.14' -Channel release -DryRun
    Assert-Equal '' ($emptyJson | ConvertFrom-Json).inputs.changelogMarkdown 'optional changelog remains empty'

    $testState.FailDispatch = $true
    $failedAsExpected = $false
    try {
        & $dispatchScript -Version '3.0.14' -Channel beta -ChangelogPath $changelogPath
    }
    catch {
        if ($_.Exception.Message -notlike '*gh exit code 1*') { throw }
        $failedAsExpected = $true
    }
    if (-not $failedAsExpected) { throw 'Failed gh dispatch must throw.' }
    if (Test-Path -LiteralPath $testState.CapturedPayloadPath) { throw 'Failed dispatch leaked a temp file.' }
    # The failure above is simulated, not the test process exit status.
    $global:LASTEXITCODE = 0

    if ($PSVersionTable.PSVersion.Major -ge 7) {
        Assert-Equal 2 $testState.PostCalls 'both Discord channels checked'
    }
    Write-Host "PASS: release text encoding (PowerShell $($PSVersionTable.PSVersion)); no network calls."
}
finally {
    $env:CHANGELOG_MARKDOWN = $savedChangelog
    $env:DISCORD_TEASER = $savedTeaser
    Remove-Item -LiteralPath $changelogPath -Force
}
