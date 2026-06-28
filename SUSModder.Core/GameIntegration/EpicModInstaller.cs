using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.GameIntegration
{
    /// <summary>
    /// Pobiera i rozpakowuje gotowy wariant moda FULL dla Epic do wskazanego katalogu.
    /// Używany przez lokalne instancje modpacków; nie modyfikuje katalogowego mods.InstallPath.
    /// </summary>
    public sealed class EpicModInstaller
    {
        private readonly HttpClient _httpClient;

        public EpicModInstaller(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
        }

        public async Task<ModInstallResult> InstallAsync(
            ModConfiguration modConfig,
            string targetInstallPath,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            CancellationToken ct = default)
        {
            if (modConfig == null)
                throw new ArgumentNullException(nameof(modConfig));
            if (string.IsNullOrWhiteSpace(targetInstallPath))
                throw new ArgumentException("Target install path cannot be empty.", nameof(targetInstallPath));

            string? tempDirectory = null;

            try
            {
                progress.Report(5, "Rozpoczynam instalację moda Epic...");

                var resolution = await ModDownloadUrlBuilder.ResolveWithHashAsync(modConfig, "epic", ct);
                if (string.IsNullOrWhiteSpace(resolution.Url))
                    return ModInstallResult.Failed("mod_pack_platform_variant_unavailable");

                log.Write($"[EpicModInstaller] URL: {resolution.Url}");

                var modsInstallPath = PathSettings.ModsInstallPath;
                Directory.CreateDirectory(modsInstallPath);

                var uniqueTempId = Guid.NewGuid().ToString("N");
                tempDirectory = Path.Combine(modsInstallPath, "temp", uniqueTempId);
                Directory.CreateDirectory(tempDirectory);

                var modFile = Path.Combine(tempDirectory, "mod.zip");

                progress.Report(10, "Pobieranie moda...");
                var downloaded = await DownloadFileAsync(
                    resolution.Url,
                    modFile,
                    progress,
                    10,
                    50,
                    log,
                    ct);

                if (!downloaded)
                    return ModInstallResult.Failed("mod_pack_full_mod_download_failed");

                if (!File.Exists(modFile))
                    return ModInstallResult.Failed("mod_pack_full_mod_download_failed");

                if (!string.IsNullOrWhiteSpace(resolution.ExpectedSha256))
                {
                    progress.Report(50, "Weryfikacja sumy kontrolnej...");
                    var actualHash = await Sha256Verifier.ComputeFileHexAsync(modFile, ct);
                    if (!string.Equals(actualHash, resolution.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        log.Write($"[EpicModInstaller] SHA256 mismatch: expected {resolution.ExpectedSha256}, got {actualHash}");
                        return ModInstallResult.Failed("mod_pack_full_mod_sha256_mismatch");
                    }
                }

                progress.Report(55, "Rozpakowywanie archiwum...");
                var tempExtractPath = Path.Combine(tempDirectory, "extractMod");
                Directory.CreateDirectory(tempExtractPath);

                var extractor = new SharpCompressExtractor();
                await extractor.ExtractAsync(modFile, tempExtractPath, progress: null, ct: ct);

                var sourcePath = ResolveSourcePath(tempExtractPath);
                if (string.IsNullOrWhiteSpace(sourcePath))
                    return ModInstallResult.Failed("mod_pack_full_mod_no_files");

                progress.Report(80, "Kopiowanie plików...");
                var targetGamePath = ResolveTargetGamePath(targetInstallPath);
                Directory.CreateDirectory(targetGamePath);
                CopyContent(sourcePath, targetGamePath);

                progress.Report(95, "Finalizowanie instalacji...");

                log.Write($"[EpicModInstaller] Zainstalowano moda do: {targetGamePath}");
                progress.Report(100, "Gotowe");
                return ModInstallResult.Succeeded();
            }
            catch (HttpRequestException ex)
            {
                log.Write($"[EpicModInstaller] HTTP error: {ex.Message}");
                return ModInstallResult.Failed("mod_pack_full_mod_download_failed");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Write($"[EpicModInstaller] Unexpected error: {ex.Message}");
                return ModInstallResult.Failed("mod_pack_epic_install_failed");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempDirectory))
                {
                    ModsStorageCleanupService.TryDeleteInstallTempDirectory(tempDirectory, log.Write);
                }
            }
        }

        private async Task<bool> DownloadFileAsync(
            string url,
            string filePath,
            IProgressReporter progress,
            int progressMin,
            int progressMax,
            IDiagnosticsOutput log,
            CancellationToken ct)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    log.Write($"[EpicModInstaller] 404 for {url}");
                    return false;
                }

                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes > 0;

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalBytesRead = 0L;
                int bytesRead;
                var lastProgressReport = DateTime.Now;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) != 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead, ct);
                    totalBytesRead += bytesRead;

                    if (canReportProgress && DateTime.Now - lastProgressReport > TimeSpan.FromMilliseconds(500))
                    {
                        var rawPercent = (int)((totalBytesRead * 100L) / totalBytes);
                        var mapped = progressMin + (rawPercent * (progressMax - progressMin) / 100);
                        progress.Report(mapped, $"Pobieranie: {totalBytesRead / (1024.0 * 1024.0):F1} MB / {totalBytes / (1024.0 * 1024.0):F1} MB");
                        lastProgressReport = DateTime.Now;
                    }
                }

                return true;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Write($"[EpicModInstaller] Download error: {ex.Message}");
                return false;
            }
        }

        private static string? ResolveSourcePath(string tempExtractPath)
        {
            if (Directory.Exists(Path.Combine(tempExtractPath, "BepInEx")))
                return tempExtractPath;

            var subDirs = Directory.GetDirectories(tempExtractPath);
            if (subDirs.Length == 1)
                return subDirs[0];

            // If the archive extracts its files directly into the root (no single
            // wrapper folder), use the root as the source.
            if (subDirs.Length == 0 && Directory.EnumerateFiles(tempExtractPath).Any())
                return tempExtractPath;

            // More than one subfolder is ambiguous; treat it as a direct-root layout
            // only if there are also files at the root, otherwise fail.
            if (subDirs.Length > 1 && Directory.EnumerateFiles(tempExtractPath).Any())
                return tempExtractPath;

            return null;
        }

        private static string ResolveTargetGamePath(string targetInstallPath)
        {
            var directoryName = Path.GetFileName(
                targetInstallPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.Equals(directoryName, "AmongUs", StringComparison.OrdinalIgnoreCase))
                return targetInstallPath;

            return Path.Combine(targetInstallPath, "AmongUs");
        }

        private static void CopyContent(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                DirectoryCopy(dir, destSubDir);
            }
        }

        private static void DirectoryCopy(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                DirectoryCopy(dir, destSubDir);
            }
        }
    }
}
