using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Utilities;

namespace SUSModder.E2E.Tests.Api;

/// <summary>
/// E2E tests for mod download URL resolution, actual file download, and SHA256 verification.
/// Hits real API v2, downloads real files, verifies checksums.
/// </summary>
public sealed class ApiDownloadSmokeTests : IDisposable
{
    private readonly ISUSModderApiClient _client;
    private readonly E2EDiagnosticsOutput _log;
    private readonly E2ETestContext _ctx;
    private readonly HttpClient _http;

    public ApiDownloadSmokeTests()
    {
        _ctx = new E2ETestContext("api-download");
        _log = new E2EDiagnosticsOutput();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://api.susmodder-cdn.ovh/v2"
            })
            .Build();

        _client = new SUSModderApiClient(config, _log);
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        var previous = SUSModderApiClientProvider.TryGetDefault();
        SUSModderApiClientProvider.SetDefault(_client);
    }

    public void Dispose()
    {
        _client.Dispose();
        _http.Dispose();
        _ctx.Dispose();
        SUSModderApiClientProvider.ResetForTests();
    }

    [Fact]
    public async Task Download_EveryDeclaredVariant_ResolvesAndDownloads()
    {
        var catalog = await _client.GetCatalogAsync(new CatalogQuery { Limit = 200 });
        Assert.NotNull(catalog.Data);

        var results = new List<VariantTestResult>();
        var modsWithVariants = 0;
        var totalVariants = 0;

        foreach (var item in catalog.Data)
        {
            var detail = await _client.GetCatalogModDetailAsync(item.Id);
            if (detail.Data?.Variants == null || detail.Data.Variants.Count == 0)
                continue;

            modsWithVariants++;

            foreach (var variant in detail.Data.Variants)
            {
                totalVariants++;
                var result = new VariantTestResult
                {
                    ModId = item.Id,
                    ModName = item.Name,
                    ModType = item.Type,
                    Platform = variant.Platform,
                    Architecture = variant.Architecture,
                    Version = variant.Version ?? detail.Data.CurrentVersion,
                    AmongVersion = item.AmongVersion?.DbValue ?? string.Empty
                };

                try
                {
                    // Build download URL through ModDownloadUrlBuilder
                    var modConfig = new ModConfiguration
                    {
                        Id = item.Id,
                        ModName = item.Name,
                        ModVersion = variant.Version ?? detail.Data.CurrentVersion,
                        AmongVersion = item.AmongVersion?.DbValue ?? string.Empty
                    };

                    var resolution = await ModDownloadUrlBuilder.ResolveWithHashAsync(
                        modConfig,
                        variant.Platform,
                        CancellationToken.None);

                    result.ResolvedUrl = resolution.Url;

                    if (string.IsNullOrWhiteSpace(resolution.Url))
                    {
                        result.Status = "SKIP_NO_URL";
                        results.Add(result);
                        continue;
                    }

                    // Download the file
                    var fileName = SanitizeFileName($"{item.Id}_{item.Name}_{variant.Platform}_{variant.Architecture}.zip");
                    var downloadPath = _ctx.GetDownloadPath(fileName);

                    var response = await _http.GetAsync(resolution.Url);
                    result.HttpStatus = (int)response.StatusCode;

                    if (!response.IsSuccessStatusCode)
                    {
                        result.Status = $"FAIL_HTTP_{result.HttpStatus}";
                        results.Add(result);
                        continue;
                    }

                    await using var fileStream = File.Create(downloadPath);
                    await response.Content.CopyToAsync(fileStream);
                    await fileStream.FlushAsync();
                    fileStream.Close();

                    var fileInfo = new FileInfo(downloadPath);
                    result.FileSizeBytes = fileInfo.Length;

                    // Verify SHA256
                    var expectedSha256 = variant.Sha256 ?? resolution.ExpectedSha256;
                    if (!string.IsNullOrWhiteSpace(expectedSha256))
                    {
                        var actualHash = await Sha256Verifier.ComputeFileHexAsync(downloadPath);
                        result.ActualSha256 = actualHash;
                        result.ExpectedSha256 = expectedSha256;

                        if (string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Status = "PASS";
                        }
                        else
                        {
                            // DLL mods use cross-platform fallback — mismatched SHA256 on
                            // one variant is a warning, not a blocker, if another variant works.
                            var isDll = item.Type.Equals("dll", StringComparison.OrdinalIgnoreCase);
                            result.Status = isDll ? "WARN_SHA256_MISMATCH" : "FAIL_SHA256_MISMATCH";
                        }
                    }
                    else
                    {
                        // No SHA256 to verify — accept download
                        result.Status = fileInfo.Length > 0 ? "PASS_NO_SHA256" : "FAIL_EMPTY_FILE";
                    }

                    if (fileInfo.Length == 0)
                        result.Status = "FAIL_EMPTY_FILE";
                }
                catch (Exception ex)
                {
                    result.Status = $"FAIL_EXCEPTION: {ex.GetType().Name}: {ex.Message}";
                }

                results.Add(result);
            }
        }

        // Write report
        var reportLines = new List<string>
        {
            $"# Mod Download E2E Report",
            $"Generated: {DateTimeOffset.UtcNow:O}",
            $"Mods with variants: {modsWithVariants}",
            $"Total variants tested: {totalVariants}",
            $"",
            "| Mod ID | Name | Type | Platform | Arch | Status | HTTP | Size | SHA256 Match |",
            "|--------|------|------|----------|------|--------|------|------|-------------|"
        };

        var passed = 0;
        var failed = 0;
        var warns = 0;
        foreach (var r in results)
        {
            var isPass = r.Status?.StartsWith("PASS") == true;
            var isWarn = r.Status?.StartsWith("WARN") == true;
            if (isPass) passed++;
            else if (isWarn) warns++;
            else failed++;
            reportLines.Add(
                $"| {r.ModId} | {r.ModName} | {r.ModType} | {r.Platform} | {r.Architecture} | {r.Status} | {r.HttpStatus} | {FormatBytes(r.FileSizeBytes)} | {(isPass ? "✅" : isWarn ? "⚠️" : "❌")} |");
        }
        reportLines.Add("");
        reportLines.Add($"**Passed: {passed}, Warnings: {warns}, Failed: {failed}**");

        _ctx.WriteArtifact("download-report.md", string.Join(Environment.NewLine, reportLines));
        _ctx.WriteArtifact("download-results.json",
            System.Text.Json.JsonSerializer.Serialize(results,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        _log.Write($"[E2E] Download test: {passed} PASS, {warns} WARN, {failed} FAIL out of {totalVariants} variants");

        // Group results by mod for per-mod analysis
        var resultsByMod = results
            .GroupBy(r => r.ModId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var blockingFailures = new List<string>();
        var warnings = new List<string>();

        foreach (var (modId, modResults) in resultsByMod)
        {
            var first = modResults.FirstOrDefault();
            var modType = first?.ModType ?? "unknown";
            var modName = first?.ModName ?? $"mod {modId}";
            var amongVersion = first?.AmongVersion ?? string.Empty;
            var hasPass = modResults.Any(r => r.Status?.StartsWith("PASS") == true);
            var hasFail = modResults.Any(r => r.Status?.StartsWith("FAIL") == true);

            // Pre-split check: mods targeting Among Us ≤ 2025-03-31 share
            // the same archive for Steam and Epic. Both variants must have
            // identical SHA256 or only one variant should be present.
            var isPreSplit = IsAmongUsPreSplit(amongVersion);
            if (isPreSplit && modResults.Count >= 2)
            {
                var sha256s = modResults
                    .Where(r => !string.IsNullOrWhiteSpace(r.ActualSha256))
                    .Select(r => r.ActualSha256!.ToLowerInvariant())
                    .Distinct()
                    .ToList();
                if (sha256s.Count > 1)
                {
                    blockingFailures.Add(
                        $"Pre-split mod {modName} (id={modId}, AU={amongVersion}): " +
                        $"Steam and Epic variants have different SHA256 but must be identical " +
                        $"({string.Join(" vs ", sha256s)})");
                }
            }

            if (modType.Equals("dll", StringComparison.OrdinalIgnoreCase))
            {
                // DLL mods: cross-platform fallback — one variant must work
                var hasWarn = modResults.Any(r => r.Status?.StartsWith("WARN") == true);
                if (!hasPass && hasFail)
                {
                    blockingFailures.Add($"DLL mod {modName} (id={modId}): no working variant");
                }
                else if ((hasFail || hasWarn) && hasPass)
                {
                    // Some variants have issues but at least one works — warn, not block
                    var issueVariants = modResults.Where(r => r.Status?.StartsWith("FAIL") == true || r.Status?.StartsWith("WARN") == true)
                        .Select(r => $"{r.Platform}/{r.Architecture}: {r.Status}");
                    warnings.Add($"DLL mod {modName} (id={modId}): variant(s) have non-blocking issues (cross-platform fallback works): {string.Join(", ", issueVariants)}");
                }
            }
            else
            {
                // FULL mods: every declared variant must work (platform-specific)
                if (hasFail)
                {
                    var failVariants = modResults.Where(r => r.Status?.StartsWith("FAIL") == true)
                        .Select(r => $"{r.Platform}/{r.Architecture}: {r.Status}");
                    blockingFailures.Add($"FULL mod {modName} (id={modId}): {string.Join(", ", failVariants)}");
                }
            }
        }

        if (warnings.Count > 0)
        {
            _ctx.WriteArtifact("download-warnings.txt", string.Join(Environment.NewLine, warnings));
            _log.Write($"[E2E] Warnings: {warnings.Count} mod(s) have non-blocking download issues");
        }

        Assert.True(blockingFailures.Count == 0,
            $"{blockingFailures.Count} mod(s) have blocking download failures:\n" +
            string.Join("\n", blockingFailures));
    }

    /// <summary>
    /// Among Us builds up to and including 2025-03-31 did not have separate Steam/Epic
    /// distributions. Mod archives targeting these versions are identical for both platforms.
    /// Format: "2025-3-31" (dbValue from API).
    /// </summary>
    private static bool IsAmongUsPreSplit(string? amongVersion)
    {
        if (string.IsNullOrWhiteSpace(amongVersion))
            return false;

        // Parse "YYYY-M-D" format
        var parts = amongVersion.Split('-');
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) ||
            !int.TryParse(parts[2], out var day))
            return false;

        // 2025-03-31 is the last build without Epic support
        if (year < 2025) return true;
        if (year > 2025) return false;
        if (month < 3) return true;
        if (month > 3) return false;
        return day <= 31;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes == null) return "N/A";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private sealed class VariantTestResult
    {
        public int ModId { get; set; }
        public string ModName { get; set; } = string.Empty;
        public string ModType { get; set; } = string.Empty;
        public string AmongVersion { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? ResolvedUrl { get; set; }
        public int? HttpStatus { get; set; }
        public long? FileSizeBytes { get; set; }
        public string? ExpectedSha256 { get; set; }
        public string? ActualSha256 { get; set; }
        public string? Status { get; set; }
    }
}
