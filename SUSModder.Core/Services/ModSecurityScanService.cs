using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Services;

/// <summary>
/// Wynik raportu bezpieczeństwa dla wariantu moda – mapowany z DTO API na własny model,
/// żeby warstwa UI nie zależała od kontraktu transportowego.
/// </summary>
public sealed class ModSecurityScanResult
{
    /// <summary>
    /// Czy udało się pobrać raport (false = błąd sieci/API, timeout, brak wariantu).
    /// </summary>
    public bool ReportFetched { get; init; }

    /// <summary>
    /// Oryginalny, kanoniczny status skanu z backendu.
    /// Wartości: clean / suspicious / malicious / pending / scanning / error / unknown.
    /// </summary>
    public string ScanStatus { get; init; } = VirusTotalScanStatus.Unknown;

    /// <summary>
    /// Czy mod jest uznany za ryzykowny (suspicious / malicious).
    /// </summary>
    public bool IsRisky => VirusTotalScanStatus.IsRisky(ScanStatus);

    /// <summary>
    /// Czy skan potwierdził czystość moda.
    /// </summary>
    public bool IsClean => VirusTotalScanStatus.IsClean(ScanStatus);

    /// <summary>
    /// Czy raport nie został jeszcze pobrany lub nie ma danych (pending/scanning/error/unknown/null).
    /// </summary>
    public bool IsUnknown => VirusTotalScanStatus.IsUnknownLike(ScanStatus);

    /// <summary>
    /// Czy AI review sugeruje false positive (łagodniejsze ostrzeżenie).
    /// </summary>
    public bool IsFalsePositiveLikely { get; init; }

    /// <summary>
    /// Czy AI review potwierdza realne ryzyko.
    /// </summary>
    public bool IsRiskConfirmed { get; init; }

    /// <summary>
    /// Status ostrzeżenia – determinuje, który komunikat wyświetlić w UI.
    /// </summary>
    public VirusTotalWarningLevel WarningLevel { get; init; }

    public int MaliciousCount { get; init; }
    public int SuspiciousCount { get; init; }
    public int UndetectedCount { get; init; }
    public int HarmlessCount { get; init; }

    public string? VtPermalink { get; init; }
    public string? VtLastAnalysisDate { get; init; }
    public string? VtLastCheckedAt { get; init; }
    public string? AiReviewStatus { get; init; }
    public string? AiReviewSummary { get; init; }
    public string? Sha256 { get; init; }
}

/// <summary>
/// Poziom ostrzeżenia bezpieczeństwa dla UI.
/// </summary>
public enum VirusTotalWarningLevel
{
    /// <summary>Brak ostrzeżenia – mod czysty.</summary>
    None,

    /// <summary>Łagodne ostrzeżenie – suspicious + AI review false_positive_likely.</summary>
    Mild,

    /// <summary>Normalne ostrzeżenie – suspicious bez potwierdzenia false positive.</summary>
    Warning,

    /// <summary>Wysokie ryzyko – malicious (bez potwierdzenia lub inconclusive).</summary>
    High,

    /// <summary>Najwyższe ryzyko – malicious + AI review risk_confirmed.</summary>
    Critical
}

/// <summary>
/// Serwis pobierający raporty VirusTotal dla wariantów modów.
/// Cache'uje wyniki w pamięci na 10 minut. Best-effort – błędy nie blokują instalacji.
/// </summary>
public sealed class ModSecurityScanService : IDisposable
{
    private static readonly MemoryCache Cache = new(new MemoryCacheOptions());
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly ISUSModderApiClient _apiClient;
    private readonly IDiagnosticsOutput _log;
    private readonly Data.IModRepository? _modRepository;
    private readonly object _cacheLock = new();

    public ModSecurityScanService(
        ISUSModderApiClient apiClient,
        IDiagnosticsOutput log,
        Data.IModRepository? modRepository = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _modRepository = modRepository;
    }

    /// <summary>
    /// Pobiera raport VT dla wariantu moda. Wynik jest cache'owany na 10 minut.
    /// Nigdy nie rzuca wyjątkiem – błędy sieci/API zwracają <see cref="ModSecurityScanResult.ReportFetched"/> = false.
    /// </summary>
    public async Task<ModSecurityScanResult> GetReportAsync(
        int modId,
        string version,
        string platform,
        CancellationToken cancellationToken = default)
    {
        var arch = ResolveArchitecture(platform);
        return await GetReportAsync(modId, version, platform, arch, cancellationToken);
    }

    /// <summary>
    /// Pobiera raport VT dla wariantu moda z jawną architekturą.
    /// </summary>
    public async Task<ModSecurityScanResult> GetReportAsync(
        int modId,
        string version,
        string platform,
        string arch,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"vt_{modId}_{version}_{platform}_{arch}";

        // Próbuj z cache
        if (Cache.TryGetValue(cacheKey, out ModSecurityScanResult? cached) && cached is not null)
            return cached;

        SusModderApiResult<ModVariantVirusTotalReportDto> apiResult;
        try
        {
            apiResult = await _apiClient.GetModVariantVirusTotalReportAsync(
                modId, version, platform, arch, cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Write($"[ModSecurityScan] Błąd pobierania raportu VT dla mod {modId} v{version}: {ex.Message}");
            var fallback = new ModSecurityScanResult
            {
                ReportFetched = false,
                ScanStatus = VirusTotalScanStatus.Unknown
            };
            Cache.Set(cacheKey, fallback, CacheTtl);
            return fallback;
        }

        if (!apiResult.IsSuccess || apiResult.Data is null)
        {
            var message = apiResult.Error?.Message ?? "Brak danych";
            _log.Write($"[ModSecurityScan] Raport VT niedostępny dla mod {modId} v{version}: {message}");

            var fallback = new ModSecurityScanResult
            {
                ReportFetched = false,
                ScanStatus = VirusTotalScanStatus.Unknown
            };
            Cache.Set(cacheKey, fallback, CacheTtl);
            return fallback;
        }

        var result = MapFromDto(apiResult.Data);
        Cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    /// <summary>
    /// Pobiera raporty VT dla wielu modów równolegle. Używane przy bulk install.
    /// Błędy pojedynczych modów nie przerywają całej paczki.
    /// </summary>
    public async Task<IReadOnlyList<ModSecurityScanResult>> GetReportsForBatchAsync(
        IReadOnlyList<(int ModId, string Version, string Platform)> mods,
        CancellationToken cancellationToken = default)
    {
        if (mods.Count == 0)
            return Array.Empty<ModSecurityScanResult>();

        var tasks = mods.Select(m => GetReportAsync(m.ModId, m.Version, m.Platform, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results;
    }

    /// <summary>
    /// Określa architekturę wariantu na podstawie platformy:
    /// Steam → x86, Epic → x64.
    /// </summary>
    public static string ResolveArchitecture(string platform)
    {
        return platform.Equals("epic", StringComparison.OrdinalIgnoreCase)
            ? "x64"
            : "x86";
    }

    private static ModSecurityScanResult MapFromDto(ModVariantVirusTotalReportDto dto)
    {
        var status = NormalizeStatus(dto.ScanStatus);
        var isFalsePositive = VirusTotalAiReviewStatus.IsFalsePositiveLikely(dto.AiReviewStatus);
        var isRiskConfirmed = VirusTotalAiReviewStatus.IsRiskConfirmed(dto.AiReviewStatus);

        VirusTotalWarningLevel warningLevel;
        if (string.Equals(status, VirusTotalScanStatus.Malicious, StringComparison.OrdinalIgnoreCase))
        {
            warningLevel = isRiskConfirmed
                ? VirusTotalWarningLevel.Critical
                : VirusTotalWarningLevel.High;
        }
        else if (string.Equals(status, VirusTotalScanStatus.Suspicious, StringComparison.OrdinalIgnoreCase))
        {
            warningLevel = isFalsePositive
                ? VirusTotalWarningLevel.Mild
                : VirusTotalWarningLevel.Warning;
        }
        else
        {
            warningLevel = VirusTotalWarningLevel.None;
        }

        var stats = dto.LastAnalysisStats;

        return new ModSecurityScanResult
        {
            ReportFetched = true,
            ScanStatus = status,
            WarningLevel = warningLevel,
            IsFalsePositiveLikely = isFalsePositive,
            IsRiskConfirmed = isRiskConfirmed,
            MaliciousCount = stats?.Malicious ?? 0,
            SuspiciousCount = stats?.Suspicious ?? 0,
            UndetectedCount = stats?.Undetected ?? 0,
            HarmlessCount = stats?.Harmless ?? 0,
            VtPermalink = dto.VtPermalink,
            VtLastAnalysisDate = dto.VtLastAnalysisDate,
            VtLastCheckedAt = dto.VtLastCheckedAt,
            AiReviewStatus = dto.AiReviewStatus,
            AiReviewSummary = dto.AiReviewSummary,
            Sha256 = dto.Sha256
        };
    }

    private static string NormalizeStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return VirusTotalScanStatus.Unknown;

        var lower = raw.Trim().ToLowerInvariant();

        return lower switch
        {
            "clean" => VirusTotalScanStatus.Clean,
            "suspicious" => VirusTotalScanStatus.Suspicious,
            "malicious" => VirusTotalScanStatus.Malicious,
            "pending" => VirusTotalScanStatus.Pending,
            "scanning" => VirusTotalScanStatus.Scanning,
            "error" => VirusTotalScanStatus.Error,
            _ => VirusTotalScanStatus.Unknown
        };
    }

    public void Dispose()
    {
        // MemoryCache nie wymaga jawnego Dispose, ale interfejs tego oczekuje.
    }

    /// <summary>
    /// Pobiera raporty VT dla wszystkich pełnych modów (nie Vanilla, nie DLL) i zapisuje do DB.
    /// Używane po załadowaniu katalogu z API. Best-effort – błędy nie przerywają.
    /// </summary>
    public async Task FetchAndStoreVtForCatalogAsync(
        IReadOnlyList<(int ModId, string Version)> fullMods,
        string platform,
        CancellationToken cancellationToken = default)
    {
        if (fullMods.Count == 0 || _modRepository == null)
            return;

        var arch = ResolveArchitecture(platform);
        var mods = fullMods
            .Select(m => (m.ModId, m.Version, Platform: platform))
            .ToList();

        var results = await GetReportsForBatchAsync(mods, cancellationToken);

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var modId = fullMods[i].ModId;

            if (!result.ReportFetched)
            {
                // Oznacz jako sprawdzone (brak raportu)
                _modRepository.SaveModVirusTotalData(
                    modId,
                    scanStatus: VirusTotalScanStatus.Unknown,
                    permalink: null,
                    lastCheckedAt: DateTime.UtcNow.ToString("O"),
                    stats: null,
                    aiReviewStatus: null,
                    aiReviewSummary: null);
                continue;
            }

            string? statsJson = null;
            if (result.MaliciousCount > 0 || result.SuspiciousCount > 0 || result.UndetectedCount > 0)
            {
                statsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    malicious = result.MaliciousCount,
                    suspicious = result.SuspiciousCount,
                    undetected = result.UndetectedCount,
                    harmless = result.HarmlessCount
                });
            }

            _modRepository.SaveModVirusTotalData(
                modId,
                scanStatus: result.ScanStatus,
                permalink: result.VtPermalink,
                lastCheckedAt: DateTime.UtcNow.ToString("O"),
                stats: statsJson,
                aiReviewStatus: result.AiReviewStatus,
                aiReviewSummary: result.AiReviewSummary);
        }

        _log.Write($"[ModSecurityScan] Zapisano VT dla {results.Count(r => r.ReportFetched)}/{fullMods.Count} modów.");
    }
}
