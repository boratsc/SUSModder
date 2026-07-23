# Generates SUSModder.Core/Secrets.cs from environment variables.
# Secrets.cs is gitignored and must never be committed.
#
# Required env vars (plaintext):
#   SUSMODDER_DOWNLOAD_TOKEN  - API Authorization token
#   SUSMODDER_7Z_PASSWORD     - Password for legacy vanilla 7z archives
#
# Usage (local):
#   $env:SUSMODDER_DOWNLOAD_TOKEN = "..."
#   $env:SUSMODDER_7Z_PASSWORD = "..."
#   .\SKRYPTY\Build\generate-secrets.ps1
#
# Usage (CI): called from GitHub Actions after injecting secrets into env.

param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $ProjectRoot "SUSModder.Core\Secrets.cs"
}

$token = $env:SUSMODDER_DOWNLOAD_TOKEN
$password = $env:SUSMODDER_7Z_PASSWORD

if ([string]::IsNullOrWhiteSpace($token)) {
    throw "SUSMODDER_DOWNLOAD_TOKEN is missing or empty."
}
if ([string]::IsNullOrWhiteSpace($password)) {
    throw "SUSMODDER_7Z_PASSWORD is missing or empty."
}

function ConvertTo-Base64Utf8([string]$Value) {
    return [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Value))
}

$tokenB64 = ConvertTo-Base64Utf8 $token
$passwordB64 = ConvertTo-Base64Utf8 $password

$content = @"
namespace SUSModder.Core
{
    public static class SecretProvider
    {
        public static string GetDownloadToken()
        {
            return Decrypt("$tokenB64");
        }
        public static string Get7zPassword()
        {
            return Decrypt("$passwordB64");
        }
        private static string Decrypt(string encrypted)
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encrypted));
        }
    }
}
"@

$outDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

Set-Content -Path $OutputPath -Value $content -Encoding UTF8 -NoNewline
Write-Host "Generated Secrets.cs at: $OutputPath" -ForegroundColor Green
Write-Host "WARNING: Do not commit this file. Do not upload it as a CI artifact." -ForegroundColor Yellow
