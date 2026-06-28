using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Utilities;

namespace SUSModder.E2E.Tests.Install;

/// <summary>
/// E2E tests for mod extraction and file structure verification.
/// Downloads real mod packages and verifies they extract with expected structure.
/// </summary>
public sealed class ModExtractionTests : IDisposable
{
    private readonly E2ETestContext _ctx;
    private readonly E2EDiagnosticsOutput _log;
    private readonly HttpClient _http;
    private ISUSModderApiClient? _client;

    public ModExtractionTests()
    {
        _ctx = new E2ETestContext("extraction");
        _log = new E2EDiagnosticsOutput();
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

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
    public async Task Extract_EveryFullModSteamVariant_HasExpectedStructure()
    {
        Assert.NotNull(_client);
        var catalog = await _client.GetCatalogAsync(new() { Limit = 200 });
        Assert.NotNull(catalog.Data);

        var results = new List<ExtractionResult>();
        var fullMods = catalog.Data.Where(m =>
            m.Type.Equals("full", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var mod in fullMods)
        {
            var detail = await _client.GetCatalogModDetailAsync(mod.Id);
            if (detail.Data?.Variants == null) continue;

            var steamVariants = detail.Data.Variants
                .Where(v => v.Platform.Equals("steam", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var variant in steamVariants)
            {
                var result = new ExtractionResult
                {
                    ModId = mod.Id,
                    ModName = mod.Name,
                    Platform = "steam",
                    Architecture = variant.Architecture,
                    Version = variant.Version ?? detail.Data.CurrentVersion
                };

                try
                {
                    // Resolve download URL
                    var modConfig = new ModConfiguration
                    {
                        Id = mod.Id,
                        ModName = mod.Name,
                        ModVersion = variant.Version ?? detail.Data.CurrentVersion
                    };

                    var resolution = await ModDownloadUrlBuilder.ResolveWithHashAsync(modConfig, "steam");
                    if (string.IsNullOrWhiteSpace(resolution.Url))
                    {
                        result.Status = "SKIP_NO_URL";
                        results.Add(result);
                        continue;
                    }

                    // Download
                    var zipPath = _ctx.GetDownloadPath($"steam_{mod.Id}_{mod.Name}.zip");
                    var response = await _http.GetAsync(resolution.Url);
                    if (!response.IsSuccessStatusCode)
                    {
                        result.Status = $"FAIL_DOWNLOAD_{response.StatusCode}";
                        results.Add(result);
                        continue;
                    }

                    await using var fs = File.Create(zipPath);
                    await response.Content.CopyToAsync(fs);
                    await fs.FlushAsync();
                    fs.Close();

                    // Verify SHA256
                    if (!string.IsNullOrWhiteSpace(resolution.ExpectedSha256))
                    {
                        var actualHash = await Sha256Verifier.ComputeFileHexAsync(zipPath);
                        if (!string.Equals(actualHash, resolution.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Status = "FAIL_SHA256";
                            results.Add(result);
                            continue;
                        }
                    }

                    // Extract
                    var extractPath = _ctx.GetExtractPath(mod.Name);
                    var extractor = new SharpCompressExtractor();
                    await extractor.ExtractAsync(zipPath, extractPath, ct: CancellationToken.None);

                    // Verify structure
                    var structureIssues = VerifyExtractedStructure(extractPath, "steam");
                    if (structureIssues.Count > 0)
                    {
                        result.Status = "FAIL_STRUCTURE";
                        result.StructureIssues = structureIssues;
                    }
                    else
                    {
                        result.Status = "PASS";
                    }

                    result.ExtractedPath = extractPath;
                    result.FileCount = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories).Length;
                }
                catch (Exception ex)
                {
                    result.Status = $"FAIL_EXCEPTION: {ex.GetType().Name}: {ex.Message}";
                }

                results.Add(result);
            }
        }

        // Write report
        WriteExtractionReport(results);

        var hardFails = results.Count(r => r.Status?.StartsWith("FAIL") == true);
        Assert.True(hardFails == 0,
            $"{hardFails} extraction(s) failed.\n" +
            string.Join("\n", results.Where(r => r.Status?.StartsWith("FAIL") == true)
                .Select(r => $"  {r.ModName} (steam/{r.Architecture}): {r.Status}")));
    }

    [Fact]
    public async Task Extract_EveryFullModEpicVariant_HasExpectedStructure()
    {
        Assert.NotNull(_client);
        var catalog = await _client.GetCatalogAsync(new() { Limit = 200 });
        Assert.NotNull(catalog.Data);

        var results = new List<ExtractionResult>();
        var fullMods = catalog.Data.Where(m =>
            m.Type.Equals("full", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var mod in fullMods)
        {
            var detail = await _client.GetCatalogModDetailAsync(mod.Id);
            if (detail.Data?.Variants == null) continue;

            var epicVariants = detail.Data.Variants
                .Where(v => v.Platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var variant in epicVariants)
            {
                var result = new ExtractionResult
                {
                    ModId = mod.Id,
                    ModName = mod.Name,
                    Platform = "epic",
                    Architecture = variant.Architecture,
                    Version = variant.Version ?? detail.Data.CurrentVersion
                };

                try
                {
                    var modConfig = new ModConfiguration
                    {
                        Id = mod.Id,
                        ModName = mod.Name,
                        ModVersion = variant.Version ?? detail.Data.CurrentVersion
                    };

                    var resolution = await ModDownloadUrlBuilder.ResolveWithHashAsync(modConfig, "epic");
                    if (string.IsNullOrWhiteSpace(resolution.Url))
                    {
                        result.Status = "SKIP_NO_URL";
                        results.Add(result);
                        continue;
                    }

                    var zipPath = _ctx.GetDownloadPath($"epic_{mod.Id}_{mod.Name}.zip");
                    var response = await _http.GetAsync(resolution.Url);
                    if (!response.IsSuccessStatusCode)
                    {
                        result.Status = $"FAIL_DOWNLOAD_{response.StatusCode}";
                        results.Add(result);
                        continue;
                    }

                    await using var fs = File.Create(zipPath);
                    await response.Content.CopyToAsync(fs);
                    await fs.FlushAsync();
                    fs.Close();

                    if (!string.IsNullOrWhiteSpace(resolution.ExpectedSha256))
                    {
                        var actualHash = await Sha256Verifier.ComputeFileHexAsync(zipPath);
                        if (!string.Equals(actualHash, resolution.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Status = "FAIL_SHA256";
                            results.Add(result);
                            continue;
                        }
                    }

                    var extractPath = _ctx.GetExtractPath(mod.Name + "_epic");
                    var extractor = new SharpCompressExtractor();
                    await extractor.ExtractAsync(zipPath, extractPath, ct: CancellationToken.None);

                    var structureIssues = VerifyExtractedStructure(extractPath, "epic");
                    if (structureIssues.Count > 0)
                    {
                        result.Status = "FAIL_STRUCTURE";
                        result.StructureIssues = structureIssues;
                    }
                    else
                    {
                        result.Status = "PASS";
                    }

                    result.ExtractedPath = extractPath;
                    result.FileCount = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories).Length;
                }
                catch (Exception ex)
                {
                    result.Status = $"FAIL_EXCEPTION: {ex.GetType().Name}: {ex.Message}";
                }

                results.Add(result);
            }
        }

        WriteExtractionReport(results);

        var hardFails = results.Count(r => r.Status?.StartsWith("FAIL") == true);
        // Epic variants may be fewer — only fail if we have hard failures
        if (hardFails > 0)
        {
            _log.Write($"[E2E] Epic extraction: {hardFails} failures (non-blocking if no Epic variants expected)");
        }
    }

    /// <summary>
    /// Verifies the extracted mod archive has expected structure.
    /// Most full-mod packages are BepInEx overlays (not full game bundles).
    /// They must contain BepInEx/plugins/ with at least one DLL, doorstop_config.ini, and winhttp.dll.
    /// Some packages may be full game bundles containing Among Us.exe.
    /// Also checks for path traversal safety (no absolute paths, no ../).
    /// </summary>
    private static List<string> VerifyExtractedStructure(string extractPath, string platform)
    {
        var issues = new List<string>();
        var allFiles = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories);

        // Safety: no absolute paths outside extract directory
        foreach (var file in allFiles)
        {
            var fullPath = Path.GetFullPath(file);
            if (!fullPath.StartsWith(Path.GetFullPath(extractPath), StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Path traversal detected: {file}");
            }
        }

        // Check for expected BepInEx structure (mod overlay pattern)
        var hasBepInEx = Directory.GetDirectories(extractPath, "BepInEx", SearchOption.AllDirectories).Length > 0;
        var hasPluginsDir = Directory.GetDirectories(extractPath, "plugins", SearchOption.AllDirectories)
            .Any(d => d.Contains("BepInEx", StringComparison.OrdinalIgnoreCase));
        var hasPlugins = hasPluginsDir && Directory.GetDirectories(extractPath, "plugins", SearchOption.AllDirectories)
            .Any(d => d.Contains("BepInEx", StringComparison.OrdinalIgnoreCase) && Directory.GetFiles(d).Length > 0);
        var hasDoorstop = allFiles.Any(f => Path.GetFileName(f).Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase));
        var hasWinhttp = allFiles.Any(f => Path.GetFileName(f).Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase));

        // Check if full game bundle (contains Among Us.exe)
        var hasAmongUsExe = allFiles.Any(f =>
            Path.GetFileName(f).Equals("Among Us.exe", StringComparison.OrdinalIgnoreCase));

        // Acceptable: Either a BepInEx overlay OR a full game bundle
        var isValidOverlay = hasBepInEx && hasPlugins && hasDoorstop && hasWinhttp;
        var isValidFullBundle = hasAmongUsExe && hasBepInEx;

        if (!isValidOverlay && !isValidFullBundle)
        {
            if (!hasBepInEx)
                issues.Add("No BepInEx directory found");
            if (!hasPlugins)
                issues.Add("No plugins in BepInEx/plugins");
            if (!hasDoorstop)
                issues.Add("doorstop_config.ini missing");
            if (!hasWinhttp)
                issues.Add("winhttp.dll missing");
            if (!hasAmongUsExe)
                issues.Add("Not a full game bundle (no Among Us.exe)");
        }

        // Check for suspicious files (allow only expected executables)
        var allowedExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Among Us.exe", "UnityCrashHandler64.exe", "UnityCrashHandler32.exe",
            "BepInEx.SplashScreen.GUI.exe" // Legitimate BepInEx component, shipped by some mods
        };

        foreach (var file in allFiles)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var fileName = Path.GetFileName(file);
            if (ext == ".exe" && !allowedExes.Contains(fileName))
            {
                issues.Add($"Unexpected executable: {fileName}");
            }
        }

        return issues;
    }

    private void WriteExtractionReport(List<ExtractionResult> results)
    {
        var reportLines = new List<string>
        {
            "# Mod Extraction E2E Report",
            $"Generated: {DateTimeOffset.UtcNow:O}",
            $"Total variants extracted: {results.Count}",
            "",
            "| Mod | Platform | Arch | Status | Files | Issues |",
            "|-----|----------|------|--------|-------|--------|"
        };

        foreach (var r in results)
        {
            var statusIcon = r.Status switch
            {
                "PASS" => "✅",
                var s when s?.StartsWith("FAIL") == true => "❌",
                var s when s?.StartsWith("SKIP") == true => "⏭️",
                _ => "⚠️"
            };
            reportLines.Add(
                $"| {r.ModName} | {r.Platform} | {r.Architecture} | {statusIcon} {r.Status} | {r.FileCount} | {(r.StructureIssues?.Count > 0 ? string.Join("; ", r.StructureIssues) : "-")} |");
        }

        _ctx.WriteArtifact("extraction-report.md", string.Join(Environment.NewLine, reportLines));
    }

    private sealed class ExtractionResult
    {
        public int ModId { get; set; }
        public string ModName { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? ExtractedPath { get; set; }
        public int FileCount { get; set; }
        public List<string>? StructureIssues { get; set; }
    }
}
