#Requires -Version 7.0
<#
.SYNOPSIS
  Tworzy strony-indeksy per katalog w AFFiNE (opcja A: nawigacja bez prawdziwych folderów).

.DESCRIPTION
  Na podstawie .affine-migration/map.json buduje:
  - INDEX / DOC / PLAN, Core, Frontend, POC, ...
  - INDEX / DOC / _archive (+ pod-indeksy)
  - INDEX / SKRYPTY / Build, Test, Utilities
  Aktualizuje SUSModder / INDEX linkami do tych stron.

.PARAMETER DelayMs
  Pauza między create/update (domyślnie 1200).

.EXAMPLE
  .\SKRYPTY\Utilities\generate-affine-folder-indexes.ps1
#>
[CmdletBinding()]
param(
    [int] $DelayMs = 1200,
    [string] $MapPath = '',
    [string] $FolderMapPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($MapPath)) {
    $MapPath = Join-Path $RepoRoot '.affine-migration\map.json'
}
if ([string]::IsNullOrWhiteSpace($FolderMapPath)) {
    $FolderMapPath = Join-Path $RepoRoot '.affine-migration\folder-indexes.json'
}

function Get-AffineMcpConfig {
    $mcpFile = Join-Path $env:USERPROFILE '.cursor\mcp.json'
    $cfg = Get-Content $mcpFile -Raw | ConvertFrom-Json
    $affine = $cfg.mcpServers.affine
    if (-not $affine) { throw 'Brak mcpServers.affine' }
    return [PSCustomObject]@{
        Url   = [string]$affine.url
        Token = ($affine.headers.Authorization -replace '^Bearer\s+', '')
    }
}

function Invoke-AffineMcp {
    param(
        [string] $Url,
        [string] $Token,
        [string] $ToolName,
        [hashtable] $Arguments,
        [int] $MaxRetries = 8
    )
    $headers = @{
        Authorization  = "Bearer $Token"
        'Content-Type' = 'application/json'
        Accept         = 'application/json, text/event-stream'
    }
    $body = @{
        jsonrpc = '2.0'
        id      = 1
        method  = 'tools/call'
        params  = @{ name = $ToolName; arguments = $Arguments }
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
                    Write-Host "  rate-limit, ${wait}s ($attempt/$MaxRetries)" -ForegroundColor DarkYellow
                    Start-Sleep -Seconds $wait
                    continue
                }
                throw "MCP $ToolName failed: $msg"
            }
            if ($null -ne $parsed.result.PSObject.Properties['isError'] -and $parsed.result.isError) {
                $t = ($parsed.result.content | ForEach-Object { $_.text }) -join ''
                throw "MCP $ToolName isError: $t"
            }
            return $parsed.result
        }
        catch {
            $errText = "$_"
            if ($errText -match '429|Too Many Requests|TOO_MANY' -and $attempt -lt $MaxRetries) {
                $wait = [Math]::Min(60, 3 * [Math]::Pow(2, $attempt - 1))
                Write-Host "  rate-limit HTTP, ${wait}s ($attempt/$MaxRetries)" -ForegroundColor DarkYellow
                Start-Sleep -Seconds $wait
                continue
            }
            throw
        }
    }
}

function New-OrUpdateFolderIndex {
    param(
        [string] $Url,
        [string] $Token,
        [string] $FolderKey,
        [string] $Title,
        [string] $Body,
        [hashtable] $Existing
    )
    if ($Existing.ContainsKey($FolderKey)) {
        $docId = $Existing[$FolderKey]
        Write-Host "UPDATE [$FolderKey] $docId" -ForegroundColor Cyan
        $null = Invoke-AffineMcp -Url $Url -Token $Token -ToolName 'update_document' -Arguments @{
            docId   = $docId
            content = $Body
        }
        $null = Invoke-AffineMcp -Url $Url -Token $Token -ToolName 'update_document_meta' -Arguments @{
            docId = $docId
            title = $Title
        }
        return $docId
    }

    Write-Host "CREATE [$FolderKey] $Title" -ForegroundColor Yellow
    $result = Invoke-AffineMcp -Url $Url -Token $Token -ToolName 'create_document' -Arguments @{
        title   = $Title
        content = $Body
    }
    $text = ($result.content | ForEach-Object { $_.text }) -join ''
    $payload = $text | ConvertFrom-Json
    if (-not $payload.success) { throw "create failed: $text" }
    Write-Host "  OK $($payload.docId)" -ForegroundColor Green
    return [string]$payload.docId
}

function Build-DocTable {
    param([object[]] $Entries)
    $lines = @(
        '| path | docId |',
        '|------|-------|'
    )
    foreach ($e in ($Entries | Sort-Object path)) {
        $lines += "| ``$($e.path)`` | ``$($e.docId)`` |"
    }
    return ($lines -join "`n")
}

# --- load map ---
$rawMap = @(Get-Content $MapPath -Raw | ConvertFrom-Json)
$mainIndexId = ($rawMap | Where-Object { $_.path -eq '__INDEX__' } | Select-Object -First 1).docId
if (-not $mainIndexId) { throw 'Brak __INDEX__ w map.json' }

$docs = @($rawMap | Where-Object { $_.path -ne '__INDEX__' })
Write-Host "Docs: $($docs.Count) | Main INDEX: $mainIndexId" -ForegroundColor Cyan

$existing = @{}
if (Test-Path $FolderMapPath) {
    $fm = Get-Content $FolderMapPath -Raw | ConvertFrom-Json
    foreach ($item in @($fm)) {
        if ($item.key -and $item.docId) { $existing[[string]$item.key] = [string]$item.docId }
    }
}

$mcp = Get-AffineMcpConfig

# Primary folder groups (path prefix)
function Get-PrimaryFolderKey {
    param([string] $Path)
    $parts = $Path -split '/'
    if ($parts[0] -eq 'DOC') {
        if ($parts.Count -eq 2) { return 'DOC' } # root files
        return "DOC/$($parts[1])"
    }
    if ($parts[0] -eq 'SKRYPTY') {
        if ($parts.Count -eq 2) { return 'SKRYPTY' }
        return "SKRYPTY/$($parts[1])"
    }
    return $parts[0]
}

$primary = @{}
foreach ($d in $docs) {
    $key = Get-PrimaryFolderKey -Path $d.path
    if (-not $primary.ContainsKey($key)) { $primary[$key] = [System.Collections.Generic.List[object]]::new() }
    $primary[$key].Add($d)
}

# Archive subgroups
$archiveSubs = @{}
foreach ($d in $docs | Where-Object { $_.path -like 'DOC/_archive/*' }) {
    $parts = $d.path -split '/'
    $sub = if ($parts.Count -ge 3) { $parts[2] } else { '_root' }
    # single files at archive root stay only on archive index
    $isFile = $sub -match '\.(md|ps1|py|json|cs)$'
    if ($isFile) { $sub = '_root' }
    if (-not $archiveSubs.ContainsKey($sub)) { $archiveSubs[$sub] = [System.Collections.Generic.List[object]]::new() }
    $archiveSubs[$sub].Add($d)
}

$folderResults = [System.Collections.Generic.List[object]]::new()

# --- create archive sub-indexes first ---
$archiveSubLinks = [System.Collections.Generic.List[string]]::new()
foreach ($sub in ($archiveSubs.Keys | Sort-Object)) {
    if ($sub -eq '_root') { continue }
    $key = "DOC/_archive/$sub"
    $title = "INDEX / DOC / _archive / $sub"
    $entries = @($archiveSubs[$sub])
    $body = @"
> Indeks folderu ``DOC/_archive/$sub`` ($($entries.Count) docs). Powrót: wyszukaj ``SUSModder / INDEX`` lub ``INDEX / DOC / _archive``.

$(Build-DocTable -Entries $entries)
"@
    $docId = New-OrUpdateFolderIndex -Url $mcp.Url -Token $mcp.Token -FolderKey $key -Title $title -Body $body -Existing $existing
    $folderResults.Add([PSCustomObject]@{ key = $key; title = $title; docId = $docId; count = $entries.Count })
    $archiveSubLinks.Add("| ``DOC/_archive/$sub`` | ``$docId`` | $($entries.Count) |")
    if ($DelayMs -gt 0) { Start-Sleep -Milliseconds $DelayMs }
}

# Archive root files
$archiveRoot = @()
if ($archiveSubs.ContainsKey('_root')) { $archiveRoot = @($archiveSubs['_root']) }

# --- DOC/_archive parent index ---
$archBody = @"
> Indeks ``DOC/_archive`` ($($primary['DOC/_archive'].Count) docs). Podkatalogi poniżej; pliki luźne na końcu.

## Podkatalogi

| folder | docId indeksu | docs |
|--------|---------------|------|
$($archiveSubLinks -join "`n")

$(if ($archiveRoot.Count -gt 0) {
@"
## Pliki w root archive

$(Build-DocTable -Entries $archiveRoot)
"@
})
"@
$archId = New-OrUpdateFolderIndex -Url $mcp.Url -Token $mcp.Token -FolderKey 'DOC/_archive' -Title 'INDEX / DOC / _archive' -Body $archBody -Existing $existing
$folderResults.Add([PSCustomObject]@{ key = 'DOC/_archive'; title = 'INDEX / DOC / _archive'; docId = $archId; count = $primary['DOC/_archive'].Count })
if ($DelayMs -gt 0) { Start-Sleep -Milliseconds $DelayMs }

# --- other primary folders (skip DOC/_archive — already done) ---
$primaryOrder = @(
    'DOC', 'DOC/PLAN', 'DOC/Core', 'DOC/Frontend', 'DOC/POC', 'DOC/Updater', 'DOC/readme',
    'SKRYPTY', 'SKRYPTY/Build', 'SKRYPTY/Test', 'SKRYPTY/Utilities'
)

foreach ($key in $primaryOrder) {
    if (-not $primary.ContainsKey($key)) { continue }
    $entries = @($primary[$key])
    $title = "INDEX / $($key -replace '/', ' / ')"
    $body = @"
> Indeks folderu ``$key`` ($($entries.Count) docs). Powrót: ``SUSModder / INDEX``.

$(Build-DocTable -Entries $entries)
"@
    $docId = New-OrUpdateFolderIndex -Url $mcp.Url -Token $mcp.Token -FolderKey $key -Title $title -Body $body -Existing $existing
    $folderResults.Add([PSCustomObject]@{ key = $key; title = $title; docId = $docId; count = $entries.Count })
    if ($DelayMs -gt 0) { Start-Sleep -Milliseconds $DelayMs }
}

# --- update main INDEX ---
$byKey = @{}
foreach ($f in $folderResults) { $byKey[$f.key] = $f }

$indexRows = [System.Collections.Generic.List[string]]::new()
foreach ($k in @(
        'DOC', 'DOC/PLAN', 'DOC/Core', 'DOC/Frontend', 'DOC/POC', 'DOC/Updater', 'DOC/readme',
        'DOC/_archive', 'SKRYPTY', 'SKRYPTY/Build', 'SKRYPTY/Test', 'SKRYPTY/Utilities'
    )) {
    if (-not $byKey.ContainsKey($k)) { continue }
    $f = $byKey[$k]
    $indexRows.Add("| ``$($f.key)`` | ``$($f.docId)`` | $($f.count) |")
}

$quickPaths = @(
    'DOC/PLAN/README.md',
    'DOC/PLAN/2026-07-24-support-banner-suppi.md',
    'DOC/Core/01_Configuration.md',
    'DOC/Frontend/02_Views.md',
    'SKRYPTY/Build/build-dual-channel.ps1',
    'DOC/README.md'
)
$quickRows = [System.Collections.Generic.List[string]]::new()
foreach ($p in $quickPaths) {
    $e = $docs | Where-Object path -eq $p | Select-Object -First 1
    if ($e) { $quickRows.Add("| ``$p`` | ``$($e.docId)`` |") }
}

$mainBody = @"
> Pełna migracja 2026-07-24. **$($docs.Count)** dokumentów. Nawigacja: indeksy per katalog (opcja A).

## Jak korzystać (agent)

1. Zacznij od tej strony albo ``keyword_search`` / ``semantic_search``
2. Wejdź w indeks folderu → weź ``docId`` → ``read_document``
3. Tytuły dokumentów: ``DOC / PLAN / ...``, indeksy: ``INDEX / DOC / PLAN``

## Indeksy katalogów

| folder | docId indeksu | docs |
|--------|---------------|------|
$($indexRows -join "`n")

``DOC/_archive`` ma własne pod-indeksy (Updater-Refactoring, PLAN, POC, Frontend, Localization, …).

## CI — zostaje w repo (executable)

- ``SKRYPTY/Build/generate-secrets.ps1``
- ``SKRYPTY/Build/build-dual-channel.ps1``
- ``SKRYPTY/Build/Assert-NotSingleFile.ps1``
- ``SKRYPTY/Build/deploy-to-server.ps1``

## Szybkie wejścia

| Ścieżka | docId |
|---------|-------|
$($quickRows -join "`n")

## Migrator

``````powershell
.\SKRYPTY\Utilities\sync-to-affine.ps1 -All -SkipReadback -DelayMs 1200
.\SKRYPTY\Utilities\generate-affine-folder-indexes.ps1
``````
"@

Write-Host "UPDATE main INDEX $mainIndexId" -ForegroundColor Magenta
$null = Invoke-AffineMcp -Url $mcp.Url -Token $mcp.Token -ToolName 'update_document' -Arguments @{
    docId   = $mainIndexId
    content = $mainBody
}

# save folder map
$folderResults | Sort-Object key | ConvertTo-Json -Depth 3 | Set-Content -Path $FolderMapPath -Encoding UTF8
Write-Host "`nZapisano $FolderMapPath ($($folderResults.Count) indeksów)" -ForegroundColor Cyan
$folderResults | Sort-Object key | Format-Table key, count, docId -AutoSize
