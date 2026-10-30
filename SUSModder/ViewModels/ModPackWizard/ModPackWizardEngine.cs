using System;
using System.Collections.Generic;
using System.Linq;
using SUSModder.Core.Configuration;

namespace SUSModder.ViewModels.ModPackWizard;

/// <summary>Przypisana wiadomość prezentacyjna: klucz lokalizowany + argumenty pozycyjne.</summary>
public sealed record ModPackWizardMessage(string Key, params string?[] Args)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Key);
}

/// <summary>Główna akcja stopki kreatora — opisuje skutek, nie „UTWÓRZ”.</summary>
public enum ModPackWizardPrimaryAction
{
    Next,
    InstallLocal,
    InstallAndShare,
    ShareExisting,
    Finish
}

/// <summary>
/// Stan prowadzonego kreatora modpacków (3.1-02). Warstwa prezentacyjna — nie dotyka
/// dysku ani API; wykonaniem steruje widok przez etapy opisane poniżej.
/// Walidacja i komunikaty operują na KLUCZACH lokalizacyjnych (PL/EN po stronie widoka).
/// </summary>
public sealed class ModPackWizardEngine
{
    private readonly List<ModPackWizardStage> _stages = new();

    private readonly List<ModPackWizardStep> _steps;

    public ModPackWizardEngine(ModPackWizardEntryMode entryMode, string platform)
    {
        EntryMode = entryMode;
        Platform = platform ?? string.Empty;

        _steps = entryMode == ModPackWizardEntryMode.ShareExisting
            ? new List<ModPackWizardStep>
            {
                ModPackWizardStep.MainMod,
                ModPackWizardStep.Addons,
                ModPackWizardStep.NameSettings,
                ModPackWizardStep.Summary,
                ModPackWizardStep.Execution
            }
            : new List<ModPackWizardStep>
            {
                ModPackWizardStep.Goal,
                ModPackWizardStep.MainMod,
                ModPackWizardStep.Addons,
                ModPackWizardStep.NameSettings,
                ModPackWizardStep.Summary,
                ModPackWizardStep.Execution
            };
        Steps = _steps;

        Goal = entryMode == ModPackWizardEntryMode.CreateAndShare
            ? ModPackWizardGoal.ForFriends
            : ModPackWizardGoal.Personal;

        if (entryMode == ModPackWizardEntryMode.ShareExisting)
            Goal = ModPackWizardGoal.ForFriends;

        CurrentStep = Steps[0];
        TtlDays = 30;
    }

    /// <summary>Zmiana stanu — widok przebudowuje kroki.</summary>
    public event Action? Changed;

    public ModPackWizardEntryMode EntryMode { get; }
    public string Platform { get; }
    public IReadOnlyList<ModPackWizardStep> Steps { get; }
    public ModPackWizardStep CurrentStep { get; private set; } = ModPackWizardStep.Goal;
    public int StepIndex => _steps.IndexOf(CurrentStep);

    public bool CanGoBack => StepIndex > 0 && !ExecutionStarted;

    /// <summary>Udostępnianie włączone w ustawieniach aplikacji (bramka hosta; silnik pokazuje wyjaśnienie).</summary>
    public bool SharingAllowedByApp { get; set; } = true;

    // ── Cel ──────────────────────────────────────────────────────────────
    public ModPackWizardGoal Goal { get; private set; }

    public bool SharesOnline => Goal == ModPackWizardGoal.ForFriends;
    public bool CreatesLocalInstance => EntryMode != ModPackWizardEntryMode.ShareExisting;

    public bool IsGoalLockedToFriends => EntryMode == ModPackWizardEntryMode.ShareExisting;

    public void SetGoal(ModPackWizardGoal goal)
    {
        if (IsGoalLockedToFriends && goal != ModPackWizardGoal.ForFriends)
            return;
        if (Goal == goal)
            return;
        Goal = goal;
        Touch();
    }

    // ── Mod główny: katalog ──────────────────────────────────────────────
    public IReadOnlyList<ModConfiguration> CatalogFullMods { get; private set; } = Array.Empty<ModConfiguration>();

    public int SelectedCatalogModId { get; private set; } = -1;

    /// <summary>Aktywny mod główny z katalogu (null przy własnym modzie z GitHuba).</summary>
    public ModConfiguration? SelectedCatalogMod =>
        CatalogFullMods.FirstOrDefault(m => m.Id == SelectedCatalogModId);

    /// <summary>True, gdy aktywny jest własny mod główny z GitHuba zamiast katalogowego.</summary>
    public bool UsingCustomFull { get; private set; }

    public ModPackWizardCustomFullDraft? ConfirmedCustomFull { get; private set; }

    public IReadOnlyList<ModPackWizardVersionOption> VersionOptions { get; private set; } =
        Array.Empty<ModPackWizardVersionOption>();

    public string SelectedVersionValue { get; private set; } = "latest";

    public void SetCatalogFullMods(IReadOnlyList<ModConfiguration> mods, int? preselectedModId = null)
    {
        CatalogFullMods = mods ?? Array.Empty<ModConfiguration>();
        var preselected = preselectedModId.HasValue
            ? CatalogFullMods.FirstOrDefault(m => m.Id == preselectedModId.Value)
            : null;
        // Fallback do pierwszego elementu — tak jak w poprzednim formularzu.
        SelectedCatalogModId = (preselected ?? CatalogFullMods.FirstOrDefault())?.Id ?? -1;
        OnMainModChanged(triggerReview: false);
    }

    public void SetSelectedCatalogMod(int modId)
    {
        if (UsingCustomFull || SelectedCatalogModId == modId)
            return;
        SelectedCatalogModId = modId;
        VersionOptions = Array.Empty<ModPackWizardVersionOption>();
        SelectedVersionValue = "latest";
        OnMainModChanged(triggerReview: true);
        Touch();
    }

    /// <summary>Widok pobrał wersje wybranego moda katalogowego (programowo — bez brudzenia).</summary>
    public void SetVersionOptions(IReadOnlyList<ModPackWizardVersionOption>? options)
    {
        VersionOptions = options ?? Array.Empty<ModPackWizardVersionOption>();
        if (VersionOptions.Count > 0 &&
            VersionOptions.All(o => !string.Equals(o.Value, SelectedVersionValue, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedVersionValue = VersionOptions[0].Value;
        }
        RaiseChanged();
    }

    public void SetSelectedVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || SelectedVersionValue == value)
            return;

        // Zmiana wersji moda głównego też może ruszać dodatki — sprawdzamy jak przy zmianie moda.
        var oldAmong = GetMainAmongVersion();
        SelectedVersionValue = value;
        var newAmong = GetMainAmongVersion();
        if (!string.Equals(oldAmong, newAmong, StringComparison.OrdinalIgnoreCase))
            OnMainModChanged(triggerReview: true);
        Touch();
    }

    /// <summary>Potwierdzenie własnego moda głównego z GitHuba w podkroku prowadzonym.</summary>
    public bool TryConfirmCustomFull(ModPackWizardCustomFullDraft draft, out ModPackWizardMessage? error)
    {
        error = ValidateCustomFull(draft);
        if (error != null)
            return false;

        ConfirmedCustomFull = new ModPackWizardCustomFullDraft
        {
            Name = draft.Name.Trim(),
            Version = draft.Version.Trim(),
            AmongVersion = draft.AmongVersion.Trim(),
            GithubUrl = draft.GithubUrl.Trim()
        };
        UsingCustomFull = true;
        OnMainModChanged(triggerReview: true);
        Touch();
        return true;
    }

    public void RemoveCustomFull()
    {
        if (!UsingCustomFull)
            return;
        UsingCustomFull = false;
        ConfirmedCustomFull = null;
        OnMainModChanged(triggerReview: true);
        Touch();
    }

    // ── Mod główny: źródło udostępnienia (ShareExisting) ─────────────────
    public IReadOnlyList<ModPackWizardShareSource> ShareSources { get; private set; } =
        Array.Empty<ModPackWizardShareSource>();

    public int SelectedShareSourceIndex { get; private set; } = -1;

    public ModPackWizardShareSource? SelectedShareSource =>
        SelectedShareSourceIndex >= 0 && SelectedShareSourceIndex < ShareSources.Count
            ? ShareSources[SelectedShareSourceIndex]
            : null;

    public void SetShareSources(IReadOnlyList<ModPackWizardShareSource> sources,
        string? preselectedInstanceId = null, int? preselectedCatalogModId = null)
    {
        ShareSources = sources ?? Array.Empty<ModPackWizardShareSource>();
        var idx = !string.IsNullOrEmpty(preselectedInstanceId)
            ? ShareSources.ToList().FindIndex(s => s.InstanceId == preselectedInstanceId)
            : preselectedCatalogModId.HasValue
                ? ShareSources.ToList().FindIndex(s => s.TargetMod.Id == preselectedCatalogModId.Value)
                : -1;
        if (idx < 0)
            idx = ShareSources.Count > 0 ? 0 : -1;
        SelectedShareSourceIndex = idx;
        OnSourceChanged(triggerReview: ShareSources.Count > 0);
    }

    public void SetSelectedShareSource(int index)
    {
        if (SelectedShareSourceIndex == index || index < 0 || index >= ShareSources.Count)
            return;
        SelectedShareSourceIndex = index;
        OnSourceChanged(triggerReview: true);
        Touch();
    }

    // ── Dodatki katalogowe ───────────────────────────────────────────────
    public List<ModPackWizardAddonRow> AddonRows { get; } = new();

    /// <summary>Baner „sprawdziliśmy dodatki po zmianie moda głównego” — znika po rozwiązaniu decyzji.</summary>
    public bool MainModReviewBannerVisible { get; private set; }

    public IReadOnlyList<ModPackWizardAddonRow> RowsPendingDecision =>
        AddonRows.Where(r => r.ReviewState == ModPackWizardAddonReviewState.NeedsDecision).ToList();

    public IReadOnlyList<ModPackWizardAddonRow> RowsUnverified =>
        AddonRows.Where(r => r.CompatibilityUnverified && r.IsChecked).ToList();

    public void SetAddonCatalog(IReadOnlyList<ModConfiguration> dllMods,
        IReadOnlyCollection<int>? preselectedIds = null, bool programmatic = true)
    {
        AddonRows.Clear();
        foreach (var mod in dllMods ?? Array.Empty<ModConfiguration>())
        {
            AddonRows.Add(new ModPackWizardAddonRow
            {
                Mod = mod,
                IsChecked = preselectedIds != null && preselectedIds.Contains(mod.Id)
            });
        }
        if (!programmatic)
            Touch();
        RaiseChanged();
    }

    public void SetAddonChecked(int modId, bool isChecked)
    {
        var row = AddonRows.FirstOrDefault(r => r.Mod.Id == modId);
        if (row == null || row.IsChecked == isChecked)
            return;
        row.IsChecked = isChecked;
        if (isChecked && row.ReviewState == ModPackWizardAddonReviewState.NeedsDecision)
            row.ReviewState = ModPackWizardAddonReviewState.KeptWithWarning;
        if (!isChecked)
        {
            row.ReviewState = ModPackWizardAddonReviewState.None;
            row.CompatibilityUnverified = false;
        }
        Touch();
        RaiseChanged();
    }

    /// <summary>Świadoma decyzja użytkownika dla dodatku oznaczonego po zmianie moda głównego.</summary>
    public void ResolveAddonDecision(int modId, bool keep)
    {
        var row = AddonRows.FirstOrDefault(r => r.Mod.Id == modId);
        if (row == null)
            return;
        row.IsChecked = keep;
        row.ReviewState = keep
            ? ModPackWizardAddonReviewState.KeptWithWarning
            : ModPackWizardAddonReviewState.None;
        if (!keep)
            row.CompatibilityUnverified = false;
        if (RowsPendingDecision.Count == 0)
            MainModReviewBannerVisible = false;
        Touch();
        RaiseChanged();
    }

    // ── Dodatki spoza katalogu ───────────────────────────────────────────
    public List<ModPackWizardCustomDllRow> CustomDllRows { get; } = new();
    public List<ModPackWizardGithubDllRow> GithubDllRows { get; } = new();

    /// <summary>Przełącznik źródeł GitHub DLL — widoczny tylko gdy zestaw idzie na serwer.</summary>
    public bool UseGithubDllSources { get; private set; }

    public void SetUseGithubDllSources(bool value)
    {
        if (UseGithubDllSources == value)
            return;
        UseGithubDllSources = value;
        Touch();
        RaiseChanged();
    }

    public bool AddCustomDllFile(ModPackWizardCustomDllRow row)
    {
        if (row == null ||
            CustomDllRows.Any(x => string.Equals(x.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase)))
            return false;
        CustomDllRows.Add(row);
        Touch();
        RaiseChanged();
        return true;
    }

    public void RemoveCustomDllFile(ModPackWizardCustomDllRow row)
    {
        if (CustomDllRows.Remove(row))
        {
            Touch();
            RaiseChanged();
        }
    }

    public void AddGithubDll(ModPackWizardGithubDllRow row)
    {
        if (row == null)
            return;
        GithubDllRows.Add(row);
        Touch();
        RaiseChanged();
    }

    public void RemoveGithubDll(ModPackWizardGithubDllRow row)
    {
        if (GithubDllRows.Remove(row))
        {
            Touch();
            RaiseChanged();
        }
    }

    // ── Nazwa i ustawienia ───────────────────────────────────────────────
    private string _packName = string.Empty;

    public string PackName
    {
        get => _packName;
        private set => _packName = value ?? string.Empty;
    }

    /// <summary>Czy użytkownik sam wpisał nazwę (nie nadpisujemy propozycji po zmianie moda).</summary>
    public bool PackNameUserEdited { get; private set; }

    public bool IncludeTouConfig { get; private set; }

    /// <summary>ToU dostępne tylko gdy źródło ma zapisany snapshot — inaczej nie wystawiamy przełącznika.</summary>
    public bool IsTouConfigAvailable =>
        SharesOnline && SelectedShareSource?.InstanceId != null && SelectedShareSource.HasTouSnapshot;

    public IReadOnlyList<int> AllowedTtlDays { get; } = new[] { 7, 30, 90 };
    public int TtlDays { get; private set; } = 30;

    public string? CreatorName { get; private set; }
    public string? DiscordInvite { get; private set; }

    public void SetPackName(string? value, bool fromUser)
    {
        if (fromUser)
            PackNameUserEdited = true;
        if (string.Equals(PackName, value?.Trim() ?? string.Empty, StringComparison.Ordinal))
            return;
        PackName = value?.Trim() ?? string.Empty;
        if (fromUser)
            Touch();
        RaiseChanged();
    }

    /// <summary>Programistyczna propozycja nazwy (bez brudzenia stanu).</summary>
    public void ApplySuggestedPackName(string? suggestion)
    {
        if (PackNameUserEdited || string.IsNullOrWhiteSpace(suggestion))
            return;
        PackName = suggestion.Trim();
        RaiseChanged();
    }

    public void SetIncludeTouConfig(bool value)
    {
        if (!IsTouConfigAvailable)
        {
            IncludeTouConfig = false;
            return;
        }
        if (IncludeTouConfig == value)
            return;
        IncludeTouConfig = value;
        Touch();
        RaiseChanged();
    }

    public void SetTtlDays(int days)
    {
        if (!AllowedTtlDays.Contains(days))
            days = 30;
        if (TtlDays == days)
            return;
        TtlDays = days;
        Touch();
        RaiseChanged();
    }

    /// <summary>Widok wczytuje zapisany profil udostępniania — programowo, bez brudzenia.</summary>
    public void ApplyShareProfile(string? creatorName, string? discordInvite)
    {
        CreatorName = string.IsNullOrWhiteSpace(creatorName) ? null : creatorName.Trim();
        DiscordInvite = string.IsNullOrWhiteSpace(discordInvite) ? null : discordInvite.Trim();
        RaiseChanged();
    }

    public void SetCreatorName(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.Equals(CreatorName ?? string.Empty, trimmed, StringComparison.Ordinal))
            return;
        CreatorName = trimmed.Length == 0 ? null : trimmed;
        Touch();
    }

    public void SetDiscordInvite(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.Equals(DiscordInvite ?? string.Empty, trimmed, StringComparison.Ordinal))
            return;
        DiscordInvite = trimmed.Length == 0 ? null : trimmed;
        Touch();
    }

    // ── Nawigacja i walidacja ────────────────────────────────────────────
    public ModPackWizardPrimaryAction PrimaryAction =>
        CurrentStep switch
        {
            ModPackWizardStep.Summary => SharesOnline
                ? CreatesLocalInstance
                    ? ModPackWizardPrimaryAction.InstallAndShare
                    : ModPackWizardPrimaryAction.ShareExisting
                : ModPackWizardPrimaryAction.InstallLocal,
            ModPackWizardStep.Execution when ExecutionFinished => ModPackWizardPrimaryAction.Finish,
            _ => ModPackWizardPrimaryAction.Next
        };

    /// <summary>Walidacja bieżącego kroku; zwraca komunikat z kluczem lokalizowanym albo null.</summary>
    public ModPackWizardMessage? ValidateCurrentStep()
    {
        switch (CurrentStep)
        {
            case ModPackWizardStep.Goal:
                if (SharesOnline && !SharingAllowedByApp)
                    return new ModPackWizardMessage("ModPacks.Disabled");
                return null;

            case ModPackWizardStep.MainMod:
                if (EntryMode == ModPackWizardEntryMode.ShareExisting)
                {
                    return SelectedShareSource == null
                        ? new ModPackWizardMessage("UI.Packs.SelectShareSource")
                        : null;
                }

                if (UsingCustomFull)
                {
                    return ConfirmedCustomFull == null
                        ? new ModPackWizardMessage("ModPacks.Wizard.Main.ConfirmRequired")
                        : null;
                }

                return SelectedCatalogModId < 0
                    ? new ModPackWizardMessage("ModPacks.SelectFullMod")
                    : null;

            case ModPackWizardStep.Addons:
                if (RowsPendingDecision.Count > 0)
                    return new ModPackWizardMessage("ModPacks.Wizard.Addons.UnresolvedDecisions");
                return null;

            case ModPackWizardStep.NameSettings:
                if (string.IsNullOrWhiteSpace(PackName))
                    return new ModPackWizardMessage("UI.Packs.DisplayNameRequired");
                if (SharesOnline && !SharingAllowedByApp)
                    return new ModPackWizardMessage("ModPacks.Disabled");
                return null;

            default:
                return null;
        }
    }

    public bool TryNext(out ModPackWizardMessage? error)
    {
        error = ValidateCurrentStep();
        if (error != null)
            return false;

        var idx = StepIndex;
        if (idx >= Steps.Count - 1)
            return false;
        CurrentStep = Steps[idx + 1];
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Zweryfikuj wszystkie kroki merytoryczne przed startem wykonania.
    /// Gdy któryś zawiedzie — nawiguje do niego i zwraca komunikat (błąd przy kroku, nie cichy powrót).
    /// </summary>
    public ModPackWizardMessage? ValidateAllContentSteps()
    {
        var original = CurrentStep;
        foreach (var step in Steps)
        {
            if (step is ModPackWizardStep.Summary or ModPackWizardStep.Execution)
                break;
            CurrentStep = step;
            var error = ValidateCurrentStep();
            if (error != null)
            {
                RaiseChanged();
                return error;
            }
        }
        CurrentStep = original;
        return null;
    }

    public void GoBack()
    {
        var idx = StepIndex;
        if (idx <= 0 || !CanGoBack)
            return;
        CurrentStep = Steps[idx - 1];
        RaiseChanged();
    }

    // ── Podsumowanie ─────────────────────────────────────────────────────
    public string SummaryMainLine
    {
        get
        {
            if (EntryMode == ModPackWizardEntryMode.ShareExisting)
            {
                var source = SelectedShareSource;
                return source == null
                    ? string.Empty
                    : $"{source.DefaultPackName} — {source.TargetMod.ModName} v{source.TargetMod.ModVersion}";
            }

            if (UsingCustomFull && ConfirmedCustomFull != null)
                return $"{ConfirmedCustomFull.Name} v{(string.IsNullOrWhiteSpace(ConfirmedCustomFull.Version) ? "custom" : ConfirmedCustomFull.Version)} · Among Us {ConfirmedCustomFull.AmongVersion} · GitHub";

            var mod = CatalogFullMods.FirstOrDefault(m => m.Id == SelectedCatalogModId);
            if (mod == null)
                return string.Empty;
            var version = string.Equals(SelectedVersionValue, "latest", StringComparison.OrdinalIgnoreCase)
                ? mod.ModVersion
                : SelectedVersionValue;
            return $"{mod.ModName} v{version}";
        }
    }

    public IReadOnlyList<string> SummaryAddonLines => AddonRows
        .Where(r => r.IsChecked)
        .Select(r => $"{r.Mod.ModName} v{r.Mod.ModVersion}")
        .ToList();

    public IReadOnlyList<string> SummaryOutOfCatalogLines
    {
        get
        {
            var lines = new List<string>();
            lines.AddRange(CustomDllRows.Select(r => $"{r.FileName} ({FormatSize(r.FileSize)})"));
            lines.AddRange(GithubDllRows
                .Where(_ => UseGithubDllSources)
                .Select(r => $"{(string.IsNullOrWhiteSpace(r.DisplayName) ? r.GithubUrl : r.DisplayName)}"));
            if (UsingCustomFull && ConfirmedCustomFull != null)
                lines.Add($"{ConfirmedCustomFull.Name} (GitHub)");
            return lines;
        }
    }

    public bool HasCustomContent =>
        CustomDllRows.Count > 0 || (UseGithubDllSources && GithubDllRows.Count > 0) ||
        (UsingCustomFull && EntryMode != ModPackWizardEntryMode.ShareExisting && SharesOnline);

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    // ── Podsumowanie / wykonanie ─────────────────────────────────────────
    public IReadOnlyList<ModPackWizardStage> Stages => _stages;
    public bool ExecutionStarted { get; private set; }
    public bool ExecutionFinished { get; private set; }

    /// <summary>Wykonanie zatrzymanie/powodziło się częściowo — primary = ponów lub wznowij.</summary>
    public bool ExecutionNeedsRetry => ExecutionStarted && !IsExecuting && Stages.Any(s =>
        s.State is ModPackWizardStageState.Failed or ModPackWizardStageState.Stopped or ModPackWizardStageState.Pending);

    public bool ExecutionAllDone => Stages.Count > 0 && Stages.All(s =>
        s.State is ModPackWizardStageState.Done or ModPackWizardStageState.Skipped);

    /// <summary>W trakcie realnej operacji (blokuje Wstecz i wymaga potwierdzenia przy zamknięciu).</summary>
    public bool IsExecuting { get; private set; }

    /// <summary>
    /// Kod zestawu utworzonego na serwerze, gdy dalsze etapy (custom content / finalizacja)
    /// nie doszły do skutku. Ponowienie wznawia finalizację zamiast tworzyć drugą paczkę.
    /// </summary>
    public string? PendingSharePackCode { get; set; }

    public bool PackAlreadyCreatedOnServer => !string.IsNullOrWhiteSpace(PendingSharePackCode) || !string.IsNullOrWhiteSpace(ResultPackCode);

    /// <summary>Identyfikator instancji utworzonej w tym przebiegu — ponowienie NIE instaluje drugiej.</summary>
    public string? CreatedInstanceId { get; set; }

    public List<int> InstalledDllIds { get; } = new();
    public List<string> FailedDllNames { get; } = new();
    public List<string> FailedCustomFileNames { get; } = new();

    public string? ResultPackCode { get; set; }
    public string? ResultShareUrl { get; set; }
    public string? ResultErrorCode { get; set; }

    /// <summary>Błąd siatki: niepewne utworzenie — trzeba zweryfikować listę przed ponowieniem.</summary>
    public bool RequiresDuplicateProbeBeforeRetry { get; set; }
    public bool CanSubmitNewShareRequest => !RequiresDuplicateProbeBeforeRetry && !PackAlreadyCreatedOnServer;

    public IReadOnlyList<ModPackWizardStage> PlanStages()
    {
        var planned = new List<ModPackWizardStage>();
        if (CreatesLocalInstance)
        {
            planned.Add(new ModPackWizardStage
            {
                Id = ModPackWizardStageId.InstallMain,
                TitleKey = "ModPacks.Wizard.Stage.InstallMain"
            });

            if (AddonRows.Any(r => r.IsChecked))
                planned.Add(new ModPackWizardStage
                {
                    Id = ModPackWizardStageId.InstallAddons,
                    TitleKey = "ModPacks.Wizard.Stage.InstallAddons"
                });

            if (CustomDllRows.Count > 0)
                planned.Add(new ModPackWizardStage
                {
                    Id = ModPackWizardStageId.CopyCustomFiles,
                    TitleKey = "ModPacks.Wizard.Stage.CopyCustom"
                });
        }

        if (SharesOnline)
        {
            planned.Add(new ModPackWizardStage
            {
                Id = ModPackWizardStageId.CreateSharePack,
                TitleKey = PackAlreadyCreatedOnServer
                    ? "ModPacks.Wizard.Stage.ResumeShare"
                    : "ModPacks.Wizard.Stage.CreateShare",
                Cancellable = true
            });

            if (HasCustomContent)
            {
                planned.Add(new ModPackWizardStage
                {
                    Id = ModPackWizardStageId.SendCustomContent,
                    TitleKey = "ModPacks.Wizard.Stage.SendCustom",
                    Cancellable = true
                });
                planned.Add(new ModPackWizardStage
                {
                    Id = ModPackWizardStageId.FinalValidation,
                    TitleKey = "ModPacks.Wizard.Stage.Finalize",
                    Cancellable = true
                });
            }
        }

        return planned;
    }

    public void StartExecution()
    {
        _stages.Clear();
        // Lista porażek jest przeliczana przy każdym przebiegu — bez dublowania nazw przy ponowieniu.
        FailedDllNames.Clear();
        FailedCustomFileNames.Clear();
        var planned = PlanStages();
        foreach (var stage in planned)
        {
            if (IsStageAlreadyCompleted(stage.Id))
            {
                stage.State = ModPackWizardStageState.Done;
                stage.Detail = null;
            }
            _stages.Add(stage);
        }
        ExecutionStarted = true;
        ExecutionFinished = false;
        ResultErrorCode = null;
        RaiseChanged();
    }

    /// <summary>Metryczki etapów już wykonanych w tym oknie kreatora — ponowienie nie powtarza pracy.</summary>
    public bool InstanceCreated => !string.IsNullOrWhiteSpace(CreatedInstanceId);

    public bool AddonsStageProcessed { get; set; }
    public bool CustomFilesCopiedStageDone { get; set; }

    private bool IsStageAlreadyCompleted(ModPackWizardStageId id) => id switch
    {
        ModPackWizardStageId.InstallMain => InstanceCreated,
        ModPackWizardStageId.InstallAddons => AddonsStageProcessed,
        ModPackWizardStageId.CopyCustomFiles => CustomFilesCopiedStageDone,
        ModPackWizardStageId.CreateSharePack => PackAlreadyCreatedOnServer,
        _ => false
    };

    public void SetStageRunning(ModPackWizardStageId id, bool cancellable)
    {
        var stage = _stages.FirstOrDefault(s => s.Id == id);
        if (stage == null)
            return;
        stage.State = ModPackWizardStageState.Running;
        stage.Cancellable = cancellable;
        IsExecuting = true;
        RaiseChanged();
    }

    public void SetStageDone(ModPackWizardStageId id, string? detail = null)
    {
        var stage = _stages.FirstOrDefault(s => s.Id == id);
        if (stage == null)
            return;
        stage.State = ModPackWizardStageState.Done;
        stage.Detail = detail;
        RaiseChanged();
    }

    public void SetStageFailed(ModPackWizardStageId id, string? detail = null)
    {
        var stage = _stages.FirstOrDefault(s => s.Id == id);
        if (stage == null)
            return;
        stage.State = ModPackWizardStageState.Failed;
        stage.Detail = detail;
        RaiseChanged();
    }

    public void SetStageStopped(ModPackWizardStageId id)
    {
        var stage = _stages.FirstOrDefault(s => s.Id == id);
        if (stage == null)
            return;
        stage.State = ModPackWizardStageState.Stopped;
        RaiseChanged();
    }

    public void MarkExecutionSettled(bool finished)
    {
        IsExecuting = false;
        if (finished)
            ExecutionFinished = true;
        RaiseChanged();
    }

    public ModPackWizardStage? RunningStage =>
        _stages.FirstOrDefault(s => s.State == ModPackWizardStageState.Running);

    public bool IsExecutionSettled() => !IsExecuting;

    // ── Brudny stan / zamknięcie ─────────────────────────────────────────
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Zamknięcie wymaga potwierdzenia: niezapisane wybory przed pierwszym wykonaniem
    /// albo trwająca operacja. Po starcie wykonania zamknięcie ZWRACA realny stan częściowy
    /// (BuildDialogResult), więc nie udajemy utraty ani rollbacku.
    /// </summary>
    public bool RequiresCloseConfirmation => IsExecuting || (!ExecutionStarted && IsDirty);

    private void Touch()
    {
        IsDirty = true;
    }

    public void MarkClean() => IsDirty = false;

    // ── Wewnętrzna rewalidacja zgodności ─────────────────────────────────
    private string? GetMainAmongVersion()
    {
        if (EntryMode == ModPackWizardEntryMode.ShareExisting)
            return NullIfEmpty(SelectedShareSource?.TargetMod.AmongVersion);
        if (UsingCustomFull)
            return NullIfEmpty(ConfirmedCustomFull?.AmongVersion);

        var mod = CatalogFullMods.FirstOrDefault(m => m.Id == SelectedCatalogModId);
        if (mod == null)
            return null;

        var option = VersionOptions.FirstOrDefault(o =>
            string.Equals(o.Value, SelectedVersionValue, StringComparison.OrdinalIgnoreCase));
        if (option != null && !string.IsNullOrWhiteSpace(option.AmongVersion))
            return NullIfEmpty(option.AmongVersion);

        // Wersja katalogowa = „latest” → AmongVersion z katalogu jest aktualna.
        // Przypięta starsza wersja bez własnych danych → unknown (nie zgadujemy zgodności).
        if (string.Equals(SelectedVersionValue, "latest", StringComparison.OrdinalIgnoreCase) ||
            VersionOptions.Count == 0)
            return NullIfEmpty(mod.AmongVersion);
        return null;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Po zmianie moda głównego: dodatki ZAZNACZONE wracają do weryfikacji.
    /// Nie usuwamy niczego po cichu — wymagamy jawnej decyzji lub potwierdzenia braku danych.
    /// </summary>
    private void OnMainModChanged(bool triggerReview)
    {
        if (!triggerReview)
        {
            RaiseChanged();
            return;
        }

        RevalidateAddons();
        RaiseChanged();
    }

    private void OnSourceChanged(bool triggerReview)
    {
        // Źródło = zawartość: przeładowanie dodatków i ustawień leży po stronie widoka (event).
        MainModReviewBannerVisible = triggerReview && AddonRows.Any(r => r.IsChecked);
        SourceChangedRequested = triggerReview;

        // ToU wystawiamy TYLKO gdy źródło realnie ma snapshot — domyślnie dołączony (tak wysyła mapper).
        IncludeTouConfig = SharesOnline && SelectedShareSource?.HasTouSnapshot == true;

        RevalidateAddons();
        RaiseChanged();
    }

    /// <summary>Widok ma przeładować dodatki/ustawienia źródła i pokazać jawny komunikat.</summary>
    public bool SourceChangedRequested { get; private set; }

    public void AcknowledgeSourceChangeReload() => SourceChangedRequested = false;

    public event Action? AddonRevalidationRequested;

    private void RevalidateAddons()
    {
        var mainAmong = GetMainAmongVersion();
        var anyReview = false;

        foreach (var row in AddonRows)
        {
            if (!row.IsChecked)
                continue;

            var addonAmong = NullIfEmpty(row.Mod.AmongVersion);
            if (mainAmong == null || addonAmong == null)
            {
                // Brak danych o którejś stronie — nie zgadujemy, informujemy jawnie.
                row.CompatibilityUnverified = true;
                row.ReviewState = ModPackWizardAddonReviewState.None;
                continue;
            }

            if (!VersionsComparable(mainAmong, addonAmong))
            {
                row.CompatibilityUnverified = false;
                continue;
            }

            if (!string.Equals(NormalizeVersion(mainAmong), NormalizeVersion(addonAmong), StringComparison.OrdinalIgnoreCase))
            {
                row.ReviewState = ModPackWizardAddonReviewState.NeedsDecision;
                row.CompatibilityUnverified = false;
                anyReview = true;
            }
            else
            {
                row.ReviewState = ModPackWizardAddonReviewState.None;
                row.CompatibilityUnverified = false;
            }
        }

        if (anyReview)
            MainModReviewBannerVisible = true;
        else if (RowsPendingDecision.Count == 0)
            MainModReviewBannerVisible = false;

        AddonRevalidationRequested?.Invoke();
    }

    /// <summary>Wersje gry w katalogu to m.in. „2023.6.13” i „v2023…”; porównujemy liczbowy rdzeń.</summary>
    private static bool VersionsComparable(string a, string b) =>
        a.Any(char.IsDigit) && b.Any(char.IsDigit);

    private static string NormalizeVersion(string version)
    {
        var trimmed = version.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase) &&
            trimmed.Length > 1 && char.IsDigit(trimmed[1]))
            return trimmed[1..];
        return trimmed;
    }

    private static ModPackWizardMessage? ValidateCustomFull(ModPackWizardCustomFullDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.GithubUrl))
            return new ModPackWizardMessage("ModPacks.CustomFull.UrlRequired");
        if (!ModPackWizardSourceGuard.IsAllowedGithubReleaseAssetUrl(draft.GithubUrl))
            return new ModPackWizardMessage("ModPacks.CustomFull.ReleaseAssetRequired");
        if (string.IsNullOrWhiteSpace(draft.Name))
            return new ModPackWizardMessage("ModPacks.CustomFull.NameRequired");
        if (string.IsNullOrWhiteSpace(draft.AmongVersion))
            return new ModPackWizardMessage("ModPacks.CustomFull.AmongVersionRequired");
        return null;
    }

    private void RaiseChanged() => Changed?.Invoke();

    public ModPackWizardSummary BuildSummary()
    {
        return new ModPackWizardSummary
        {
            MainLine = SummaryMainLine,
            AddonLines = SummaryAddonLines,
            OutOfCatalogLines = SummaryOutOfCatalogLines,
            SharesOnline = SharesOnline,
            CreatesLocalInstance = CreatesLocalInstance,
            IncludesTouConfig = IsTouConfigAvailable && IncludeTouConfig,
            TtlDays = TtlDays,
            CreatorName = CreatorName,
            DiscordInvite = DiscordInvite,
            UploadScope = SharesOnline
                ? "ModPacks.Wizard.Summary.UploadNotice"
                : null
        };
    }
}
