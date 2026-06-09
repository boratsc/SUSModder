using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration.Steam;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.GameIntegration;

public sealed class SteamVanillaProvider
{
    private readonly IConfiguration _configuration;
    private readonly AmongUsManifestService _manifestService;
    private readonly VanillaCacheService _cacheService;
    private readonly DepotDownloaderRunner _depotDownloader;

    public SteamVanillaProvider(IConfiguration configuration)
    {
        _configuration = configuration;
        _manifestService = new AmongUsManifestService(configuration);
        _cacheService = new VanillaCacheService();
        _depotDownloader = new DepotDownloaderRunner();
    }

    public async Task<VanillaAcquireResult> AcquireAsync(
        string amongVersion,
        string targetDirectory,
        IProgressReporter progress,
        IDiagnosticsOutput log,
        ModManagerUserCallbacks userCallbacks,
        CancellationToken ct = default)
    {
        var normalizedVersion = AmongUsVersionHelper.NormalizeAmongVersion(amongVersion);
        var storageVersion = AmongUsVersionHelper.ToStorageVersion(normalizedVersion);
        var vanillaRoot = VanillaCacheService.GetVanillaRoot(PathSettings.ModsInstallPath);
        var extractedPath = VanillaCacheService.GetExtractedPath(vanillaRoot, storageVersion);

        Directory.CreateDirectory(vanillaRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(extractedPath)!);

        var manifest = await _manifestService.GetManifestForVersionAsync(normalizedVersion, ct);
        var preferDepotDownloader = new UserSettingsService().LoadUserSettings().PreferDepotDownloader;

        if (_cacheService.IsValidExtractedCache(extractedPath, manifest?.ManifestId))
        {
            log.Write($"[Vanilla] Cache hit: {extractedPath}");
            _cacheService.TryDeleteArchiveWhenExtractedCacheValid(vanillaRoot, storageVersion, log.Write);
            progress.Report(55, "Kopiowanie gry vanilla z cache...");
            _cacheService.CopyExtractedToTarget(extractedPath, targetDirectory);
            return VanillaAcquireResult.Ok(VanillaAcquireSource.CacheHit);
        }

        if (preferDepotDownloader
            && manifest is not null
            && !string.IsNullOrWhiteSpace(manifest.ManifestId))
        {
            var ddResult = await TryDepotDownloaderAsync(
                normalizedVersion,
                storageVersion,
                extractedPath,
                targetDirectory,
                manifest!,
                progress,
                log,
                userCallbacks,
                ct);

            if (ddResult.Success)
                return ddResult;

            if (!string.Equals(ddResult.ErrorMessage, "fallback_7z", StringComparison.Ordinal))
                return ddResult;
        }
        else if (preferDepotDownloader && manifest is null)
        {
            log.Write("[Vanilla] Brak manifestId w API — fallback do paczki 7z.");
        }
        else if (!preferDepotDownloader)
        {
            log.Write("[Vanilla] DepotDownloader wyłączony w ustawieniach — używam paczki 7z.");
        }

        return await AcquireViaFallback7zAsync(
            normalizedVersion,
            storageVersion,
            vanillaRoot,
            extractedPath,
            targetDirectory,
            manifest,
            progress,
            log,
            userCallbacks,
            ct);
    }

    private async Task<VanillaAcquireResult> TryDepotDownloaderAsync(
        string amongVersion,
        string storageVersion,
        string extractedPath,
        string targetDirectory,
        SteamManifestInfo manifest,
        IProgressReporter progress,
        IDiagnosticsOutput log,
        ModManagerUserCallbacks userCallbacks,
        CancellationToken ct)
    {
        var useQr = !_depotDownloader.HasSavedCredentials();

        if (useQr)
        {
            if (userCallbacks.RunSteamQrDownloadAsync is null)
            {
                log.Write("[Vanilla] Brak obsługi QR auth w UI — fallback do 7z.");
                return VanillaAcquireResult.Fail("fallback_7z");
            }

            progress.Report(12, "Logowanie Steam (QR)...");
            var qrContext = new SteamQrDownloadContext
            {
                ExtractedCachePath = extractedPath,
                ManifestId = manifest.ManifestId,
                AmongVersion = amongVersion,
                Progress = progress,
                Log = log,
                OnDepotProgress = p =>
                {
                    if (p.Percent.HasValue)
                    {
                        var mapped = 15 + (int)(p.Percent.Value * 40 / 100);
                        progress.Report(mapped, $"Steam: {p.Percent.Value:0.#}% {p.LastFileName}");
                    }
                }
            };

            var qrOk = await userCallbacks.RunSteamQrDownloadAsync(qrContext);
            if (!qrOk)
            {
                if (userCallbacks.ConfirmAsync is not null)
                {
                    var useFallback = await userCallbacks.ConfirmAsync(
                        "Logowanie Steam zostało anulowane.\n\nCzy użyć kopii zapasowej SUSModder (paczka 7z)?",
                        "Fallback vanilla");
                    if (useFallback)
                        return VanillaAcquireResult.Fail("fallback_7z");
                }

                return VanillaAcquireResult.Fail("Anulowano logowanie Steam.");
            }

            if (!_cacheService.IsValidExtractedCache(extractedPath, manifest.ManifestId))
                return VanillaAcquireResult.Fail("Pobieranie ze Steam nie utworzyło cache vanilli.");

            _cacheService.WriteMarker(
                extractedPath, amongVersion, storageVersion,
                VanillaAcquireSource.DepotDownloader, manifest.ManifestId, manifest.BuildId);

            progress.Report(55, "Kopiowanie gry vanilla...");
            _cacheService.CopyExtractedToTarget(extractedPath, targetDirectory);
            return VanillaAcquireResult.Ok(VanillaAcquireSource.DepotDownloader);
        }

        try
        {
            progress.Report(12, "Pobieranie gry vanilla ze Steam...");
            _depotDownloader.OnProgress = p =>
            {
                if (p.Percent.HasValue)
                {
                    var mapped = 12 + (int)(p.Percent.Value * 43 / 100);
                    progress.Report(mapped, $"Steam: {p.Percent.Value:0.#}% {p.LastFileName}");
                }
            };

            await _depotDownloader.RunDownloadAsync(
                extractedPath,
                manifest.ManifestId,
                useQrAuth: false,
                log,
                ct);

            if (!_cacheService.IsValidExtractedCache(extractedPath, manifest.ManifestId))
                return VanillaAcquireResult.Fail("DepotDownloader zakończył się bez plików gry.");

            _cacheService.WriteMarker(
                extractedPath, amongVersion, storageVersion,
                VanillaAcquireSource.DepotDownloader, manifest.ManifestId, manifest.BuildId);

            progress.Report(55, "Kopiowanie gry vanilla...");
            _cacheService.CopyExtractedToTarget(extractedPath, targetDirectory);
            return VanillaAcquireResult.Ok(VanillaAcquireSource.DepotDownloader);
        }
        catch (Exception ex)
        {
            log.Write($"[Vanilla] DepotDownloader failed: {ex.Message}");

            if (userCallbacks.ConfirmAsync is null)
                return VanillaAcquireResult.Fail(ex.Message);

            var useFallback = await userCallbacks.ConfirmAsync(
                $"Nie udało się pobrać gry ze Steam:\n{ex.Message}\n\n" +
                "Czy użyć kopii zapasowej SUSModder (paczka 7z)?",
                "Fallback vanilla");

            return useFallback
                ? VanillaAcquireResult.Fail("fallback_7z")
                : VanillaAcquireResult.Fail(ex.Message);
        }
    }

    private async Task<VanillaAcquireResult> AcquireViaFallback7zAsync(
        string amongVersion,
        string storageVersion,
        string vanillaRoot,
        string extractedPath,
        string targetDirectory,
        SteamManifestInfo? manifest,
        IProgressReporter progress,
        IDiagnosticsOutput log,
        ModManagerUserCallbacks userCallbacks,
        CancellationToken ct)
    {
        var archivePath = VanillaCacheService.GetArchivePath(vanillaRoot, storageVersion);
        var baseUrl = _configuration.GetSection("Configuration")["BaseUrl"] ?? "https://susmodder.app/";
        var downloadUrl = $"{baseUrl}api/susmodder-download-version?version={storageVersion}";

        if (!File.Exists(archivePath))
        {
            progress.Report(10, "Pobieranie gry vanilla (fallback 7z)...");
            log.Write($"[Vanilla] Pobieram 7z: {downloadUrl}");

            var downloaded = await DownloadArchiveAsync(downloadUrl, archivePath, progress, log, userCallbacks, ct);
            if (!downloaded)
                return VanillaAcquireResult.Fail("Nie udało się pobrać paczki vanilla 7z.");
        }
        else
        {
            log.Write($"[Vanilla] Używam cache archiwum: {archivePath}");
        }

        if (!File.Exists(archivePath) || new FileInfo(archivePath).Length < 1000)
            return VanillaAcquireResult.Fail("Paczka vanilla 7z jest nieprawidłowa lub pusta.");

        if (_cacheService.IsValidExtractedCache(extractedPath, manifest?.ManifestId))
        {
            log.Write($"[Vanilla] Używam rozpakowanego cache: {extractedPath}");
            _cacheService.TryDeleteArchiveWhenExtractedCacheValid(vanillaRoot, storageVersion, log.Write);
            progress.Report(55, "Kopiowanie gry vanilla z cache...");
            _cacheService.CopyExtractedToTarget(extractedPath, targetDirectory);
            return VanillaAcquireResult.Ok(VanillaAcquireSource.Fallback7zCache);
        }

        try
        {
            if (Directory.Exists(extractedPath))
                Directory.Delete(extractedPath, recursive: true);

            Directory.CreateDirectory(extractedPath);
            progress.Report(50, "Rozpakowywanie gry vanilla (7z)...");

            var password = SecretProvider.Get7zPassword();
            var extractor = new SharpCompressExtractor();

            var extractionProgress = new Progress<ExtractionProgress>(p =>
            {
                int pct = p.PercentComplete.HasValue
                    ? (int)p.PercentComplete.Value
                    : p.TotalBytes > 0
                        ? (int)(p.BytesExtracted * 100 / p.TotalBytes)
                        : 0;

                progress.Report(50 + pct * 5 / 100, $"Rozpakowywanie vanilla: {pct}%");
            });

            await extractor.ExtractAsync(archivePath, extractedPath, password, extractionProgress);

            _cacheService.WriteMarker(
                extractedPath,
                amongVersion,
                storageVersion,
                VanillaAcquireSource.Fallback7z,
                manifest?.ManifestId,
                manifest?.BuildId);

            _cacheService.TryDeleteArchiveWhenExtractedCacheValid(vanillaRoot, storageVersion, log.Write);

            progress.Report(55, "Kopiowanie gry vanilla...");
            _cacheService.CopyExtractedToTarget(extractedPath, targetDirectory);

            return VanillaAcquireResult.Ok(VanillaAcquireSource.Fallback7z);
        }
        catch (Exception ex)
        {
            log.Write($"[Vanilla] Błąd rozpakowywania 7z: {ex.Message}");

            try
            {
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
            }
            catch { }

            if (userCallbacks.ConfirmAsync is null)
                return VanillaAcquireResult.Fail(ex.Message);

            var retry = await userCallbacks.ConfirmAsync(
                $"Błąd rozpakowywania vanilla 7z:\n{ex.Message}\n\nSpróbować ponownie?",
                "Błąd rozpakowywania");

            if (!retry)
                return VanillaAcquireResult.Fail(ex.Message);

            return await AcquireViaFallback7zAsync(
                amongVersion, storageVersion, vanillaRoot, extractedPath, targetDirectory,
                manifest, progress, log, userCallbacks, ct);
        }
    }

    private static async Task<bool> DownloadArchiveAsync(
        string url,
        string filePath,
        IProgressReporter progress,
        IDiagnosticsOutput log,
        ModManagerUserCallbacks userCallbacks,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", SecretProvider.GetDownloadToken());

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        var pct = (int)(downloadedBytes * 100 / totalBytes);
                        progress.Report(10 + pct * 35 / 100, "Pobieranie vanilla (7z)...");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                log.Write($"[Vanilla] Błąd pobierania 7z: {ex.Message}");

                if (userCallbacks.ConfirmAsync is null)
                    return false;

                var retry = await userCallbacks.ConfirmAsync(
                    "Wystąpił błąd podczas pobierania pliku vanilla. Czy chcesz spróbować ponownie?",
                    "Błąd pobierania");

                if (!retry)
                    return false;
            }
        }
    }
}
