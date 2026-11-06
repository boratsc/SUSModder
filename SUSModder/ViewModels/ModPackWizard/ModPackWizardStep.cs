namespace SUSModder.ViewModels.ModPackWizard;

/// <summary>Tryb wejścia kreatora — mapowany 1:1 z ModPackCreatorMode przez warstwę widoku.</summary>
public enum ModPackWizardEntryMode
{
    /// <summary>Nowy zestaw lokalny.</summary>
    InstallLocal,

    /// <summary>Udostępnienie istniejącej zawartości (instancji) na serwer.</summary>
    ShareExisting,

    /// <summary>Nowy zestaw lokalny + udostępnienie.</summary>
    CreateAndShare
}

/// <summary>
/// Kroki prowadzonego kreatora zestawów (3.1-02). Stan trzymany wyłącznie w pamięci —
/// zgodnie z analizą bez trwałych szkiców i bez migracji DB.
/// </summary>
public enum ModPackWizardStep
{
    /// <summary>Po co tworzymy zestaw (znajomi / tylko dla siebie). Pomijany przy udostępnianiu istniejącej zawartości.</summary>
    Goal,

    /// <summary>Mod główny z katalogu, własny mod z GitHuba lub — w trybie udostępniania — źródło z Moich zestawów.</summary>
    MainMod,

    /// <summary>Dodatki: katalog DLL oraz źródła spoza katalogu (plik z dysku, GitHub release asset).</summary>
    Addons,

    /// <summary>Nazwa zestawu i ustawienia (integracja, ToU gdy dostępny, dane i ważność udostępnienia).</summary>
    NameSettings,

    /// <summary>Podsumowanie składu, platformy, instalacji lokalnej i zakresu uploadu.</summary>
    Summary,

    /// <summary>Wykonanie: jawne etapy zgodne z kolejnością usług oraz wynik (także częściowy).</summary>
    Execution
}

/// <summary>Wybór celu z kroku „Cel”.</summary>
public enum ModPackWizardGoal
{
    /// <summary>„Tylko dla siebie” — instalacja lokalna bez wysyłania na serwer.</summary>
    Personal,

    /// <summary>„Przygotować dla znajomych” — instalacja lokalna + udostępnienie (link/kod).</summary>
    ForFriends
}

/// <summary>Identyfikatory etapów wykonania — kolejność odzwierciedla rzeczywisty przepływ usług.</summary>
public enum ModPackWizardStageId
{
    InstallMain,
    InstallAddons,
    CopyCustomFiles,
    CreateSharePack,
    SendCustomContent,
    FinalValidation
}

/// <summary>Stan pojedynczego etapu wykonania.</summary>
public enum ModPackWizardStageState
{
    Pending,

    /// <summary>Etap niekasowalny, ale jeszcze nietrwający — pokazujemy „czeka”.</summary>
    Running,
    Done,
    Failed,

    /// <summary>Przerwany przez użytkownika w punkcie bezpiecznego zatrzymania.</summary>
    Stopped,
    Skipped
}
