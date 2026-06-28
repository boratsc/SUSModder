using System;

namespace SUSModder.Core.Diagnostics.Launch;

/// <summary>
/// Reprezentuje pojedynczą próbę uruchomienia moda.
/// Tworzony przed startem procesu, aktualizowany po zakończeniu obserwacji.
/// </summary>
public sealed class LaunchAttempt
{
    /// <summary>Unikalny identyfikator próby (GUID).</summary>
    public string AttemptId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>ID moda z katalogu.</summary>
    public int ModId { get; set; }

    /// <summary>Nazwa moda.</summary>
    public string ModName { get; set; } = string.Empty;

    /// <summary>Typ moda (full, dll, Vanilla).</summary>
    public string ModType { get; set; } = string.Empty;

    /// <summary>Platforma gry (steam, epic).</summary>
    public string PlatformMode { get; set; } = string.Empty;

    /// <summary>Ścieżka instalacji moda (wg configu).</summary>
    public string? InstallPath { get; set; }

    /// <summary>Ścieżka do Among Us.exe, który będzie uruchamiany.</summary>
    public string? ExePath { get; set; }

    /// <summary>Czas rozpoczęcia próby (UTC).</summary>
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>ID procesu gry, jeśli udało się wystartować (nullable).</summary>
    public int? ProcessId { get; set; }

    /// <summary>Kod wyjścia procesu, jeśli się zakończył (nullable).</summary>
    public int? ExitCode { get; set; }

    /// <summary>Czy proces zakończył się w ciągu pierwszych N sekund (domyślnie 60).</summary>
    public bool ExitedWithinObservationWindow { get; set; }

    /// <summary>Ile milisekund proces działał zanim wyszedł (lub 0 jeśli nadal działa).</summary>
    public long ElapsedMs { get; set; }

    /// <summary>Status logu BepInEx po próbie.</summary>
    public BepInExLogStatus BepInExLogStatus { get; set; } = BepInExLogStatus.Unknown;

    /// <summary>Lokalna ścieżka do support bundle ZIP, jeśli utworzono (nullable).</summary>
    public string? SupportBundlePath { get; set; }
}

/// <summary>
/// Status logu BepInEx po próbie uruchomienia.
/// </summary>
public enum BepInExLogStatus
{
    /// <summary>Jeszcze nie sprawdzono.</summary>
    Unknown = 0,

    /// <summary>LogOutput.log nie istnieje.</summary>
    Missing = 1,

    /// <summary>Log istnieje, ale nie został zaktualizowany od startu gry.</summary>
    Stale = 2,

    /// <summary>Log został zaktualizowany po starcie – BepInEx się załadował.</summary>
    Updated = 3
}
