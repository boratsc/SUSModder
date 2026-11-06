using System;
using System.Collections.Generic;
using SUSModder.Core.Configuration;

namespace SUSModder.ViewModels.ModPackWizard;

/// <summary>Opcja wersji moda głównego (display + wartość przekazywana do instalatora/API + wersja gry, gdy znana).</summary>
public sealed record ModPackWizardVersionOption(string DisplayText, string Value, string? AmongVersion = null);

/// <summary>Źródło zawartości w trybie udostępniania istniejącego zestawu (instancja lub legacy z katalogu).</summary>
public sealed class ModPackWizardShareSource
{
    public string? InstanceId { get; init; }

    public required string DefaultPackName { get; init; }

    /// <summary>Etykieta listy (nazwa + wersja) — tworzona przez warstwę widoku, to dane, nie tekst UI.</summary>
    public required string Label { get; init; }

    public required ModConfiguration TargetMod { get; init; }

    /// <summary>Czy źródło (instancja) ma zapisany snapshot konfiguracji ToU — dopiero wtedy wolno wystawić przełącznik.</summary>
    public bool HasTouSnapshot { get; init; }
}

/// <summary>Dane własnego moda głównego z GitHuba (podkrok prowadzony w kroku „Mod główny”).</summary>
public sealed class ModPackWizardCustomFullDraft
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string AmongVersion { get; set; } = string.Empty;
    public string GithubUrl { get; set; } = string.Empty;

    /// <summary>Nazwa wśród wersji Among Us pobierana przez widok z manifestu; silnik tylko przechowuje wybór.</summary>
    public IReadOnlyList<string> AmongVersionOptions { get; set; } = Array.Empty<string>();
}

/// <summary>Stan przeglądu dodatka po zmianie moda głównego — nigdy nie usuwamy po cichu.</summary>
public enum ModPackWizardAddonReviewState
{
    None,

    /// <summary>Znane wersje Among Us różnią się — użytkownik musi wybrać „Zostaw” albo „Usuń”.</summary>
    NeedsDecision,

    /// <summary>Użytkownik świadomie zostawił dodatek mimo ostrzeżenia.</summary>
    KeptWithWarning
}

/// <summary>Wiersz dodatka katalogowego na kroku „Dodatki”.</summary>
public sealed class ModPackWizardAddonRow
{
    public required ModConfiguration Mod { get; init; }

    public bool IsChecked { get; set; }

    public ModPackWizardAddonReviewState ReviewState { get; set; } = ModPackWizardAddonReviewState.None;

    /// <summary>Gdy True — nie udało się zweryfikować zgodności (brak danych); informacja bez blokady.</summary>
    public bool CompatibilityUnverified { get; set; }
}

/// <summary>Plik DLL spoza katalogu wybrany z dysku (statusy skanu/uploadu pisze warstwa wykonania).</summary>
public sealed class ModPackWizardCustomDllRow
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public string Sha256 { get; set; } = string.Empty;
    public string? ArtifactId { get; set; }

    /// <summary>Status surowy z API (clean/scanning/…), null = jeszcze nie zgłoszono.</summary>
    public string? RawScanStatus { get; set; }

    /// <summary>Tekst statusu dla listy (klucz zlokalizowany wyznaczony przez widok). </summary>
    public string StatusText { get; set; } = string.Empty;

    public bool Uploaded { get; set; }
    public bool CopiedToInstance { get; set; }

    public string Sha256Preview => Sha256.Length > 12 ? Sha256[..12] + "…" : Sha256;
}

/// <summary>Artefakt DLL zgłoszony z publicznego GitHub release assetu.</summary>
public sealed class ModPackWizardGithubDllRow
{
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string GithubUrl { get; set; } = string.Empty;
    public string DllInstallPath { get; set; } = "BepInEx/plugins";
    public string? ArtifactId { get; set; }
    public string? RawScanStatus { get; set; }
    public string StatusText { get; set; } = string.Empty;
}

/// <summary>Model etapu wykonania renderowany na kroku „Wykonanie”.</summary>
public sealed class ModPackWizardStage
{
    public ModPackWizardStageId Id { get; init; }

    /// <summary>Klucz lokalizowany nazwy etapu (np. ModPacks.Wizard.Stage.InstallMain).</summary>
    public required string TitleKey { get; init; }

    public ModPackWizardStageState State { get; set; } = ModPackWizardStageState.Pending;

    /// <summary>Czy bieżące wykonanie tego etapu można bezpiecznie przerwać (operacja z CancellationToken).</summary>
    public bool Cancellable { get; set; }

    /// <summary>Dodatek: tekst wyniku (np. liczba zainstalowanych dodatków lub nazwy błędów) — dane, nie klucz.</summary>
    public string? Detail { get; set; }
}

/// <summary>Zebrane dane do renderowania kroku Podsumowanie (bez zależności od kontrolek).</summary>
public sealed class ModPackWizardSummary
{
    public required string MainLine { get; init; }
    public IReadOnlyList<string> AddonLines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OutOfCatalogLines { get; init; } = Array.Empty<string>();
    public bool SharesOnline { get; init; }
    public bool CreatesLocalInstance { get; init; }
    public bool IncludesTouConfig { get; init; }
    public string? UploadScope { get; init; }
    public int TtlDays { get; init; }
    public string? CreatorName { get; init; }
    public string? DiscordInvite { get; init; }
}
