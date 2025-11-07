# Test Velopack API Endpoint
param([string]$Url = "https://susmodder.app/api/releases?channel=win")

Write-Host "Testing Velopack API Endpoint" -ForegroundColor Cyan
Write-Host "URL: $Url" -ForegroundColor Yellow
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri $Url -Method Get -ErrorAction Stop
    
    Write-Host "SUCCESS - API is responding" -ForegroundColor Green
    Write-Host ""
    
    # Check response structure
    Write-Host "Validation:" -ForegroundColor Cyan
    
    if ($response.success -eq $true) {
        Write-Host "  [OK] success = true" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] success is not true" -ForegroundColor Red
    }
    
    if ($response.latestVersion) {
        Write-Host "  [OK] latestVersion = $($response.latestVersion)" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] latestVersion missing" -ForegroundColor Red
    }
    
    if ($response.manifest) {
        Write-Host "  [OK] manifest exists" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] manifest missing" -ForegroundColor Red
    }
    
    if ($response.downloadBaseUrl) {
        Write-Host "  [OK] downloadBaseUrl = $($response.downloadBaseUrl)" -ForegroundColor Green
    } else {
        Write-Host "  [WARN] downloadBaseUrl missing (will use API host)" -ForegroundColor Yellow
    }
    
    if ($response.manifest.Releases) {
        $release = $response.manifest.Releases[0]
        Write-Host ""
        Write-Host "First Release:" -ForegroundColor Cyan
        Write-Host "  Version: $($release.Version)" -ForegroundColor White
        Write-Host "  File: $($release.File)" -ForegroundColor White
        Write-Host "  SHA256: $($release.SHA256)" -ForegroundColor White
        
        if ($release.SHA256 -eq "dummychecksum") {
            Write-Host ""
            Write-Host "  [CRITICAL] SHA256 is still 'dummychecksum'!" -ForegroundColor Red
            Write-Host "  You need to update backend to return real checksum:" -ForegroundColor Yellow
            Write-Host "  76c9188b143a8c44ed65132c648ee89185941f615eb81b8d663d74f57719e705" -ForegroundColor White
        } elseif ($release.SHA256.Length -eq 64) {
            Write-Host ""
            Write-Host "  [OK] SHA256 looks valid (64 hex chars)" -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "  [WARN] SHA256 format might be invalid" -ForegroundColor Yellow
        }
        
        # Test download URL
        $downloadUrl = if ($response.downloadBaseUrl) {
            "$($response.downloadBaseUrl.TrimEnd('/'))/$($release.File)"
        } else {
            "https://susmodder.app/releases/$($release.File)"
        }
        
        Write-Host ""
        Write-Host "Download URL Test:" -ForegroundColor Cyan
        Write-Host "  $downloadUrl" -ForegroundColor White
        
        try {
            $headResponse = Invoke-WebRequest -Uri $downloadUrl -Method Head -ErrorAction Stop
            Write-Host "  [OK] File is accessible (HTTP $($headResponse.StatusCode))" -ForegroundColor Green
            if ($headResponse.Headers.'Content-Length') {
                $size = [int]$headResponse.Headers.'Content-Length'[0]
                Write-Host "  File size: $size bytes" -ForegroundColor Gray
            }
        } catch {
            Write-Host "  [FAIL] File not accessible: $($_.Exception.Message)" -ForegroundColor Red
        }
        
    } else {
        Write-Host ""
        Write-Host "  [FAIL] No releases in manifest" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "Full Response:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 10
    
} catch {
    Write-Host "FAILED - Could not connect to API" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
