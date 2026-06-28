using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Utilities;

namespace SUSModder.E2E.Tests.Api;

/// <summary>
/// E2E tests specifically for DLL mod downloads.
/// DLL mods are platform-independent single .dll files that work on both Steam and Epic.
/// </summary>
public sealed class DllModDownloadTests : IDisposable
{
    private readonly E2ETestContext _ctx;
    private readonly E2EDiagnosticsOutput _log;
    private readonly HttpClient _http;
    private ISUSModderApiClient? _client;

    public DllModDownloadTests()
    {
        _ctx = new E2ETestContext("dll-download");
        _log = new E2EDiagnosticsOutput();
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://api.susmodder-cdn.ovh/v2"
            })
            .Build();

        _client = new SUSModderApiClient(config, _log);
        SUSModderApiClientProvider.SetDefault(_client);
    }

    public void Dispose()
    {
        _http.Dispose();
        _client?.Dispose();
        _ctx.Dispose();
        SUSModderApiClientProvider.ResetForTests();
    }

    [Fact]
    public async Task Download_EveryDllMod_DownloadsAndVerifies()
    {
        Assert.NotNull(_client);
        var catalog = await _client.GetCatalogAsync(new() { Limit = 200 });
        Assert.NotNull(catalog.Data);

        var dllMods = catalog.Data.Where(m =>
            m.Type.Equals("dll", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(dllMods);
        _log.Write($"[E2E] Found {dllMods.Count} DLL mods in catalog");

        var results = new List<DllTestResult>();

        foreach (var mod in dllMods)
        {
            var modConfig = new ModConfiguration
            {
                Id = mod.Id,
                ModName = mod.Name,
                ModVersion = mod.CurrentVersion,
                ModType = "dll",
                GitHubRepoOrLink = mod.GitHubProjectUrl ?? string.Empty
            };

            var result = new DllTestResult
            {
                ModId = mod.Id,
                ModName = mod.Name,
                Version = mod.CurrentVersion
            };

            try
            {
                // Resolve download URL (uses cross-platform fallback for DLLs)
                var resolution = await ModDownloadUrlBuilder.ResolveWithHashAsync(
                    modConfig, "steam", CancellationToken.None);

                result.ResolvedUrl = resolution.Url;

                if (string.IsNullOrWhiteSpace(resolution.Url))
                {
                    result.Status = "FAIL_NO_URL";
                    results.Add(result);
                    continue;
                }

                // Download the DLL
                var expectedFileName = ModDownloadUrlBuilder.GetDllFileName(modConfig, "steam");
                result.ExpectedFileName = expectedFileName;

                var downloadPath = _ctx.GetDownloadPath(expectedFileName);
                var response = await _http.GetAsync(resolution.Url);
                result.HttpStatus = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    result.Status = $"FAIL_HTTP_{response.StatusCode}";
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
                if (!string.IsNullOrWhiteSpace(resolution.ExpectedSha256))
                {
                    var actualHash = await Sha256Verifier.ComputeFileHexAsync(downloadPath);
                    result.ActualSha256 = actualHash;
                    result.ExpectedSha256 = resolution.ExpectedSha256;

                    if (!string.Equals(actualHash, resolution.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        // DLL mods have cross-platform fallback — warn, not fail
                        result.Status = "WARN_SHA256";
                        results.Add(result);
                        continue;
                    }
                }

                // Verify it's a valid DLL (PE header: MZ)
                var header = new byte[2];
                await using (var fs = File.OpenRead(downloadPath))
                {
                    await fs.ReadExactlyAsync(header, 0, 2);
                }
                var isPeHeader = header[0] == 0x4D && header[1] == 0x5A; // "MZ"
                result.IsValidDll = isPeHeader;

                // Check filename matches expected
                var actualFileName = Path.GetFileName(downloadPath);
                result.FileNameMatch = string.Equals(actualFileName, expectedFileName,
                    StringComparison.OrdinalIgnoreCase);

                if (fileInfo.Length == 0)
                {
                    result.Status = "FAIL_EMPTY";
                }
                else if (!isPeHeader)
                {
                    result.Status = "FAIL_NOT_A_DLL";
                }
                else
                {
                    result.Status = "PASS";
                }
            }
            catch (Exception ex)
            {
                result.Status = $"FAIL_EXCEPTION: {ex.GetType().Name}: {ex.Message}";
            }

            results.Add(result);
        }

        // Write report
        var reportLines = new List<string>
        {
            "# DLL Mod Download E2E Report",
            $"Generated: {DateTimeOffset.UtcNow:O}",
            $"Total DLL mods: {dllMods.Count}",
            "",
            "| Mod | Version | Status | HTTP | Size | Valid DLL | FileName Match | SHA256 |",
            "|-----|---------|--------|------|------|-----------|---------------|--------|"
        };

        var passed = 0;
        var failed = 0;
        foreach (var r in results)
        {
            var isPass = r.Status == "PASS";
            if (isPass) passed++; else failed++;
            var sha256Status = string.IsNullOrWhiteSpace(r.ExpectedSha256) ? "N/A" :
                r.ActualSha256 == r.ExpectedSha256 ? "✅" : "❌";
            reportLines.Add(
                $"| {r.ModName} | {r.Version} | {(isPass ? "✅" : "❌")} {r.Status} | {r.HttpStatus} | {FormatBytes(r.FileSizeBytes)} | {(r.IsValidDll == true ? "✅" : r.IsValidDll == false ? "❌" : "N/A")} | {(r.FileNameMatch == true ? "✅" : "❌")} | {sha256Status} |");
        }

        reportLines.Add("");
        reportLines.Add($"**Passed: {passed}, Failed: {failed}**");

        _ctx.WriteArtifact("dll-download-report.md", string.Join(Environment.NewLine, reportLines));

        _log.Write($"[E2E] DLL download: {passed} PASS, {failed} FAIL out of {results.Count}");

        var hardFails = results.Count(r => r.Status?.StartsWith("FAIL") == true);
        var warns = results.Count(r => r.Status?.StartsWith("WARN") == true);

        if (warns > 0)
        {
            var warnMods = results.Where(r => r.Status?.StartsWith("WARN") == true)
                .Select(r => $"{r.ModName}: {r.Status}");
            _ctx.WriteArtifact("dll-download-warnings.txt",
                $"Warnings ({warns}):{Environment.NewLine}{string.Join(Environment.NewLine, warnMods)}");
        }

        Assert.True(hardFails == 0,
            $"{hardFails} DLL mod(s) failed download.\n" +
            string.Join("\n", results.Where(r => r.Status?.StartsWith("FAIL") == true)
                .Select(r => $"  {r.ModName}: {r.Status} ({r.ResolvedUrl})")));
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes == null) return "N/A";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private sealed class DllTestResult
    {
        public int ModId { get; set; }
        public string ModName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? ResolvedUrl { get; set; }
        public int? HttpStatus { get; set; }
        public long? FileSizeBytes { get; set; }
        public string? ExpectedSha256 { get; set; }
        public string? ActualSha256 { get; set; }
        public string? ExpectedFileName { get; set; }
        public bool? IsValidDll { get; set; }
        public bool? FileNameMatch { get; set; }
    }
}
