using System;
using System.Collections.Generic;
using System.Linq;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services;

/// <summary>
/// Wybór wpisu kompatybilności z API — tylko aktualna wersja moda (IsCurrentVersion).
/// Nie podbija W→F na podstawie starszych wpisów w macierzy.
/// </summary>
public static class CompatibilityMerger
{
    public static CompatibilityInfo FromEntry(CompatibilityEntry entry) =>
        new()
        {
            Id = entry.Id,
            StatusCode = NormalizeStatusCode(entry.Status),
            TestedDate = string.IsNullOrEmpty(entry.TestedDate) || !DateTime.TryParse(entry.TestedDate, out var date)
                ? null
                : date,
            TestedBy = entry.TestedBy,
            AmongUsVersion = entry.AmongUsVersion,
            Notes = entry.Notes,
            IssuesUrl = entry.IssuesUrl,
            IsCurrentVersion = entry.IsCurrentVersion,
            Warning = entry.Warning
        };

    /// <summary>
    /// Wybiera wpis dla jednej pary DLL↔FULL z listy zwróconej przez API.
    /// </summary>
    public static CompatibilityInfo? PickBestFromEntries(IEnumerable<CompatibilityEntry>? entries)
    {
        if (entries == null)
            return null;

        return PickBestFromInfos(entries.Select(FromEntry));
    }

    public static CompatibilityInfo? PickBestFromInfos(IEnumerable<CompatibilityInfo> entries)
    {
        var list = entries.ToList();
        if (list.Count == 0)
            return null;

        var current = list.Where(e => e.IsCurrentVersion).ToList();
        if (current.Count == 0)
        {
            // Brak wpisu dla bieżącej wersji — nie pokazuj historycznego F/W jako aktualnego statusu.
            return null;
        }

        return current
            .OrderByDescending(e => e.TestedDate ?? DateTime.MinValue)
            .ThenBy(e => e.Id)
            .First();
    }

    public static Dictionary<int, CompatibilityInfo> BuildMatrixByFullModId(IEnumerable<CompatibilityEntry> entries)
    {
        var result = new Dictionary<int, CompatibilityInfo>();
        foreach (var group in entries.Where(e => e.FullMod != null).GroupBy(e => e.FullMod!.Id))
        {
            var picked = PickBestFromEntries(group);
            if (picked != null)
                result[group.Key] = picked;
        }

        return result;
    }

    public static Dictionary<int, CompatibilityInfo> BuildMatrixByDllModId(IEnumerable<CompatibilityEntry> entries)
    {
        var result = new Dictionary<int, CompatibilityInfo>();
        foreach (var group in entries.Where(e => e.DllMod != null).GroupBy(e => e.DllMod!.Id))
        {
            var picked = PickBestFromEntries(group);
            if (picked != null)
                result[group.Key] = picked;
        }

        return result;
    }

    internal static string NormalizeStatusCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "NT";

        var normalized = code.Trim().ToUpperInvariant();
        return normalized switch
        {
            "F" or "FAVORITE" => "F",
            "W" or "WORKS" => "W",
            "NW" or "NOTWORK" or "NOT_WORK" => "NW",
            "NT" or "NOTTESTED" or "NOT_TESTED" => "NT",
            _ => normalized.Length <= 3 ? normalized : "NT"
        };
    }
}
