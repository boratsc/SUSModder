param(
    [string]$RegionFile = "C:\Users\Administrator\AppData\LocalLow\Innersloth\Among Us\regionInfo.json",
    [string[]]$BaseUrl = @(),
    [string]$Authorization = "",
    [int]$TimeoutSec = 10
)

$endpoints = @(
    @{ Method = "GET"; Path = "/api/games" },
    @{ Method = "GET"; Path = "/api/games/filtered" },
    @{ Method = "POST"; Path = "/api/user"; Body = "{}" }
)

function Get-RegionUrls {
    param([string]$Path)

    $json = Get-Content -Raw -Path $Path | ConvertFrom-Json
    $urls = foreach ($region in $json.Regions) {
        foreach ($server in $region.Servers) {
            if ($server.Ip -match '^https?://') {
                $server.Ip.TrimEnd('/')
            }
        }
    }

    return $urls | Select-Object -Unique
}

function Invoke-Probe {
    param(
        [string]$Method,
        [string]$Url,
        [string]$Auth,
        [string]$Body,
        [int]$Timeout
    )

    $headers = @{
        'User-Agent' = 'SUSModder-Lobby-Probe/0.1'
        'Accept' = 'application/json, text/plain, */*'
    }

    if ($Auth) {
        $headers['Authorization'] = $Auth
    }

    try {
        $invokeParams = @{
            Uri = $Url
            Method = $Method
            Headers = $headers
            TimeoutSec = $Timeout
        }

        if ($Method -eq 'POST') {
            $invokeParams['Body'] = $Body
            $invokeParams['ContentType'] = 'application/json'
        }

        $response = Invoke-WebRequest @invokeParams
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            StatusDescription = [string]$response.StatusDescription
            Body = [string]$response.Content
        }
    }
    catch {
        $webResponse = $_.Exception.Response
        if ($webResponse) {
            $reader = New-Object System.IO.StreamReader($webResponse.GetResponseStream())
            $content = $reader.ReadToEnd()
            $reader.Dispose()

            return [pscustomobject]@{
                StatusCode = [int]$webResponse.StatusCode
                StatusDescription = [string]$webResponse.StatusDescription
                Body = [string]$content
            }
        }

        return [pscustomobject]@{
            StatusCode = -1
            StatusDescription = $_.Exception.GetType().Name
            Body = $_.Exception.Message
        }
    }
}

if (-not $BaseUrl -or $BaseUrl.Count -eq 0) {
    if (-not (Test-Path -LiteralPath $RegionFile)) {
        Write-Error "Brak pliku regionow: $RegionFile"
        exit 1
    }

    $BaseUrl = Get-RegionUrls -Path $RegionFile
}

Write-Host '== Matchmaker probe =='
Write-Host ("Authorization ustawione: {0}" -f ($(if ($Authorization) { 'tak' } else { 'nie' })))

foreach ($base in $BaseUrl) {
    Write-Host "`n## $base"

    foreach ($endpoint in $endpoints) {
        $url = $base.TrimEnd('/') + $endpoint.Path
        $result = Invoke-Probe -Method $endpoint.Method -Url $url -Auth $Authorization -Body $endpoint.Body -Timeout $TimeoutSec
        Write-Host (("{0,-4} {1,-20} -> {2} {3}") -f $endpoint.Method, $endpoint.Path, $result.StatusCode, $result.StatusDescription)

        if ($result.Body) {
            $singleLine = ($result.Body -split "`r?`n" | Where-Object { $_ -ne '' }) -join ' '
            if ($singleLine.Length -gt 220) {
                $singleLine = $singleLine.Substring(0, 220)
            }
            Write-Host "      $singleLine"
        }
    }
}
