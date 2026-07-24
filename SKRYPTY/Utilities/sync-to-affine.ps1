#Requires -Version 7.0
<#
.SYNOPSIS
  Migruje pliki DOC/SKRYPTY do AFFiNE przez natywny MCP workspace.

.DESCRIPTION
  Tworzy dokumenty przez create_document (title + markdown body).
  Zapisuje mapę relativePath -> docId w .affine-migration/map.json (gitignored).
  Ponowne uruchomienie pomija pliki już w mapie (idempotentne).

.PARAMETER Files
  Ścieżki względem root repo.

.PARAMETER All
  Migruje cały DOC/ + SKRYPTY/ (oprócz sync-to-affine.ps1).

.PARAMETER DryRun
  Tylko pokaż title i rozmiar, bez uploadu.

.PARAMETER SkipReadback
  Nie weryfikuj read_document po create (szybsze przy batchu).

.PARAMETER DelayMs
  Pauza między create (domyślnie 300).

.PARAMETER MapPath
  Plik mapy (domyślnie .affine-migration/map.json).

.EXAMPLE
  .\SKRYPTY\Utilities\sync-to-affine.ps1 -All -SkipReadback
#>
[CmdletBinding()]
param(
    [string[]] $Files = @(),
    [switch] $All,
    [switch] $DryRun,
    [switch] $SkipReadback,
    [int] $DelayMs = 300,
    [string] $MapPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($MapPath)) {
    $MapPath = Join-Path $RepoRoot '.affine-migration\map.json'
}

$CodeExtensions = @('.ps1', '.py', '.sh', '.cs', '.json', '.js', '.ts', '.yml', '.yaml')

function Get-AffineMcpConfig {
    $mcpFile = Join-Path $env:USERPROFILE '.cursor\mcp.json'
    if (-not (Test-Path $mcpFile)) { throw "Brak $mcpFile" }
    $cfg = Get-Content $mcpFile -Raw | ConvertFrom-Json
    $affine = $cfg.mcpServers.affine
    if (-not $affine) { throw 'Brak wpisu mcpServers.affine w mcp.json' }
    $token = $affine.headers.Authorization -replace '^Bearer\s+', ''
    return [PSCustomObject]@{
        Url   = [string]$affine.url
        Token = $token
    }
}

function ConvertTo-AffineTitle {
    param([string] $RelativePath)
    $normalized = $RelativePath -replace '\\', '/'
    $parts = $normalized -split '/'
    $name = [System.IO.Path]::GetFileNameWithoutExtension($parts[-1])
    if ($parts.Count -eq 1) { return $name }
    $prefix = ($parts[0..($parts.Count - 2)] -join ' / ')
    return "$prefix / $name"
}

function ConvertTo-AffineContent {
    param(
        [string] $FullPath,
        [string] $RelativePath
    )
    $raw = Get-Content -LiteralPath $FullPath -Raw -Encoding UTF8
    if ($null -eq $raw) { $raw = '' }
    $ext = [System.IO.Path]::GetExtension($FullPath).ToLowerInvariant()

    if ($ext -in $CodeExtensions) {
        $lang = switch ($ext) {
            '.ps1' { 'powershell' }
            '.py' { 'python' }
            '.sh' { 'bash' }
            '.cs' { 'csharp' }
            '.json' { 'json' }
            '.js' { 'javascript' }
            '.ts' { 'typescript' }
            { $_ -in '.yml', '.yaml' } { 'yaml' }
            default { '' }
        }
        return @"
> Snapshot z repo. Uruchamiaj / czytaj z gita: ``$RelativePath``

``````$lang
$($raw.TrimEnd())
``````
"@
    }

    return $raw.TrimEnd()
}

function Invoke-AffineMcp {
    param(
        [string] $Url,
        [string] $Token,
        [string] $ToolName,
        [hashtable] $Arguments,
        [int] $Id = 1,
        [int] $MaxRetries = 8
    )
    $headers = @{
        Authorization  = "Bearer $Token"
        'Content-Type' = 'application/json'
        Accept         = 'application/json, text/event-stream'
    }
    $body = @{
        jsonrpc = '2.0'
        id      = $Id
        method  = 'tools/call'
        params  = @{
            name      = $ToolName
            arguments = $Arguments
        }
    } | ConvertTo-Json -Depth 10 -Compress

    $attempt = 0
    while ($true) {
        $attempt++
        try {
            $response = Invoke-WebRequest -Uri $Url -Headers $headers -Method POST -Body $body -UseBasicParsing -TimeoutSec 180
            $parsed = $response.Content | ConvertFrom-Json
            if ($null -ne $parsed.PSObject.Properties['error']) {
                $msg = [string]$parsed.error.message
                if ($msg -match 'Too many requests|TOO_MANY|429' -and $attempt -lt $MaxRetries) {
                    $wait = [Math]::Min(60, 3 * [Math]::Pow(2, $attempt - 1))
                    Write-Host "  rate-limit, czekam ${wait}s (attempt $attempt/$MaxRetries)" -ForegroundColor DarkYellow
                    Start-Sleep -Seconds $wait
                    continue
                }
                throw "MCP $ToolName failed: $msg"
            }
            return $parsed.result
        }
        catch {
            $errText = "$_"
            if ($errText -match '429|Too Many Requests|TOO_MANY' -and $attempt -lt $MaxRetries) {
                $wait = [Math]::Min(60, 3 * [Math]::Pow(2, $attempt - 1))
                Write-Host "  rate-limit HTTP, czekam ${wait}s (attempt $attempt/$MaxRetries)" -ForegroundColor DarkYellow
                Start-Sleep -Seconds $wait
                continue
            }
            throw
        }
    }
}

function New-AffineDocument {
    param(
        [string] $Url,
        [string] $Token,
        [string] $Title,
        [string] $Content
    )
    $result = Invoke-AffineMcp -Url $Url -Token $Token -ToolName 'create_document' -Arguments @{
        title   = $Title
        content = $Content
    }
    $text = ($result.content | ForEach-Object { $_.text }) -join ''
    $payload = $text | ConvertFrom-Json
    if (-not $payload.success) { throw "create_document failed: $text" }
    return [string]$payload.docId
}

function Read-AffineDocument {
    param(
        [string] $Url,
        [string] $Token,
        [string] $DocId
    )
    $result = Invoke-AffineMcp -Url $Url -Token $Token -ToolName 'read_document' -Arguments @{
        docId = $DocId
    } -Id 2
    return (($result.content | ForEach-Object { $_.text }) -join '').TrimEnd()
}

function Get-AllMigrationFiles {
    param([string] $Root)
    $excludeNames = @('sync-to-affine.ps1')
    $allowed = @('.md', '.ps1', '.py', '.sh', '.cs', '.json', '.js', '.ts', '.yml', '.yaml', '.txt')
    $paths = @()
    foreach ($dir in @('DOC', 'SKRYPTY')) {
        $base = Join-Path $Root $dir
        if (-not (Test-Path $base)) { continue }
        Get-ChildItem -LiteralPath $base -Recurse -File | ForEach-Object {
            if ($_.Name -in $excludeNames) { return }
            if ($_.Extension.ToLowerInvariant() -notin $allowed) { return }
            $rel = $_.FullName.Substring($Root.Length + 1) -replace '\\', '/'
            $paths += $rel
        }
    }
    return $paths | Sort-Object
}

function Import-MigrationMap {
    param([string] $Path)
    $ht = @{}
    if (-not (Test-Path $Path)) { return $ht }
    $raw = Get-Content $Path -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($raw)) { return $ht }
    $parsed = $raw | ConvertFrom-Json
    if ($parsed -is [System.Collections.IEnumerable] -and -not ($parsed -is [string])) {
        foreach ($item in @($parsed)) {
            if ($null -eq $item.path -or $null -eq $item.docId) { continue }
            if ($item.path -eq '__INDEX__') { continue }
            $ht[[string]$item.path] = [string]$item.docId
        }
        return $ht
    }
    # legacy hashtable-like object
    $parsed.PSObject.Properties | ForEach-Object {
        if ($_.Name -ne '__INDEX__') {
            $ht[$_.Name] = [string]$_.Value
        }
    }
    return $ht
}

function Export-MigrationMap {
    param(
        [hashtable] $Map,
        [string] $Path,
        [string] $IndexDocId = ''
    )
    $entries = @()
    if (-not [string]::IsNullOrWhiteSpace($IndexDocId)) {
        $entries += [PSCustomObject]@{ path = '__INDEX__'; docId = $IndexDocId; title = 'SUSModder / INDEX' }
    }
    $entries += ($Map.GetEnumerator() | Sort-Object Name | ForEach-Object {
            [PSCustomObject]@{ path = $_.Key; docId = $_.Value }
        })
    $entries | ConvertTo-Json -Depth 3 | Set-Content -Path $Path -Encoding UTF8
}

# --- main ---

if ($All) {
    $Files = Get-AllMigrationFiles -Root $RepoRoot
}
elseif ($Files.Count -eq 0) {
    throw 'Podaj -All albo -Files @(...)'
}

$mcp = Get-AffineMcpConfig
$mapDir = Split-Path $MapPath -Parent
if (-not (Test-Path $mapDir)) {
    New-Item -ItemType Directory -Path $mapDir -Force | Out-Null
}

$map = Import-MigrationMap -Path $MapPath
$indexDocId = ''
if (Test-Path $MapPath) {
    $rawMap = Get-Content $MapPath -Raw | ConvertFrom-Json
    $idx = @($rawMap) | Where-Object { $_.path -eq '__INDEX__' } | Select-Object -First 1
    if ($idx) { $indexDocId = [string]$idx.docId }
}

Write-Host "AFFiNE MCP: $($mcp.Url)" -ForegroundColor Cyan
Write-Host "Pliki w kolejce: $($Files.Count) | Już w mapie: $($map.Count)" -ForegroundColor Cyan

$created = 0
$skipped = 0
$failed = 0
$results = [System.Collections.Generic.List[object]]::new()

foreach ($rel in $Files) {
    $relNorm = $rel -replace '\\', '/'
    $full = Join-Path $RepoRoot ($relNorm -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $full)) {
        Write-Warning "Pominięto (brak pliku): $relNorm"
        continue
    }

    $title = ConvertTo-AffineTitle -RelativePath $relNorm
    $size = (Get-Item -LiteralPath $full).Length

    if ($DryRun) {
        Write-Host "[dry] $relNorm -> $title ($size B)"
        continue
    }

    if ($map.ContainsKey($relNorm)) {
        $skipped++
        $results.Add([PSCustomObject]@{ Path = $relNorm; DocId = $map[$relNorm]; Status = 'skipped' })
        continue
    }

    Write-Host "[$($created + $skipped + $failed + 1)/$($Files.Count)] $relNorm" -ForegroundColor Yellow
    try {
        $content = ConvertTo-AffineContent -FullPath $full -RelativePath $relNorm
        $docId = New-AffineDocument -Url $mcp.Url -Token $mcp.Token -Title $title -Content $content
        $map[$relNorm] = $docId
        $created++
        Write-Host "  OK $docId" -ForegroundColor Green

        if (-not $SkipReadback) {
            Start-Sleep -Milliseconds 200
            $readBack = Read-AffineDocument -Url $mcp.Url -Token $mcp.Token -DocId $docId
            $sourceNorm = ($content -replace "`r`n", "`n").TrimEnd()
            $readNorm = ($readBack -replace "`r`n", "`n").TrimEnd()
            if ($readNorm.Length -lt [Math]::Floor($sourceNorm.Length * 0.85)) {
                Write-Warning "  Readback krótki (src=$($sourceNorm.Length), read=$($readNorm.Length))"
            }
        }

        $results.Add([PSCustomObject]@{ Path = $relNorm; DocId = $docId; Status = 'created' })
    }
    catch {
        $failed++
        Write-Host "  FAIL: $_" -ForegroundColor Red
        $results.Add([PSCustomObject]@{ Path = $relNorm; DocId = ''; Status = "failed: $_" })
        # zapisuj mapę po każdym failu też — nie gubimy postępu
        Export-MigrationMap -Map $map -Path $MapPath -IndexDocId $indexDocId
    }

    if ($DelayMs -gt 0) { Start-Sleep -Milliseconds $DelayMs }

    # checkpoint co 20 plików
    if (($created % 20) -eq 0 -and $created -gt 0) {
        Export-MigrationMap -Map $map -Path $MapPath -IndexDocId $indexDocId
        Write-Host "  checkpoint mapa ($($map.Count) wpisów)" -ForegroundColor DarkGray
    }
}

if (-not $DryRun) {
    Export-MigrationMap -Map $map -Path $MapPath -IndexDocId $indexDocId
    Write-Host "`nMapa: $MapPath ($($map.Count) docs)" -ForegroundColor Cyan
}

Write-Host "`ncreated=$created skipped=$skipped failed=$failed" -ForegroundColor Cyan
if ($failed -gt 0) { exit 1 }
