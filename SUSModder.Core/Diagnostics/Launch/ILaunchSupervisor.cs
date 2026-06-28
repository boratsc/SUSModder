using System;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Core.Diagnostics.Launch;

/// <summary>
/// Nadzoruje uruchamianie gry (Steam lub Epic), obserwuje proces i zbiera diagnostykę.
/// Implementacja w warstwie Core lub jako adapter platformowy.
/// </summary>
public interface ILaunchSupervisor
{
    /// <summary>
    /// Rozpoczyna nadzorowane uruchomienie gry.
    /// Tworzy LaunchAttempt, startuje proces, obserwuje przez observationWindow,
    /// zbiera logi BepInEx i zwraca LaunchResult z klasyfikacją.
    /// </summary>
    /// <param name="launchContext">Kontekst uruchomienia (mod, ścieżki, platforma).</param>
    /// <param name="observationWindow">Jak długo obserwować proces po starcie.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    /// <returns>Wynik z kodami diagnozy i severity.</returns>
    Task<LaunchResult> LaunchAndObserveAsync(
        LaunchContext launchContext,
        TimeSpan? observationWindow = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Dane potrzebne do uruchomienia moda – przekazywane z UI/ViewModel do LaunchSupervisor.
/// </summary>
public sealed class LaunchContext
{
    /// <summary>ID moda z katalogu.</summary>
    public int ModId { get; set; }

    /// <summary>Nazwa moda.</summary>
    public string ModName { get; set; } = string.Empty;

    /// <summary>Typ moda (full, dll, Vanilla).</summary>
    public string ModType { get; set; } = string.Empty;

    /// <summary>Platforma gry (steam, epic).</summary>
    public string PlatformMode { get; set; } = string.Empty;

    /// <summary>Ścieżka instalacji moda.</summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>Ścieżka do Among Us.exe.</summary>
    public string ExePath { get; set; } = string.Empty;

    /// <summary>Argumenty wiersza poleceń dla Among Us.exe (opcjonalne).</summary>
    public string? Arguments { get; set; }

    /// <summary>Czy SUStats/api_set.json ma być utworzone przed startem.</summary>
    public bool EnableSUStats { get; set; }

    /// <summary>Czy launch był wywołany z uprawnieniami administratora.</summary>
    public bool WasRunAsAdmin { get; set; }
}
