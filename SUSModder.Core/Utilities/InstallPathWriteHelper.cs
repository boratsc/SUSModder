using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services;

namespace SUSModder.Core.Utilities;

/// <summary>
/// Przygotowanie katalogów instalacji/rozpakowania i obsługa błędów zapisu (AV, ReadOnly, blokady).
/// </summary>
public static class InstallPathWriteHelper
{
    public static bool IsAccessDeniedException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException)
                return true;

            if (current.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("Access to the path", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string? TryGetBlockedPathFromException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var path = TryParseBlockedPath(current.Message);
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        return null;
    }

    public static void ClearWriteBlockersForPath(string? path, IDiagnosticsOutput? log = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                    log?.Write($"[Extract] Usunięto ReadOnly: {path}");
                }
            }

            var directory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                FileSystemUtilities.ClearReadOnlyAttributesRecursive(directory, log);
        }
        catch (Exception ex)
        {
            log?.Write($"[Extract] Nie udało się przygotować ścieżki do zapisu ({path}): {ex.Message}");
        }
    }

    public static bool TryVerifyWriteAccess(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);

            var probeFile = Path.Combine(
                directoryPath,
                $".susmodder-write-test-{Guid.NewGuid():N}.tmp");

            File.WriteAllText(probeFile, "ok");
            File.Delete(probeFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task PrepareExtractionDirectoryAsync(
        string extractPath,
        IDiagnosticsOutput? log = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (Directory.Exists(extractPath))
        {
            log?.Write($"[Extract] Czyszczenie katalogu roboczego: {extractPath}");
            await FileSystemUtilities.SafeDeleteDirectoryAsync(extractPath, "extractMod", log);
        }

        Directory.CreateDirectory(extractPath);
        FileSystemUtilities.ClearReadOnlyAttributesRecursive(extractPath, log);

        if (!TryVerifyWriteAccess(extractPath))
        {
            throw new UnauthorizedAccessException(
                $"Brak uprawnień zapisu w katalogu roboczym instalacji: {extractPath}");
        }
    }

    public static void RunWithWriteRetry(
        Action action,
        string extractPath,
        string? entryKey = null,
        IDiagnosticsOutput? log = null)
    {
        const int maxAttempts = 3;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsAccessDeniedException(ex))
            {
                lastException = ex;
                var blockedPath = TryGetBlockedPathFromException(ex)
                    ?? (string.IsNullOrWhiteSpace(entryKey) ? null : Path.Combine(extractPath, entryKey));

                log?.Write(
                    $"[Extract] Access denied (próba {attempt}/{maxAttempts}){(blockedPath != null ? $": {blockedPath}" : string.Empty)}");

                if (!string.IsNullOrWhiteSpace(blockedPath))
                    ClearWriteBlockersForPath(blockedPath, log);
                else
                    FileSystemUtilities.ClearReadOnlyAttributesRecursive(extractPath, log);

                Thread.Sleep(350 * attempt);
            }
        }

        if (lastException != null)
            throw lastException;

        action();
    }

    public static string FormatExtractionAccessDeniedMessage(
        string? blockedPath,
        string modsInstallPath,
        IReadOnlyList<string>? detectedAntivirusProducts = null)
    {
        var fileHint = string.IsNullOrWhiteSpace(blockedPath)
            ? "mod file"
            : Path.GetFileName(blockedPath);

        var antivirusProducts = detectedAntivirusProducts
            ?? AntivirusDetectionService.LastDetectionResult?.ProductNames
            ?? Array.Empty<string>();

        var antivirusHint = antivirusProducts.Count > 0
            ? $"• Add the mods installation folder to your antivirus exclusions ({string.Join(", ", antivirusProducts)}):\n  {modsInstallPath}"
            : "• Add the mods installation folder to your antivirus exclusions";

        var message =
            "Failed to extract the mod archive — write access was denied for "
            + $"{fileHint}.\n\n"
            + "Try:\n"
            + "• Close Among Us and restart SUSModder\n"
            + $"• Delete the temp folder at: {modsInstallPath}\n"
            + antivirusHint + "\n"
            + "• Check Windows Ransomware Protection (Controlled Folder Access)";

        if (!string.IsNullOrWhiteSpace(blockedPath))
            message += $"\n\nFile: {blockedPath}";

        return message;
    }

    private static string? TryParseBlockedPath(string message)
    {
        const string prefix = "Access to the path '";
        var start = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += prefix.Length;
        var end = message.IndexOf('\'', start);
        if (end < 0)
            return null;

        return message.Substring(start, end - start);
    }
}
