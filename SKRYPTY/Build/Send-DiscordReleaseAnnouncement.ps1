#Requires -Version 7.0
<#
.SYNOPSIS
  Wysyła ogłoszenie nowej wersji SUSModder na Discord (webhook).

.DESCRIPTION
  Szablony beta/release. Changelog jest opcjonalny (pusty = post bez bloku zmian).
  Beta/release: Portable.zip + zbudowany Setup.exe z draftu GH + stały bootstrapper.
  Bootstrapper: https://susmodder.app/releases/SUSModderInstaller.exe (nie przebudowywany w RC).
  Release dodatkowo: susmodder.app + GitHub releases/latest.

.EXAMPLE
  .\Send-DiscordReleaseAnnouncement.ps1 -Channel beta -Version 3.0.12 -ReleaseTag v3.0.12-beta -DryRun

.EXAMPLE
  $env:DC_WEBHOOK = 'https://discord.com/api/webhooks/...'
  .\Send-DiscordReleaseAnnouncement.ps1 -Channel release -Version 3.0.13 -ReleaseTag v3.0.13-release `
    -ChangelogMarkdown "* [3.0.13] Fix X"
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('beta', 'release')]
    [string]$Channel,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$ReleaseTag = '',

    [string]$ChangelogMarkdown = $(if ($env:CHANGELOG_MARKDOWN) { $env:CHANGELOG_MARKDOWN } else { '' }),

    [string]$Teaser = $(if ($env:DISCORD_TEASER) { $env:DISCORD_TEASER } else { '' }),

    [string]$WebhookUrl = $(if ($env:DC_WEBHOOK) { $env:DC_WEBHOOK } else { '' }),

    [string]$Repo = 'boratsc/SUSModder',

    [string]$SuppiUrl = 'https://suppi.pl/susmodder',

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DisplayVersion {
    param([string]$ChannelName, [string]$RawVersion)
    $v = $RawVersion.Trim()
    if ($ChannelName -eq 'beta') {
        if ($v -match '(?i)-beta') { return $v }
        return "$v-beta"
    }
    # release: zdejmij przypadkowy sufiks -beta z inputu
    return ($v -replace '(?i)-beta$', '')
}

function Resolve-ChannelDownloadUrls {
    param(
        [string]$Tag,
        [string]$Repository
    )

    if ([string]::IsNullOrWhiteSpace($Tag)) {
        throw "ReleaseTag is required to resolve GitHub asset download URLs."
    }

    $json = & gh release view $Tag --repo $Repository --json assets 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh release view failed for tag '$Tag': $json"
    }

    $parsed = $json | ConvertFrom-Json
    $assets = @($parsed.assets)
    if ($assets.Count -eq 0) {
        throw "Release '$Tag' has no assets (expected Portable.zip + Setup.exe)."
    }

    $portable = $assets | Where-Object { $_.name -match '(?i)Portable\.zip$' } | Select-Object -First 1
    $setup = $assets | Where-Object {
        $_.name -match '(?i)Setup\.exe$' -and $_.name -notmatch '(?i)Installer'
    } | Select-Object -First 1

    if (-not $portable -or -not $setup) {
        $names = ($assets | ForEach-Object { $_.name }) -join ', '
        throw "Could not find Portable.zip / Setup.exe on release '$Tag'. Assets: $names"
    }

    return [pscustomobject]@{
        PortableUrl = [string]$portable.url
        SetupUrl    = [string]$setup.url
    }
}

function Build-AnnouncementContent {
    param(
        [string]$ChannelName,
        [string]$DisplayVersion,
        [string]$Changelog,
        [string]$TeaserText,
        [string]$PortableUrl,
        [string]$SetupUrl,
        [string]$InstallerUrl,
        [string]$SupportUrl,
        [string]$Repository
    )

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("## SUSModder $DisplayVersion")
    [void]$sb.AppendLine()

    $changelogTrimmed = if ($null -eq $Changelog) { '' } else { $Changelog.Trim() }
    $hasChangelog = -not [string]::IsNullOrWhiteSpace($changelogTrimmed)

    if ($ChannelName -eq 'beta') {
        if ($hasChangelog) {
            [void]$sb.AppendLine('### Wydana została nowa wersja beta, zmiany w tej wersji:')
            [void]$sb.AppendLine()
            [void]$sb.AppendLine($changelogTrimmed)
            [void]$sb.AppendLine()
        } else {
            [void]$sb.AppendLine('### Wydana została nowa wersja beta')
            [void]$sb.AppendLine()
        }

        [void]$sb.AppendLine('Wersję beta można testować poprzez wybranie w ustawieniach testowej wersji')
        [void]$sb.AppendLine('lub ściągając paczkę:')
        [void]$sb.AppendLine($PortableUrl)
        [void]$sb.AppendLine('lub instalator (ta wersja beta):')
        [void]$sb.AppendLine($SetupUrl)
        [void]$sb.AppendLine('lub uniwersalny instalator (zawsze najnowszy kanał z ustawień):')
        [void]$sb.AppendLine($InstallerUrl)
    } else {
        if ($hasChangelog) {
            [void]$sb.AppendLine('### Wydana została nowa wersja, zmiany w tej wersji:')
            [void]$sb.AppendLine()
            [void]$sb.AppendLine($changelogTrimmed)
            [void]$sb.AppendLine()
        } else {
            [void]$sb.AppendLine('### Wydana została nowa wersja')
            [void]$sb.AppendLine()
        }

        [void]$sb.AppendLine('Najnowsza wersja zawsze na:')
        [void]$sb.AppendLine('https://susmodder.app')
        [void]$sb.AppendLine('oraz')
        [void]$sb.AppendLine("https://github.com/$Repository/releases/latest")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine('Paczka portable:')
        [void]$sb.AppendLine($PortableUrl)
        [void]$sb.AppendLine('Instalator (ta wersja):')
        [void]$sb.AppendLine($SetupUrl)
        [void]$sb.AppendLine('Uniwersalny instalator:')
        [void]$sb.AppendLine($InstallerUrl)
    }

    [void]$sb.AppendLine()
    [void]$sb.AppendLine('Zapraszam również do wspierania projektu poprzez suppi')
    [void]$sb.AppendLine($SupportUrl)
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('@everyone')

    $teaserTrimmed = if ($null -eq $TeaserText) { '' } else { $TeaserText.Trim() }
    if (-not [string]::IsNullOrWhiteSpace($teaserTrimmed)) {
        [void]$sb.AppendLine()
        [void]$sb.AppendLine($teaserTrimmed)
    }

    return $sb.ToString().TrimEnd() + "`n"
}

# --- main ---

$displayVersion = Get-DisplayVersion -ChannelName $Channel -RawVersion $Version

if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    $ReleaseTag = "v$($Version.Trim())-$Channel"
}

$installerUrl = 'https://susmodder.app/releases/SUSModderInstaller.exe'

$urls = Resolve-ChannelDownloadUrls -Tag $ReleaseTag -Repository $Repo
$portableUrl = $urls.PortableUrl
$setupUrl = $urls.SetupUrl

$content = Build-AnnouncementContent `
    -ChannelName $Channel `
    -DisplayVersion $displayVersion `
    -Changelog $ChangelogMarkdown `
    -TeaserText $Teaser `
    -PortableUrl $portableUrl `
    -SetupUrl $setupUrl `
    -InstallerUrl $installerUrl `
    -SupportUrl $SuppiUrl `
    -Repository $Repo

Write-Host "=== Discord announcement ($Channel / $displayVersion) ===" -ForegroundColor Cyan
Write-Host $content
Write-Host "=== end ===" -ForegroundColor Cyan

if ($content.Length -gt 1900) {
    Write-Host "WARNING: message length $($content.Length) approaches Discord 2000-char limit." -ForegroundColor Yellow
}

if ($DryRun) {
    Write-Host "DryRun: skipping webhook POST." -ForegroundColor Yellow
    exit 0
}

if ([string]::IsNullOrWhiteSpace($WebhookUrl)) {
    throw "DC_WEBHOOK is empty. Set secrets.DC_WEBHOOK or pass -WebhookUrl."
}

$payload = @{
    content           = $content
    allowed_mentions  = @{
        parse = @('everyone')
    }
} | ConvertTo-Json -Depth 5 -Compress

# Discord expects UTF-8 JSON body
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($payload)

try {
    $response = Invoke-WebRequest -Uri $WebhookUrl -Method Post -ContentType 'application/json; charset=utf-8' -Body $bodyBytes -UseBasicParsing
    Write-Host "Discord webhook OK (HTTP $($response.StatusCode))" -ForegroundColor Green
} catch {
    $status = $null
    if ($_.Exception.Response) {
        $status = [int]$_.Exception.Response.StatusCode
    }
    throw "Discord webhook failed$(if ($status) { " (HTTP $status)" }): $($_.Exception.Message)"
}
