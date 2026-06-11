using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SUSModder.Core.Diagnostics.Launch;

namespace SUSModder.Core.Diagnostics;

/// <summary>
/// Tworzy lokalny ZIP z raportem diagnostycznym do ręcznego dołączenia na Discordzie.
/// Raport zawiera: launch-report.json, redacted log excerpts, plugin snapshot, app/platform metadata.
/// </summary>
public sealed class SupportBundleService
{
    private const long MaxTotalSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxLogLines = 500;
    private const int MaxLogLineLength = 300;

    /// <summary>
    /// Lista wzorców do redakcji (ścieżki użytkownika, tokeny, emaile).
    /// </summary>
    private static readonly (string Pattern, string Replacement)[] RedactionRules =
    {
        (@"C:\\Users\\[^\\]+", @"C:\Users\<redacted>"),
        (@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", @"<email-redacted>"),
        (        @"[A-Za-z0-9_-]{20,30}\.[A-Za-z0-9_-]{5,8}\.[A-Za-z0-9_-]{20,40}", @"<discord-token-redacted>"),
        (@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", @"Bearer <redacted>"),
        (@"--token\s+\S+", @"--token <redacted>"),
    };

    /// <summary>
    /// Generuje support bundle ZIP w podanej lokalizacji.
    /// </summary>
    /// <param name="launchResult">Wynik diagnostyki launch.</param>
    /// <param name="outputDir">Katalog wyjściowy na ZIP.</param>
    /// <param name="anonymize">Czy anonimizować ścieżki użytkownika.</param>
    /// <returns>Ścieżka do wygenerowanego ZIP lub null jeśli błąd.</returns>
    public async Task<string?> GenerateBundleAsync(
        LaunchResult launchResult,
        string outputDir,
        bool anonymize = true)
    {
        var attempt = launchResult.Attempt;
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var zipName = $"SUSModder-Support-{attempt.ModName}-{timestamp}.zip";
        var zipPath = Path.Combine(outputDir, zipName);

        Directory.CreateDirectory(outputDir);

        try
        {
            using var fs = new FileStream(zipPath, FileMode.Create);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

            // 1. Raport JSON
            var report = BuildReportJson(launchResult, anonymize);
            await AddTextEntryAsync(archive, "launch-report.json", report);

            // 2. Redacted log excerpts
            if (launchResult.BepInExCriticalLines.Count > 0)
            {
                var logText = BuildLogExcerpt(launchResult.BepInExCriticalLines, anonymize);
                await AddTextEntryAsync(archive, "bepinex-excerpt.txt", logText);
            }

            // 3. Plugin snapshot jako CSV
            if (launchResult.PluginSnapshot.Count > 0)
            {
                var csv = BuildPluginCsv(launchResult.PluginSnapshot);
                await AddTextEntryAsync(archive, "plugins-snapshot.csv", csv);
            }

            // 4. Metadata
            var metadata = BuildMetadataText();
            await AddTextEntryAsync(archive, "metadata.txt", metadata);

            attempt.SupportBundlePath = zipPath;
            launchResult.SupportBundleGenerated = true;

            return zipPath;
        }
        catch
        {
            // Cleanup partial file
            try { File.Delete(zipPath); } catch { }
            return null;
        }
    }

    private string BuildReportJson(LaunchResult result, bool anonymize)
    {
        var obj = new
        {
            attemptId = result.Attempt.AttemptId,
            modId = result.Attempt.ModId,
            modName = result.Attempt.ModName,
            modType = result.Attempt.ModType,
            platformMode = result.Attempt.PlatformMode,
            startedAtUtc = result.Attempt.StartedAtUtc.ToString("O"),
            processId = result.Attempt.ProcessId,
            exitCode = result.Attempt.ExitCode,
            exitedEarly = result.Attempt.ExitedWithinObservationWindow,
            elapsedMs = result.Attempt.ElapsedMs,
            bepInExLogStatus = result.Attempt.BepInExLogStatus.ToString(),
            isSuccessful = result.IsSuccessful,
            severity = result.Severity.ToString(),
            diagnosisCodes = result.DiagnosisCodes,
            pluginCount = result.PluginSnapshot.Count,
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            installPath = anonymize ? RedactPath(result.Attempt.InstallPath) : result.Attempt.InstallPath
        };

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    private string BuildLogExcerpt(List<string> lines, bool anonymize)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== BepInEx Critical Lines Excerpt ===");
        sb.AppendLine($"Total lines: {lines.Count} (max {MaxLogLines} shown)");
        sb.AppendLine();

        int count = 0;
        foreach (var line in lines)
        {
            if (count >= MaxLogLines) break;

            var sanitized = anonymize ? RedactLine(line) : line;
            if (sanitized.Length > MaxLogLineLength)
                sanitized = sanitized[..MaxLogLineLength] + "...";

            sb.AppendLine(sanitized);
            count++;
        }

        return sb.ToString();
    }

    private string BuildPluginCsv(List<PluginFileSnapshot> plugins)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FileName,SizeBytes,LastWriteUtc");
        foreach (var p in plugins)
        {
            sb.AppendLine($"{p.FileName},{p.SizeBytes},{p.LastWriteUtc:O}");
        }
        return sb.ToString();
    }

    private string BuildMetadataText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SUSModder Support Bundle ===");
        sb.AppendLine($"App: SUSModder");
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"Platform: Windows x64");
        sb.AppendLine();
        sb.AppendLine("This bundle contains redacted diagnostic data.");
        sb.AppendLine("No tokens, passwords, or full user paths are included.");
        sb.AppendLine("Please attach this ZIP on Discord for further analysis.");
        return sb.ToString();
    }

    private static async Task AddTextEntryAsync(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        await writer.WriteAsync(content);
    }

    /// <summary>
    /// Redaguje ścieżkę użytkownika.
    /// </summary>
    public static string RedactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var result = path;
        foreach (var (pattern, replacement) in RedactionRules)
        {
            result = System.Text.RegularExpressions.Regex.Replace(result, pattern, replacement);
        }
        return result;
    }

    /// <summary>
    /// Redaguje pojedynczą linię (ścieżki, tokeny, emaile).
    /// </summary>
    public static string RedactLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return line;

        var result = line;
        foreach (var (pattern, replacement) in RedactionRules)
        {
            result = System.Text.RegularExpressions.Regex.Replace(result, pattern, replacement);
        }
        return result;
    }

    /// <summary>
    /// Oblicza SHA256 pliku.
    /// </summary>
    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }
}
