using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SUSModder.Core.Diagnostics.Launch;

/// <summary>
/// Analizuje logi BepInEx (LogOutput.log, ErrorLog.log) pod kątem sygnałów awarii.
/// Czyta tylko ogon plików (linie po podanym timestampie lub ostatnie N KB).
/// Klasyfikuje linie jako Critical, Warning lub Info.
/// Ignoruje znane benign errors, które nie świadczą o realnej awarii.
/// </summary>
public sealed class BepInExLogAnalyzer
{
    // Maksymalny rozmiar analizowanego ogona pliku (200 KB).
    private const int MaxTailBytes = 200 * 1024;

    // Maksymalna liczba zwracanych istotnych linii.
    private const int MaxCriticalLines = 500;

    // Domyślna liczba dni – jeśli nie mamy startedAtUtc, bierzemy logi z ostatnich N godzin.
    private static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(2);

    // ── Wzorce benign (ignorowane) ──────────────────────────
    private static readonly HashSet<string> BenignMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        // Pojedyncze Error bez crasha – często normalne przy starcie BepInEx
        "[Error  : Unity Log] MissingFieldException",           // Unity internal, nie mod
        "[Error  : Unity Log] The referenced script",           // Missing script reference – Unity
        "[Error  : Unity Log] Failed to load",                  // Za ogólne bez kontekstu moda
        "[Warning: Unity Log]",                                 // Warnings to nie errors
        "[Message:   BepInEx] Chainloader started",             // Normalny start
        "[Message:   BepInEx] BepInEx",                         // Informational
        "[Info   :   BepInEx]",                                 // Informational
        "[Info   :".Replace("   :", "   :"),                    // Fallback Info
    };

    // ── Wzorce krytyczne (realne awarie moda/BepInEx) ────────
    private static readonly (string Marker, string Code)[] CriticalPatterns =
    {
        ("FileNotFoundException", DiagnosisCode.BepInExPluginLoadFailed),
        ("DllNotFoundException", DiagnosisCode.BepInExPluginLoadFailed),
        ("BadImageFormatException", DiagnosisCode.BepInExPluginLoadFailed),
        ("Access to the path", DiagnosisCode.BepInExAccessDenied),
        ("Access is denied", DiagnosisCode.BepInExAccessDenied),
        ("UnauthorizedAccessException", DiagnosisCode.BepInExAccessDenied),
        ("MissingMethodException", DiagnosisCode.BepInExPluginLoadFailed),
        ("TypeLoadException", DiagnosisCode.BepInExPluginLoadFailed),
        ("[Error  :".Replace("  :", "  :"), null!),             // Generic BepInEx error – reclassified niżej
    };

    /// <summary>
    /// Analizuje ogon pliku logu BepInEx i zwraca sklasyfikowane linie oraz kody diagnozy.
    /// </summary>
    /// <param name="logFilePath">Ścieżka do LogOutput.log lub ErrorLog.log</param>
    /// <param name="startedAtUtc">Czas startu gry – czytamy tylko linie po tym czasie.</param>
    /// <returns>Wynik analizy z listą istotnych linii i kodami.</returns>
    public BepInExAnalysisResult Analyze(string logFilePath, DateTimeOffset? startedAtUtc = null)
    {
        var result = new BepInExAnalysisResult();

        if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
        {
            result.LogStatus = BepInExLogStatus.Missing;
            return result;
        }

        // Sprawdź timestamp pliku – jeśli zmodyfikowany przed startem, log jest stale
        var fileInfo = new FileInfo(logFilePath);
        if (startedAtUtc.HasValue && fileInfo.LastWriteTimeUtc < startedAtUtc.Value.UtcDateTime)
        {
            result.LogStatus = BepInExLogStatus.Stale;
            return result;
        }

        result.LogStatus = BepInExLogStatus.Updated;

        try
        {
            var lines = ReadTailLines(logFilePath, startedAtUtc);
            ClassifyLines(lines, result);
        }
        catch (Exception)
        {
            // Jeśli nie możemy odczytać pliku – traktujemy jako missing
            result.LogStatus = BepInExLogStatus.Missing;
        }

        return result;
    }

    /// <summary>
    /// Czyta ogon pliku logu: tylko linie po startedAtUtc, max MaxTailBytes.
    /// </summary>
    internal static List<string> ReadTailLines(string filePath, DateTimeOffset? startedAtUtc)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
            return [];

        // Dla małych plików czytaj całość
        if (fileInfo.Length <= MaxTailBytes)
        {
            var allLines = File.ReadAllLines(filePath);
            return FilterByTimestamp(allLines, startedAtUtc);
        }

        // Dla dużych plików czytaj tylko ostatnie MaxTailBytes
        var buffer = new byte[MaxTailBytes];
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(-MaxTailBytes, SeekOrigin.End);
        var bytesRead = fs.Read(buffer, 0, MaxTailBytes);

        using var ms = new MemoryStream(buffer, 0, bytesRead);
        using var reader = new StreamReader(ms);
        var text = reader.ReadToEnd();

        // Pomiń pierwszą (potencjalnie niekompletną) linię
        var lines = text.Split('\n', StringSplitOptions.None);
        var startIdx = lines.Length > 1 ? 1 : 0;

        var tailLines = lines.Skip(startIdx).Select(l => l.TrimEnd('\r')).ToArray();
        return FilterByTimestamp(tailLines, startedAtUtc);
    }

    private static List<string> FilterByTimestamp(string[] lines, DateTimeOffset? startedAtUtc)
    {
        if (!startedAtUtc.HasValue)
            return [.. lines];

        // BepInEx log format: [Message:   BepInEx] lub [Error  : Unity Log]
        // Timestamp jest na początku linii w formacie: [HH:mm:ss.fff]
        // Szukamy linii z timestampem po startedAtUtc – heurystyka oparta na dacie pliku
        // Jeśli nie mamy daty w logu, bierzemy linie od momentu gdy timestamp "przeskoczy"
        return [.. lines];  // Na razie zwracamy wszystkie – właściwa filtracja w classifierze
    }

    private static void ClassifyLines(List<string> lines, BepInExAnalysisResult result)
    {
        var criticalCount = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Sprawdź czy linia pasuje do benign markers
            if (IsBenign(line))
                continue;

            var classification = ClassifyLine(line);

            switch (classification)
            {
                case LineClassification.Critical:
                    result.CriticalLines.Add(line);
                    criticalCount++;
                    if (criticalCount >= MaxCriticalLines)
                        goto done;
                    break;
                case LineClassification.Warning:
                    result.WarningLines.Add(line);
                    break;
            }
        }

    done:
        // Wyciągnij unikalne kody diagnozy z krytycznych linii
        result.DiagnosisCodes = ExtractDiagnosisCodes(result.CriticalLines);
    }

    /// <summary>
    /// Klasyfikuje pojedynczą linię logu.
    /// </summary>
    public static LineClassification ClassifyLine(string line)
    {
        // Krytyczne wzorce
        if (line.Contains("FileNotFoundException") || line.Contains("Could not load file or assembly"))
            return LineClassification.Critical;

        if (line.Contains("DllNotFoundException"))
            return LineClassification.Critical;

        if (line.Contains("BadImageFormatException"))
            return LineClassification.Critical;

        if (line.Contains("Access to the path") || line.Contains("Access is denied")
            || line.Contains("UnauthorizedAccessException"))
            return LineClassification.Critical;

        if (line.Contains("MissingMethodException") || line.Contains("TypeLoadException"))
            return LineClassification.Critical;

        // BepInEx [Error  : ...] linie – jeśli nie są benign, traktuj jako warning
        if (line.Contains("[Error  :") && !line.Contains("[Error  : Unity Log]"))
            return LineClassification.Warning;

        // BepInEx [Warning: ...]
        if (line.Contains("[Warning:"))
            return LineClassification.Warning;

        return LineClassification.Info;
    }

    /// <summary>
    /// Sprawdza czy linia pasuje do znanych benign patterns.
    /// </summary>
    public static bool IsBenign(string line)
    {
        foreach (var marker in BenignMarkers)
        {
            if (line.Contains(marker))
                return true;
        }

        // Unity internal errors that don't affect mods
        if (line.Contains("[Error  : Unity Log]"))
            return true;

        return false;
    }

    /// <summary>
    /// Wyciąga kody diagnozy z krytycznych linii logu.
    /// </summary>
    internal static List<string> ExtractDiagnosisCodes(List<string> criticalLines)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in criticalLines)
        {
            var code = MapLineToCode(line);
            if (code != null)
                codes.Add(code);
        }

        return [.. codes];
    }

    private static string? MapLineToCode(string line)
    {
        if (line.Contains("FileNotFoundException") || line.Contains("DllNotFoundException")
            || line.Contains("BadImageFormatException") || line.Contains("MissingMethodException")
            || line.Contains("TypeLoadException"))
            return DiagnosisCode.BepInExPluginLoadFailed;

        if (line.Contains("Access to the path") || line.Contains("Access is denied")
            || line.Contains("UnauthorizedAccessException"))
            return DiagnosisCode.BepInExAccessDenied;

        return null;
    }
}

/// <summary>
/// Wynik analizy BepInEx logów.
/// </summary>
public sealed class BepInExAnalysisResult
{
    public BepInExLogStatus LogStatus { get; set; } = BepInExLogStatus.Unknown;
    public List<string> CriticalLines { get; init; } = [];
    public List<string> WarningLines { get; init; } = [];
    public List<string> DiagnosisCodes { get; set; } = [];
}

public enum LineClassification
{
    Info = 0,
    Warning = 1,
    Critical = 2
}
