using SUSModder.Core.Configuration;
using SUSModder.ViewModels.ModPackWizard;

namespace SUSModder.Tests.ViewModels.ModPackWizard;

/// <summary>
/// Testy silnika prowadzonego kreatora zestawów (3.1-02): kolejność kroków,
/// zachowanie stanu przy „Wstecz”, rewalidacja dodatków po zmianie moda głównego,
/// bramki celowości/udostępniania/ToU oraz planowanie i wznawianie etapów wykonania.
/// </summary>
public class ModPackWizardEngineTests
{
    private static ModConfiguration Full(int id, string name, string among = "2023.6.13", string version = "1.0") => new()
    {
        Id = id,
        ModName = name,
        ModType = "full",
        ModVersion = version,
        AmongVersion = among
    };

    private static ModConfiguration Dll(int id, string name, string? among = null, string version = "1.0") => new()
    {
        Id = id,
        ModName = name,
        ModType = "dll",
        ModVersion = version,
        AmongVersion = among ?? string.Empty
    };

    private static ModPackWizardEngine Local() => new(ModPackWizardEntryMode.InstallLocal, "steam");

    [Fact]
    public void KnownServerCodeIsNeverCreatedAgainEvenWithoutCustomContent()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.CreateAndShare, "steam")
        {
            ResultPackCode = "KNOWN", PendingSharePackCode = null
        };
        engine.StartExecution();
        Assert.True(engine.PackAlreadyCreatedOnServer);
        Assert.False(engine.CanSubmitNewShareRequest);
        Assert.Equal(ModPackWizardStageState.Done,
            engine.Stages.Single(stage => stage.Id == ModPackWizardStageId.CreateSharePack).State);
    }

    [Fact]
    public void UncertainCreateRemainsBlockedAfterRestartingExecution()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.CreateAndShare, "steam")
        {
            RequiresDuplicateProbeBeforeRetry = true
        };
        engine.StartExecution();
        Assert.False(engine.CanSubmitNewShareRequest);
        Assert.True(engine.RequiresDuplicateProbeBeforeRetry);
    }

    // ── Kolejność kroków ─────────────────────────────────────────────────

    [Fact]
    public void InstallLocal_StepsIncludeGoal_And_StartAtGoal()
    {
        var engine = Local();
        Assert.Equal(ModPackWizardStep.Goal, engine.CurrentStep);
        Assert.Equal(
            new[]
            {
                ModPackWizardStep.Goal,
                ModPackWizardStep.MainMod,
                ModPackWizardStep.Addons,
                ModPackWizardStep.NameSettings,
                ModPackWizardStep.Summary,
                ModPackWizardStep.Execution
            },
            engine.Steps);
        Assert.Equal(ModPackWizardGoal.Personal, engine.Goal);
        Assert.False(engine.SharesOnline);
        Assert.True(engine.CreatesLocalInstance);
    }

    [Fact]
    public void CreateAndShare_StartsAtGoal_WithFriendsPreselected()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.CreateAndShare, "steam");
        Assert.Equal(ModPackWizardGoal.ForFriends, engine.Goal);
        Assert.True(engine.SharesOnline);
        Assert.True(engine.CreatesLocalInstance);
    }

    [Fact]
    public void ShareExisting_SkipsGoalStep_And_GoalIsLockedToFriends()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.ShareExisting, "steam");
        Assert.DoesNotContain(ModPackWizardStep.Goal, engine.Steps);
        Assert.Equal(ModPackWizardStep.MainMod, engine.CurrentStep);
        Assert.True(engine.SharesOnline);
        Assert.False(engine.CreatesLocalInstance);

        engine.SetGoal(ModPackWizardGoal.Personal);
        Assert.Equal(ModPackWizardGoal.ForFriends, engine.Goal);
    }

    // ── Nawigacja i zachowanie stanu ─────────────────────────────────────

    [Fact]
    public void Back_PreservesAllSelections()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU"), Full(2, "HS") });
        engine.SetAddonCatalog(new[] { Dll(10, "Addon A", "2023.6.13"), Dll(11, "Addon B", "2023.6.13") });

        Assert.True(engine.TryNext(out _)); // Goal → MainMod
        Assert.True(engine.TryNext(out _)); // MainMod → Addons
        engine.SetAddonChecked(10, true);
        Assert.True(engine.TryNext(out _)); // Addons → NameSettings
        engine.SetPackName("Mój zestaw", fromUser: true);
        engine.SetTtlDays(7);
        Assert.True(engine.TryNext(out _)); // NameSettings → Summary

        engine.GoBack();
        Assert.Equal(ModPackWizardStep.NameSettings, engine.CurrentStep);
        Assert.Equal("Mój zestaw", engine.PackName);
        Assert.Equal(7, engine.TtlDays);

        engine.GoBack();
        Assert.Equal(ModPackWizardStep.Addons, engine.CurrentStep);
        Assert.Contains(engine.AddonRows, r => r.Mod.Id == 10 && r.IsChecked);
    }

    [Fact]
    public void Next_KeepsDirtyStateAndValidatesPerStep()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        Assert.True(engine.TryNext(out _)); // Goal OK (Personal)

        // MainMod bez modów katalogowych (brak załadowanych) → wybrany domyślnie pierwszy
        Assert.True(engine.TryNext(out _));

        Assert.True(engine.TryNext(out _)); // Addons (brak) → NameSettings

        // Nazwa wymagana
        var advanced = engine.TryNext(out var error);
        Assert.False(advanced);
        Assert.NotNull(error);
        Assert.Equal("UI.Packs.DisplayNameRequired", error!.Key);

        engine.SetPackName("Zestaw", fromUser: true);
        Assert.True(engine.TryNext(out _));
        Assert.Equal(ModPackWizardStep.Summary, engine.CurrentStep);
        Assert.True(engine.IsDirty);
    }

    [Fact]
    public void ValidateAllContentSteps_NavigatesBackToFailingStep()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        engine.SetPackName("Zestaw", fromUser: true);
        for (var i = 0; i < 4; i++)
            Assert.True(engine.TryNext(out _));
        Assert.Equal(ModPackWizardStep.Summary, engine.CurrentStep);

        engine.SetPackName(string.Empty, fromUser: true); // użytkownik wyczyścił nazwę na podglądzie
        var error = engine.ValidateAllContentSteps();
        Assert.NotNull(error);
        Assert.Equal("UI.Packs.DisplayNameRequired", error!.Key);
        Assert.Equal(ModPackWizardStep.NameSettings, engine.CurrentStep);
    }

    [Fact]
    public void Goal_ForFriendsBlockedWhenSharingDisabled()
    {
        var engine = Local();
        engine.SharingAllowedByApp = false;
        engine.SetGoal(ModPackWizardGoal.ForFriends);
        Assert.True(engine.SharesOnline);

        var can = engine.TryNext(out var error);
        Assert.False(can);
        Assert.Equal("ModPacks.Disabled", error!.Key);
    }

    [Fact]
    public void GoalSwitch_InstallLocal_UpgradesToCreateAndShareSummaryAction()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        engine.SetPackName("Zestaw", fromUser: true);
        for (var i = 0; i < 4; i++)
            Assert.True(engine.TryNext(out _));
        Assert.Equal(ModPackWizardPrimaryAction.InstallLocal, engine.PrimaryAction);

        engine.SetGoal(ModPackWizardGoal.ForFriends);
        Assert.True(engine.SharesOnline);
        Assert.Contains(ModPackWizardStageId.CreateSharePack, engine.PlanStages().Select(s => s.Id));
    }

    // ── Mod główny: katalog + custom full ────────────────────────────────

    [Fact]
    public void MainMod_WithoutSelection_FailsWithCatalogKey()
    {
        var engine = Local();
        engine.SetCatalogFullMods(Array.Empty<ModConfiguration>());
        engine.TryNext(out _);
        var can = engine.TryNext(out var error);
        Assert.False(can);
        Assert.Equal("ModPacks.SelectFullMod", error!.Key);
    }

    [Fact]
    public void CustomFull_ConfirmGuidesThroughValidationKeys()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });

        var ok = engine.TryConfirmCustomFull(
            new ModPackWizardCustomFullDraft { GithubUrl = "http://github.com/a/b/releases/download/v/x.zip" },
            out var error);
        Assert.False(ok);
        Assert.Equal("ModPacks.CustomFull.ReleaseAssetRequired", error!.Key); // zły scheme

        ok = engine.TryConfirmCustomFull(
            new ModPackWizardCustomFullDraft
            {
                GithubUrl = "https://github.com/a/b/releases/download/v/x.zip",
                Name = "X"
            },
            out error);
        Assert.False(ok);
        Assert.Equal("ModPacks.CustomFull.AmongVersionRequired", error!.Key);

        ok = engine.TryConfirmCustomFull(
            new ModPackWizardCustomFullDraft
            {
                GithubUrl = "https://github.com/a/b/releases/download/v/x.zip",
                Name = "X",
                AmongVersion = "2023.6.13"
            },
            out error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.True(engine.UsingCustomFull);
        Assert.Equal("X", engine.ConfirmedCustomFull!.Name);

        engine.TryNext(out _);
        Assert.True(engine.TryNext(out _)); // MainMod z kartą custom przechodzi walidację
    }

    [Fact]
    public void RemoveCustomFull_ReturnsToCatalogSelection()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        engine.TryConfirmCustomFull(new ModPackWizardCustomFullDraft
        {
            GithubUrl = "https://github.com/a/b/releases/download/v/x.zip",
            Name = "X",
            AmongVersion = "2023.6.13"
        }, out _);
        Assert.True(engine.UsingCustomFull);

        engine.RemoveCustomFull();
        Assert.False(engine.UsingCustomFull);
        Assert.Null(engine.ConfirmedCustomFull);
        Assert.Equal(1, engine.SelectedCatalogModId);
    }

    [Fact]
    public void VersionOptions_DefaultsToLatest_AndProgrammaticNoDirty()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        Assert.False(engine.IsDirty);

        engine.SetVersionOptions(new[]
        {
            new ModPackWizardVersionOption("Najnowsza", "latest"),
            new ModPackWizardVersionOption("v0.9 (Among Us 2023.3.1)", "0.9", "2023.3.1")
        });
        Assert.Equal("latest", engine.SelectedVersionValue);
        Assert.False(engine.IsDirty); // załadowanie wersji to nie edycja użytkownika
    }

    // ── Rewalidacja zgodności po zmianie moda głównego ───────────────────

    [Fact]
    public void MainModChange_FlagsMismatchingAddonsForExplicitDecision()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU", "2023.6.13"), Full(2, "Old", "2022.10.1") });
        engine.SetAddonCatalog(new[] { Dll(10, "Addon X", "2023.6.13") }, new[] { 10 });
        engine.SetAddonChecked(10, true);

        engine.SetSelectedCatalogMod(2);

        var row = engine.AddonRows.Single(r => r.Mod.Id == 10);
        Assert.True(row.IsChecked); // NIE usuwamy po cichu
        Assert.Equal(ModPackWizardAddonReviewState.NeedsDecision, row.ReviewState);
        Assert.True(engine.MainModReviewBannerVisible);
    }

    [Fact]
    public void PendingDecision_BlocksNext_UntilKeepOrRemove()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU", "2023.6.13"), Full(2, "Old", "2022.10.1") });
        engine.SetAddonCatalog(new[] { Dll(10, "Addon X", "2023.6.13") }, new[] { 10 });
        engine.SetAddonChecked(10, true);
        engine.SetSelectedCatalogMod(2);

        engine.TryNext(out _); // Goal → MainMod
        var can = engine.TryNext(out var error); // MainMod → Addons
        Assert.True(can);
        Assert.Equal(ModPackWizardStep.Addons, engine.CurrentStep);

        can = engine.TryNext(out error); // Addons blokuje się na decyzji
        Assert.False(can);
        Assert.Equal("ModPacks.Wizard.Addons.UnresolvedDecisions", error!.Key);

        engine.ResolveAddonDecision(10, keep: true);
        can = engine.TryNext(out error);
        Assert.True(can);
        Assert.Null(error);
        Assert.Equal(ModPackWizardAddonReviewState.KeptWithWarning,
            engine.AddonRows.Single(r => r.Mod.Id == 10).ReviewState);
    }

    [Fact]
    public void ResolveDecision_Remove_UnchecksRow()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU", "2023.6.13"), Full(2, "Old", "2022.10.1") });
        engine.SetAddonCatalog(new[] { Dll(10, "Addon X", "2023.6.13") }, new[] { 10 });
        engine.SetAddonChecked(10, true);
        engine.SetSelectedCatalogMod(2);
        engine.ResolveAddonDecision(10, keep: false);

        var row = engine.AddonRows.Single(r => r.Mod.Id == 10);
        Assert.False(row.IsChecked);
        Assert.Equal(ModPackWizardAddonReviewState.None, row.ReviewState);
        Assert.False(engine.MainModReviewBannerVisible);
    }

    [Fact]
    public void UnknownVersions_AreUnverifiedButNotBlocking()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU", "2023.6.13"), Full(2, "Old", "2022.10.1") });
        engine.SetAddonCatalog(new[] { Dll(10, "Addon X", among: null) }, new[] { 10 });
        engine.SetAddonChecked(10, true);
        engine.SetSelectedCatalogMod(2);

        var row = engine.AddonRows.Single(r => r.Mod.Id == 10);
        Assert.Equal(ModPackWizardAddonReviewState.None, row.ReviewState);
        Assert.True(row.CompatibilityUnverified);
        Assert.NotEmpty(engine.RowsUnverified);

        engine.TryNext(out _);
        Assert.True(engine.TryNext(out _)); // nie blokuje
    }

    [Fact]
    public void CompatibleChange_AutoClearsReviewFlags()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "A", "2023.6.13"), Full(2, "B", "2022.10.1"), Full(3, "C", "2023.6.13") });
        engine.SetAddonCatalog(new[] { Dll(10, "Addon X", "2023.6.13") }, new[] { 10 });
        engine.SetAddonChecked(10, true);
        engine.SetSelectedCatalogMod(2);
        Assert.Equal(ModPackWizardAddonReviewState.NeedsDecision,
            engine.AddonRows.Single().ReviewState);

        engine.SetSelectedCatalogMod(3);
        Assert.Equal(ModPackWizardAddonReviewState.None, engine.AddonRows.Single().ReviewState);
    }

    [Fact]
    public void PackNameSuggestion_DoesNotOverwriteUserEditedName()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU"), Full(2, "HS") });
        engine.ApplySuggestedPackName("ToU");
        Assert.Equal("ToU", engine.PackName);

        engine.SetPackName("Moja nazwa", fromUser: true);
        engine.SetSelectedCatalogMod(2);
        engine.ApplySuggestedPackName("HS");
        Assert.Equal("Moja nazwa", engine.PackName);
    }

    // ── Nazwa i ustawienia ───────────────────────────────────────────────

    [Fact]
    public void TtlDays_FallsBackToThirty_ForUnknownValues()
    {
        var engine = Local();
        engine.SetTtlDays(15);
        Assert.Equal(30, engine.TtlDays);
        engine.SetTtlDays(90);
        Assert.Equal(90, engine.TtlDays);
    }

    [Fact]
    public void TouSwitch_OnlyExposedWhenSourceHasRealSnapshot()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.ShareExisting, "steam");
        engine.SetShareSources(new[]
        {
            new ModPackWizardShareSource
            {
                InstanceId = "inst-1",
                DefaultPackName = "Zestaw",
                Label = "Zestaw — ToU v1.0",
                HasTouSnapshot = false,
                TargetMod = Full(1, "ToU")
            }
        });

        Assert.False(engine.IsTouConfigAvailable);
        engine.SetIncludeTouConfig(true);
        Assert.False(engine.IncludeTouConfig); // przełącznik niepozornie niedostępny — brak fikcji

        engine.SetShareSources(new[]
        {
            new ModPackWizardShareSource
            {
                InstanceId = "inst-2",
                DefaultPackName = "Zestaw 2",
                Label = "Zestaw 2 — ToU v1.0",
                HasTouSnapshot = true,
                TargetMod = Full(1, "ToU")
            }
        });

        Assert.True(engine.IsTouConfigAvailable);
        Assert.True(engine.IncludeTouConfig); // domyślnie dołączony — tak samo jak mapuje mapper
        engine.SetIncludeTouConfig(false);
        Assert.False(engine.IncludeTouConfig);
    }

    [Fact]
    public void ShareProfile_AppliedProgrammatically_WithoutDirtying()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.CreateAndShare, "steam");
        engine.ApplyShareProfile("nick", "discord.gg/abc");
        Assert.Equal("nick", engine.CreatorName);
        Assert.Equal("discord.gg/abc", engine.DiscordInvite);
        Assert.False(engine.IsDirty);
    }

    // ── Podsumowanie i akcje ─────────────────────────────────────────────

    [Fact]
    public void Summary_PrimaryAction_DescribesEffect()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        engine.SetPackName("Zestaw", fromUser: true);
        for (var i = 0; i < 4; i++)
            Assert.True(engine.TryNext(out _));
        Assert.Equal(ModPackWizardStep.Summary, engine.CurrentStep);
        Assert.Equal(ModPackWizardPrimaryAction.InstallLocal, engine.PrimaryAction);

        engine.SetGoal(ModPackWizardGoal.ForFriends);
        Assert.Equal(ModPackWizardPrimaryAction.InstallAndShare, engine.PrimaryAction);
    }

    [Fact]
    public void ShareExisting_PrimaryActionIsCreateLink()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.ShareExisting, "steam");
        engine.SetShareSources(new[]
        {
            new ModPackWizardShareSource
            {
                InstanceId = "i",
                DefaultPackName = "Z",
                Label = "Z",
                TargetMod = Full(1, "ToU")
            }
        });
        engine.SetPackName("Z", fromUser: true);
        for (var i = 0; i < 3; i++)
            Assert.True(engine.TryNext(out _));
        Assert.Equal(ModPackWizardStep.Summary, engine.CurrentStep);
        Assert.Equal(ModPackWizardPrimaryAction.ShareExisting, engine.PrimaryAction);
    }

    // ── Etapy wykonania ──────────────────────────────────────────────────

    [Fact]
    public void PlanStages_Personal_LocalOnlyOrder()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        engine.SetPackName("Zestaw", fromUser: true);
        engine.SetAddonCatalog(new[] { Dll(10, "A", "2023.6.13") }, new[] { 10 });
        engine.SetAddonChecked(10, true);

        var stages = engine.PlanStages().Select(s => s.Id).ToList();
        Assert.Equal(
            new[]
            {
                ModPackWizardStageId.InstallMain,
                ModPackWizardStageId.InstallAddons
            },
            stages);
    }

    [Fact]
    public void PlanStages_Personal_NoAddons_NoCopyStages()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        var stages = engine.PlanStages().Select(s => s.Id).ToList();
        Assert.Equal(new[] { ModPackWizardStageId.InstallMain }, stages);
    }

    [Fact]
    public void PlanStages_ForFriends_MatchesActualServiceFlow()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.CreateAndShare, "steam");
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        engine.CustomDllRows.Add(new ModPackWizardCustomDllRow
        {
            FilePath = @"C:\x\y.dll",
            FileName = "y.dll",
            Sha256 = "abc"
        });

        var stages = engine.PlanStages().Select(s => s.Id).ToList();
        Assert.Equal(
            new[]
            {
                ModPackWizardStageId.InstallMain,
                ModPackWizardStageId.CopyCustomFiles,
                ModPackWizardStageId.CreateSharePack,
                ModPackWizardStageId.SendCustomContent,
                ModPackWizardStageId.FinalValidation
            },
            stages);
    }

    [Fact]
    public void PlanStages_ShareExisting_HasNoInstallStages()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.ShareExisting, "steam");
        var stages = engine.PlanStages().Select(s => s.Id).ToList();
        Assert.Equal(new[] { ModPackWizardStageId.CreateSharePack }, stages);
    }

    [Fact]
    public void StartExecution_MarksCompletedStages_DoesNotDuplicateWork()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.CreateAndShare, "steam");
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        engine.CustomDllRows.Add(new ModPackWizardCustomDllRow
        {
            FilePath = @"C:\x\y.dll",
            FileName = "y.dll",
            Sha256 = "abc"
        });
        engine.CreatedInstanceId = "inst-created";
        engine.PendingSharePackCode = "ABCD12";

        engine.StartExecution();

        Assert.Equal(ModPackWizardStageState.Done,
            engine.Stages.Single(s => s.Id == ModPackWizardStageId.InstallMain).State);
        Assert.Equal(ModPackWizardStageState.Done,
            engine.Stages.Single(s => s.Id == ModPackWizardStageId.CreateSharePack).State);
        Assert.Equal("ModPacks.Wizard.Stage.ResumeShare",
            engine.Stages.Single(s => s.Id == ModPackWizardStageId.CreateSharePack).TitleKey);
        Assert.Contains(ModPackWizardStageId.SendCustomContent, engine.Stages.Select(s => s.Id));
        Assert.Contains(ModPackWizardStageId.FinalValidation, engine.Stages.Select(s => s.Id));
        Assert.Equal(ModPackWizardStageState.Pending,
            engine.Stages.Single(s => s.Id == ModPackWizardStageId.SendCustomContent).State);
    }

    [Fact]
    public void StartExecution_RetryResetsFailureLists()
    {
        var engine = Local();
        engine.FailedDllNames.Add("Stary błąd");
        engine.CreatedInstanceId = "x";
        engine.StartExecution();
        Assert.Empty(engine.FailedDllNames);
    }

    [Fact]
    public void BuildSummary_ReflectsRealFlagsForRender()
    {
        var engine = new ModPackWizardEngine(ModPackWizardEntryMode.CreateAndShare, "epic");
        engine.SetCatalogFullMods(new[] { Full(1, "ToU", "2023.6.13") });
        engine.SetPackName("Zestaw PL", fromUser: true);
        engine.SetAddonCatalog(new[] { Dll(10, "A", "2023.6.13"), Dll(11, "B") }, new[] { 10 });

        var summary = engine.BuildSummary();
        Assert.True(summary.SharesOnline);
        Assert.True(summary.CreatesLocalInstance);
        Assert.Contains("ToU", summary.MainLine);
        Assert.Single(summary.AddonLines);
        Assert.False(summary.IncludesTouConfig); // nowy zestaw bez snapshotu — nie kłamiemy
        Assert.Equal("ModPacks.Wizard.Summary.UploadNotice", summary.UploadScope);

        var personal = Local();
        personal.SetCatalogFullMods(new[] { Full(1, "ToU") });
        Assert.Null(personal.BuildSummary().UploadScope); // lokalnie nic nie lecim na serwer
    }

    [Fact]
    public void DirtyAndCloseConfirmation_Matrix()
    {
        var engine = Local();
        Assert.False(engine.IsDirty);
        Assert.False(engine.RequiresCloseConfirmation);

        engine.SetTtlDays(7);
        Assert.True(engine.IsDirty);
        Assert.True(engine.RequiresCloseConfirmation);

        // Uruchomienie wykonania — potwierdzenie wymaga tylko gdy REALNIE coś trwa
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        engine.StartExecution();
        Assert.False(engine.RequiresCloseConfirmation); // start bez rozpoczętych operacji

        engine.SetStageRunning(ModPackWizardStageId.InstallMain, cancellable: false);
        Assert.True(engine.RequiresCloseConfirmation);
        Assert.False(engine.CanGoBack); // Wstecz zablokowane na kroku wykonania
    }

    [Fact]
    public void BackBlockedOnceExecutionStarted()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        engine.SetPackName("Zestaw", fromUser: true);
        for (var i = 0; i < 4; i++)
            Assert.True(engine.TryNext(out _));
        Assert.Equal(ModPackWizardStep.Summary, engine.CurrentStep);
        Assert.True(engine.CanGoBack);

        Assert.True(engine.TryNext(out _)); // Summary → Execution
        Assert.Equal(ModPackWizardStep.Execution, engine.CurrentStep);
        engine.StartExecution();
        Assert.False(engine.CanGoBack);
        engine.GoBack();
        Assert.Equal(ModPackWizardStep.Execution, engine.CurrentStep);
    }

    [Fact]
    public void ExecutionAllDone_DrivesFinishAction()
    {
        var engine = Local();
        engine.SetCatalogFullMods(new[] { Full(1, "ToU") });
        engine.SetPackName("Zestaw", fromUser: true);
        for (var i = 0; i < 5; i++)
            Assert.True(engine.TryNext(out _));
        Assert.Equal(ModPackWizardStep.Execution, engine.CurrentStep);

        engine.StartExecution();
        engine.SetStageRunning(ModPackWizardStageId.InstallMain, cancellable: false);
        engine.SetStageDone(ModPackWizardStageId.InstallMain);
        engine.MarkExecutionSettled(finished: true);

        Assert.True(engine.ExecutionAllDone);
        Assert.True(engine.ExecutionFinished);
        Assert.Equal(ModPackWizardPrimaryAction.Finish, engine.PrimaryAction);
    }
}
