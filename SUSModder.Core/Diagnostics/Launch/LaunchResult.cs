using System;
using System.Collections.Generic;

namespace SUSModder.Core.Diagnostics.Launch;

/// <summary>
/// Wynik analizy uruchomienia moda po zakończeniu obserwacji.
/// Zawiera sklasyfikowane kody diagnozy, severity i podsumowanie dla UI.
/// </summary>
public sealed class LaunchResult
{
    /// <summary>Referencja do oryginalnej próby.</summary>
    public LaunchAttempt Attempt { get; init; } = null!;

    /// <summary>Czy launch zakończył się powodzeniem (brak krytycznych sygnałów).</summary>
    public bool IsSuccessful { get; set; }

    /// <summary>Stabilne kody diagnozy – UI mapuje je na zlokalizowane teksty.</summary>
    public List<string> DiagnosisCodes { get; set; } = [];

    /// <summary>Najwyższa wykryta severity.</summary>
    public DiagnosisSeverity Severity { get; set; } = DiagnosisSeverity.Unknown;

    /// <summary>Techniczny fallback message (EN) – tylko gdy UI nie ma mapowania.</summary>
    public string TechnicalSummary { get; set; } = string.Empty;

    /// <summary>Wycinki istotnych linii z BepInEx logów (już po redakcji, limitowane).</summary>
    public List<string> BepInExCriticalLines { get; set; } = [];

    /// <summary>Lista plików w BepInEx\plugins (nazwa + rozmiar, do porównania z manifestem).</summary>
    public List<PluginFileSnapshot> PluginSnapshot { get; set; } = [];

    /// <summary>Czy support bundle został wygenerowany.</summary>
    public bool SupportBundleGenerated { get; set; }
}

public enum DiagnosisSeverity
{
    Unknown = 0,
    Info = 1,
    Warning = 2,
    Critical = 3
}

/// <summary>
/// Snapshot pliku w BepInEx\plugins.
/// </summary>
public sealed class PluginFileSnapshot
{
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset LastWriteUtc { get; set; }
}
