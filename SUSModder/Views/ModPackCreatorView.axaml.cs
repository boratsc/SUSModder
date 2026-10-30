using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;
using SUSModder.Core.Utilities;
using SUSModder.ViewModels.ModPackWizard;

namespace SUSModder.Views;

/// <summary>
/// Prowadzony kreator zestawów modpacków (3.1-02). Shell modalu renderuje kroki z
/// <see cref="ModPackWizardEngine"/>; wykonanie odpytuje ISTNIEJĄCE usługi Core
/// (instalator instancji, IModPackService) w jawnych etapach, bez drugiego instalatora
/// i bez obietnicy rollbacku, którego instalator nie zapewnia.
/// Kontrakt hosta: ModalTitle, Completed i konstruktor.
/// </summary>
public partial class ModPackCreatorView : UserControl
{
    /// <summary>Ścieżka linku udostępnienia — identyczna jak fallback ModPackResultViewModel.</summary>
    private const string ShareUrlBase = "https://susmodder.app/pack/";

    private readonly ModPackCreatorMode _mode;
    private readonly IModPackService _modPackService;
    private readonly DllModificationService _dllService;
    private readonly ModInstanceInstaller? _instanceInstaller;
    private readonly IModInstanceRepository _instances;
    private readonly InstanceToModPackMapper _mapper;
    private readonly ModVersionService _versionService;
    private readonly AmongUsManifestService? _amongUsManifestService;
    private readonly ILocalizationService _loc;
    private readonly UserSettingsService _userSettingsService = new();
    private readonly string _platform;
    private readonly string? _preselectedInstanceId;
    private readonly int? _preselectedCatalogModId;

    private ModPackWizardEngine? _engine;
    private bool _closeAfterExecution;
    private List<ModConfiguration> _dllCatalog = new();
    private readonly List<string> _amongVersionValues = new();
    private bool _amongVersionsLoaded;

    private CancellationTokenSource? _runCts;
    private bool _isExecuting;
    private bool _syncing;
    private bool _detached;
    private ModPackCreateResult? _shareResult;
    private ModInstance? _runInstance;

    // Podkrok: własny mod główny z GitHuba
    private bool _customFullSubOpen;
    private bool _customFullUrlPhase;
    private int _githubSubEditingIndex = -1; // -1 = nowy wpis, >=0 edycja istniejącego, -2 = panel zamknięty

    public string ModalTitle { get; private set; } = string.Empty;

    public event Action<ModPackCreatorDialogResult?>? Completed;

    /// <summary>Konstruktor wymagany przez loader XAML (design-time / AVLN3001).</summary>
    public ModPackCreatorView()
    {
        _mode = ModPackCreatorMode.ShareExisting;
        _modPackService = null!;
        _dllService = null!;
        _instanceInstaller = null;
        _instances = null!;
        _mapper = null!;
        _versionService = null!;
        _amongUsManifestService = null;
        _loc = null!;
        _platform = string.Empty;
        InitializeComponent();
    }

    public ModPackCreatorView(
        ModPackCreatorMode mode,
        IModPackService modPackService,
        DllModificationService dllService,
        IModInstanceRepository instances,
        InstanceToModPackMapper mapper,
        IConfiguration configuration,
        IDiagnosticsOutput diagnostics,
        ILocalizationService loc,
        string platform,
        ModInstanceInstaller? instanceInstaller = null,
        string? preselectedInstanceId = null,
        int? preselectedCatalogModId = null)
    {
        _mode = mode;
        _modPackService = modPackService;
        _dllService = dllService;
        _instances = instances;
        _mapper = mapper;
        _instanceInstaller = instanceInstaller;
        _versionService = new ModVersionService(diagnostics);
        _amongUsManifestService = new AmongUsManifestService(configuration);
        _loc = loc;
        _platform = platform;
        _preselectedInstanceId = preselectedInstanceId;
        _preselectedCatalogModId = preselectedCatalogModId;

        InitializeComponent();

        var settings = SafeLoadSettings();
        _engine = new ModPackWizardEngine(MapEntryMode(mode), platform)
        {
            SharingAllowedByApp = settings?.ModPacksEnabled ?? true
        };
        _engine.Changed += OnEngineChanged;

        ModalTitle = _loc.Get(_mode == ModPackCreatorMode.InstallLocal
            ? "UI.Packs.CreateLocalTitle"
            : _mode == ModPackCreatorMode.CreateAndShare
                ? "UI.Packs.CreateAndShareTitle"
                : "ModPacks.CreatorTitle");

        WireControls();
        ApplyStaticLabels();

        if (_engine.SharesOnline)
        {
            _engine.ApplyShareProfile(settings?.ModPackShareCreatorName, settings?.ModPackShareDiscordInvite);
        }

        Loaded += async (_, _) => await LoadDataAsync();
        DetachedFromVisualTree += (_, _) =>
        {
            _detached = true;
            try { _runCts?.Cancel(); } catch { /* best effort */ }
        };
    }

    private static ModPackWizardEntryMode MapEntryMode(ModPackCreatorMode mode) => mode switch
    {
        ModPackCreatorMode.InstallLocal => ModPackWizardEntryMode.InstallLocal,
        ModPackCreatorMode.CreateAndShare => ModPackWizardEntryMode.CreateAndShare,
        _ => ModPackWizardEntryMode.ShareExisting
    };

    private ModPackWizardEngine Engine => _engine
        ?? throw new InvalidOperationException("ModPackCreatorView is in design-time mode.");

    private UserSettings? SafeLoadSettings()
    {
        try
        {
            return _userSettingsService.LoadUserSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModPackWizard] Failed to load settings: {ex.Message}");
            return null;
        }
    }

    private void Complete(ModPackCreatorDialogResult? result) => Completed?.Invoke(result);

    // ══════════════════════════════════════════════════════════════════
    //  Ładowanie danych (istniejące usługi, bez nowych formatów)
    // ══════════════════════════════════════════════════════════════════

    private async Task LoadDataAsync()
    {
        if (_engine == null || _detached)
            return;

        _dllCatalog = _dllService.GetDllMods();

        if (Engine.CreatesLocalInstance)
        {
            if (_instanceInstaller == null)
            {
                ShowError(_loc.Get("UI.Packs.InstallerUnavailable"));
            }

            var configs = ConfigManager.LoadConfig();
            var catalogFull = configs
                .Where(m => m.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) && m.Id > 0)
                .OrderBy(m => m.ModName)
                .ToList();
            Engine.SetCatalogFullMods(catalogFull, _preselectedCatalogModId);
            Engine.SetAddonCatalog(_dllCatalog);
            await RefreshVersionsForSelectedMainAsync();
        }
        else
        {
            var sources = BuildShareSources();
            Engine.SetShareSources(sources, _preselectedInstanceId, _preselectedCatalogModId);
            await ReloadAddonsForSourceAsync(programmatic: true);
            if (sources.Count == 0)
                ShowError(_loc.Get("UI.Packs.NoShareSources"));
        }

        RenderAll();
    }

    private List<ModPackWizardShareSource> BuildShareSources()
    {
        var sources = new List<ModPackWizardShareSource>();
        var catalog = ConfigManager.LoadConfig();
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in _instances.GetPackInstances().OrderBy(i => i.DisplayName))
        {
            if (string.IsNullOrWhiteSpace(instance.InstallPath))
                continue;

            usedPaths.Add(instance.InstallPath);
            var cat = catalog.FirstOrDefault(c => c.Id == instance.BaseModId);
            sources.Add(new ModPackWizardShareSource
            {
                InstanceId = instance.InstanceId,
                DefaultPackName = instance.DisplayName,
                Label = $"{instance.DisplayName} — {instance.BaseModName} v{instance.FullModVersion}",
                HasTouSnapshot = HasTouSnapshot(instance.InstanceId),
                TargetMod = new ModConfiguration
                {
                    Id = instance.BaseModId,
                    ModName = instance.BaseModName,
                    ModType = "full",
                    ModVersion = instance.FullModVersion,
                    AmongVersion = instance.AmongVersion,
                    InstallPath = instance.InstallPath,
                    GitHubRepoOrLink = cat?.GitHubRepoOrLink ?? string.Empty,
                    EpicGitHubRepoOrLink = cat?.EpicGitHubRepoOrLink
                }
            });
        }

        foreach (var legacy in _dllService.GetAvailableFullMods())
        {
            if (string.IsNullOrWhiteSpace(legacy.InstallPath) || usedPaths.Contains(legacy.InstallPath))
                continue;

            sources.Add(new ModPackWizardShareSource
            {
                InstanceId = null,
                DefaultPackName = legacy.ModName,
                Label = $"{legacy.ModName} ({_loc.Get("UI.Packs.Origin.Legacy")}) v{legacy.ModVersion}",
                HasTouSnapshot = false,
                TargetMod = legacy
            });
        }

        return sources;
    }

    /// <summary>Realna zdolność ToU: przełącznik pokazujemy TYLKO gdy instancja ma zapisany snapshot.</summary>
    private bool HasTouSnapshot(string instanceId)
    {
        try
        {
            return _instances.GetConfigs(instanceId).Any(c =>
                string.Equals(c.ConfigType, ModInstanceTouConfigService.ConfigType, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.ConfigJson));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModPackWizard] ToU snapshot probe failed: {ex.Message}");
            return false;
        }
    }

    private async Task ReloadAddonsForSourceAsync(bool programmatic)
    {
        if (Engine.EntryMode != ModPackWizardEntryMode.ShareExisting)
        {
            Engine.SetAddonCatalog(_dllCatalog, preselectedIds: Array.Empty<int>(), programmatic: programmatic);
            return;
        }

        var source = Engine.SelectedShareSource;
        if (source == null)
            return;

        IReadOnlyCollection<int> prechecked;
        if (!string.IsNullOrEmpty(source.InstanceId))
        {
            prechecked = _instances.GetDlls(source.InstanceId)
                .Where(d => d.DllModId.HasValue)
                .Select(d => d.DllModId!.Value)
                .ToList();
        }
        else
        {
            prechecked = await _dllService.GetInstalledDllIdsAsync(source.TargetMod);
        }

        Engine.SetAddonCatalog(_dllCatalog, prechecked, programmatic: programmatic);
    }

    private async Task RefreshVersionsForSelectedMainAsync()
    {
        var mod = Engine.SelectedCatalogMod;
        if (Engine.EntryMode != ModPackWizardEntryMode.ShareExisting && mod != null)
        {
            var options = new List<ModPackWizardVersionOption>
            {
                new(_loc.Get("ModPacks.VersionLatestOption"), "latest", null)
            };
            try
            {
                var versions = await _versionService.GetVersionHistoryAsync(mod.Id);
                options.AddRange(versions.Select(v =>
                    new ModPackWizardVersionOption(v.DisplayText, v.ModVersion, v.AmongVersion)));
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(mod.ModVersion))
                {
                    options.Add(new ModPackWizardVersionOption(
                        $"{mod.ModName} v{mod.ModVersion}", mod.ModVersion, mod.AmongVersion));
                }
            }

            Engine.SetVersionOptions(options);
            Engine.ApplySuggestedPackName(mod.ModName);
        }
    }

    private async Task EnsureAmongVersionsLoadedAsync()
    {
        if (_amongVersionsLoaded || _amongUsManifestService == null)
            return;

        _amongVersionValues.Clear();
        try
        {
            var versions = await _amongUsManifestService.GetAmongUsVersionValuesAsync();
            _amongVersionValues.AddRange(versions);
            _amongVersionsLoaded = _amongVersionValues.Count > 0;
        }
        catch
        {
            ShowError(_loc.Get("ModPacks.CustomFull.AmongVersionLoadFailed"));
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Wiązanie kontrolek z silnikiem
    // ══════════════════════════════════════════════════════════════════

    private void WireControls()
    {
        GoalFriendsOption.IsCheckedChanged += (_, _) =>
        {
            if (GoalFriendsOption.IsChecked == true && !_syncing)
                Engine.SetGoal(ModPackWizardGoal.ForFriends);
        };
        GoalSelfOption.IsCheckedChanged += (_, _) =>
        {
            if (GoalSelfOption.IsChecked == true && !_syncing)
                Engine.SetGoal(ModPackWizardGoal.Personal);
        };

        MainModList.SelectionChanged += async (_, _) =>
        {
            if (_syncing || _isExecuting)
                return;
            var index = MainModList.SelectedIndex;
            if (index < 0)
                return;

            if (Engine.EntryMode == ModPackWizardEntryMode.ShareExisting)
            {
                var previousSource = Engine.SelectedShareSourceIndex;
                Engine.SetSelectedShareSource(index);
                await ReloadAddonsForSourceAsync(programmatic: true);
                var source = Engine.SelectedShareSource;
                if (source != null)
                    Engine.ApplySuggestedPackName(source.DefaultPackName);
                Engine.AcknowledgeSourceChangeReload();
                if (previousSource != index)
                    _showSourceReloadedNote = true;
            }
            else
            {
                var mods = Engine.CatalogFullMods;
                if (index >= mods.Count)
                    return;
                Engine.SetSelectedCatalogMod(mods[index].Id);
                await RefreshVersionsForSelectedMainAsync();
            }

            RenderAll();
        };

        VersionCombo.SelectionChanged += (_, _) =>
        {
            if (_syncing)
                return;
            var options = Engine.VersionOptions;
            var idx = VersionCombo.SelectedIndex;
            if (idx >= 0 && idx < options.Count)
            {
                Engine.SetSelectedVersion(options[idx].Value);
                RenderAll();
            }
        };

        PackNameBox.TextChanged += (_, _) =>
        {
            if (!_syncing)
                Engine.SetPackName(PackNameBox.Text, fromUser: true);
        };

        CreatorNameBox.TextChanged += (_, _) =>
        {
            if (!_syncing)
                Engine.SetCreatorName(CreatorNameBox.Text);
        };

        DiscordInviteBox.TextChanged += (_, _) =>
        {
            if (!_syncing)
                Engine.SetDiscordInvite(DiscordInviteBox.Text);
        };

        TtlCombo.SelectionChanged += (_, _) =>
        {
            if (_syncing || TtlCombo.SelectedIndex < 0)
                return;
            var days = TtlCombo.SelectedIndex switch { 0 => 7, 2 => 90, _ => 30 };
            Engine.SetTtlDays(days);
            RenderAll();
        };

        UseCustomFullCheck.IsCheckedChanged += (sender, _) =>
        {
            if (_syncing) return;
            if (UseCustomFullCheck.IsChecked == true)
            {
                if (Engine.UsingCustomFull) CustomFullEditButton_Click(sender, new RoutedEventArgs());
                else AddCustomFullButton_Click(sender, new RoutedEventArgs());
            }
            else CustomFullRemoveButton_Click(sender, new RoutedEventArgs());
        };

        IncludeTouCheck.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing)
                Engine.SetIncludeTouConfig(IncludeTouCheck.IsChecked == true);
        };

        UseGithubDllCheck.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing)
                Engine.SetUseGithubDllSources(UseGithubDllCheck.IsChecked == true);
        };

        CustomFullUrlBox.TextChanged += (_, _) => UpdateCustomFullSubPhase();
        GithubUrlBox.TextChanged += (_, _) =>
        {
            if (!_syncing)
                SetGithubSubPhase(_githubUrlPhase);
        };
    }

    private void ApplyStaticLabels()
    {
        CustomFullCardBadge.Text = _loc.Get("ModPacks.Wizard.OutOfCatalog");
        CustomFullEditButton.Content = _loc.Get("ModPacks.Wizard.Change");
        CustomFullRemoveButton.Content = _loc.Get("ModPacks.CustomDlls.RemoveFile");
        CustomFullBackButton.Content = _loc.Get("UI.Buttons.Cancel");
        CustomFullConfirmButton.Content = _loc.Get("ModPacks.Wizard.Confirm");
        CustomFullSubHint.Text = _loc.Get("ModPacks.Wizard.Main.UrlExample") + "\n" +
                                 _loc.Get("ModPacks.CustomFull.GitHubOnlyWarning");
        GithubSubBackButton.Content = _loc.Get("UI.Buttons.Cancel");
        GithubSubConfirmButton.Content = _loc.Get("ModPacks.Wizard.Confirm");
        GithubSubHint.Text = _loc.Get("ModPacks.Wizard.Github.UrlExample") + "\n" +
                             _loc.Get("ModPacks.CustomGitHub.GitHubOnlyWarning");
    }

    private void OnEngineChanged()
    {
        if (_detached)
            return;
        Dispatcher.UIThread.Post(RenderAll);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Renderowanie kroków
    // ══════════════════════════════════════════════════════════════════

    private void RenderAll()
    {
        if (_engine == null || _detached)
            return;

        var previous = _syncing;
        _syncing = true;
        try
        {
            RenderHeader();
            RenderStepVisibility();
            RenderGoal();
            RenderMainMod();
            RenderAddons();
            RenderNameSettings();
            RenderSummary();
            RenderExecution();
            RenderFooter();
        }
        finally
        {
            _syncing = previous;
        }
    }

    private void RenderHeader()
    {
        StepCounterText.Text = _loc.GetFormatted("ModPacks.Wizard.StepCounter",
            Engine.StepIndex + 1, Engine.Steps.Count);
        StepTitleText.Text = _loc.Get($"ModPacks.Wizard.Step.{Engine.CurrentStep}.Title");
        StepDescText.Text = _loc.Get($"ModPacks.Wizard.Step.{Engine.CurrentStep}.Desc");
    }

    private void RenderStepVisibility()
    {
        GoalPanel.IsVisible = Engine.CurrentStep == ModPackWizardStep.Goal;
        MainModPanel.IsVisible = Engine.CurrentStep == ModPackWizardStep.MainMod;
        AddonsPanel.IsVisible = Engine.CurrentStep == ModPackWizardStep.Addons;
        NameSettingsPanel.IsVisible = Engine.CurrentStep == ModPackWizardStep.NameSettings;
        SummaryPanel.IsVisible = Engine.CurrentStep == ModPackWizardStep.Summary;
        ExecutionPanel.IsVisible = Engine.CurrentStep == ModPackWizardStep.Execution;

        // Notka o przeładowaniu dodatków znika, gdy użytkownik opuści krok „Dodatki”.
        if (_lastRenderedStep != Engine.CurrentStep)
        {
            if (_lastRenderedStep == ModPackWizardStep.Addons)
                _showSourceReloadedNote = false;
            _lastRenderedStep = Engine.CurrentStep;
        }
    }

    private void RenderGoal()
    {
        GoalFriendsTitle.Text = _loc.Get("ModPacks.Wizard.Goal.Friends");
        GoalFriendsDesc.Text = _loc.Get("ModPacks.Wizard.Goal.FriendsDesc");
        GoalSelfTitle.Text = _loc.Get("ModPacks.Wizard.Goal.Self");
        GoalSelfDesc.Text = _loc.Get("ModPacks.Wizard.Goal.SelfDesc");

        GoalFriendsOption.IsChecked = Engine.Goal == ModPackWizardGoal.ForFriends;
        GoalSelfOption.IsChecked = Engine.Goal == ModPackWizardGoal.Personal;
        GoalFriendsOption.IsEnabled = Engine.SharingAllowedByApp;

        GoalShareDisabledText.IsVisible = !Engine.SharingAllowedByApp;
        if (!Engine.SharingAllowedByApp)
            GoalShareDisabledText.Text = _loc.Get("ModPacks.Disabled");

        SetCardSelected(GoalFriendsCard, Engine.Goal == ModPackWizardGoal.ForFriends);
        SetCardSelected(GoalSelfCard, Engine.Goal == ModPackWizardGoal.Personal);
    }

    private static void SetCardSelected(Border card, bool selected)
    {
        if (selected)
        {
            if (!card.Classes.Contains("selected"))
                card.Classes.Add("selected");
        }
        else
        {
            card.Classes.Remove("selected");
        }
    }

    private void RenderMainMod()
    {
        var isShare = Engine.EntryMode == ModPackWizardEntryMode.ShareExisting;
        MainListLabel.Text = _loc.Get(isShare ? "UI.Packs.ShareSourceLabel" : "ModPacks.FullModLabel");

        var labels = isShare
            ? Engine.ShareSources.Select(s => s.Label).ToList()
            : Engine.CatalogFullMods
                .Select(m => string.IsNullOrWhiteSpace(m.Description)
                    ? $"{m.ModName} (v{m.ModVersion})"
                    : $"{m.ModName} (v{m.ModVersion}) — {Truncate(m.Description, 90)}")
                .ToList();

        // Reassignujemy ItemsSource tylko przy realnej zmianie — bez resetu fokus/scroll.
        if (_mainLabelsCache == null || !_mainLabelsCache.SequenceEqual(labels))
        {
            _mainLabelsCache = labels;
            MainModList.ItemsSource = labels;
        }

        CatalogSelectionGrid.IsVisible = !Engine.UsingCustomFull && !_customFullSubOpen;
        MainModList.IsEnabled = !_isExecuting;

        var desiredIndex = isShare
            ? Engine.SelectedShareSourceIndex
            : IndexOfMain(Engine.CatalogFullMods, Engine.SelectedCatalogModId);
        if (desiredIndex >= 0 && MainModList.SelectedIndex != desiredIndex)
            MainModList.SelectedIndex = desiredIndex;
        SelectedMainText.Text = desiredIndex >= 0 && desiredIndex < labels.Count
            ? _loc.GetFormatted("ModPacks.Wizard.Main.Selected", labels[desiredIndex])
            : _loc.Get("ModPacks.Wizard.Main.SelectPrompt");

        VersionPanel.IsVisible = !isShare && !Engine.UsingCustomFull;
        if (VersionPanel.IsVisible)
        {
            var displays = Engine.VersionOptions.Select(o => o.DisplayText).ToList();
            if (_versionDisplaysCache == null || !_versionDisplaysCache.SequenceEqual(displays))
            {
                _versionDisplaysCache = displays;
                VersionCombo.ItemsSource = displays;
            }

            var vIdx = IndexOfVersion(Engine.VersionOptions, Engine.SelectedVersionValue);
            VersionCombo.SelectedIndex = vIdx >= 0 ? vIdx : 0;
        }

        var allowCustomFull = Engine.CreatesLocalInstance && !_isExecuting;
        UseCustomFullCheck.IsVisible = Engine.CreatesLocalInstance;
        UseCustomFullCheck.IsEnabled = allowCustomFull;
        UseCustomFullCheck.IsChecked = Engine.UsingCustomFull || _customFullSubOpen;

        CustomFullCard.IsVisible = Engine.UsingCustomFull && Engine.ConfirmedCustomFull != null && !_customFullSubOpen;
        if (CustomFullCard.IsVisible && Engine.ConfirmedCustomFull != null)
        {
            var c = Engine.ConfirmedCustomFull;
            CustomFullCardTitle.Text = $"{c.Name} v{(string.IsNullOrWhiteSpace(c.Version) ? "custom" : c.Version)} · Among Us {c.AmongVersion}";
            CustomFullEditButton.IsEnabled = !_isExecuting;
            CustomFullRemoveButton.IsEnabled = !_isExecuting;
        }

        CustomFullSubPanel.IsVisible = _customFullSubOpen;
        RenderCustomFullSubPanel();
    }

    private List<string>? _mainLabelsCache;
    private List<string>? _versionDisplaysCache;
    private bool _showSourceReloadedNote;
    private ModPackWizardStep? _lastRenderedStep;

    private static int IndexOfMain(IReadOnlyList<ModConfiguration> mods, int id)
    {
        for (var i = 0; i < mods.Count; i++)
            if (mods[i].Id == id)
                return i;
        return -1;
    }

    private static int IndexOfVersion(IReadOnlyList<ModPackWizardVersionOption> options, string value)
    {
        for (var i = 0; i < options.Count; i++)
            if (string.Equals(options[i].Value, value, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    private void RenderCustomFullSubPanel()
    {
        if (!_customFullSubOpen)
            return;

        CustomFullSubTitle.Text = _loc.Get("ModPacks.CustomFull.Title");
        CustomFullFieldsPanel.IsVisible = !_customFullUrlPhase;

        if (_customFullUrlPhase)
        {
            CustomFullBackButton.Content = _loc.Get("UI.Buttons.Cancel");
            CustomFullConfirmButton.Content = _loc.Get("ModPacks.Wizard.Next");
            var url = CustomFullUrlBox.Text;
            CustomFullConfirmButton.IsEnabled = ModPackWizardSourceGuard.IsAllowedGithubReleaseAssetUrl(url);
            CustomFullSubError.IsVisible = !string.IsNullOrWhiteSpace(url) && !CustomFullConfirmButton.IsEnabled;
            if (CustomFullSubError.IsVisible)
                CustomFullSubError.Text = _loc.Get("ModPacks.CustomFull.ReleaseAssetRequired");
        }
        else
        {
            CustomFullBackButton.Content = _loc.Get("ModPacks.Wizard.Back");
            CustomFullConfirmButton.Content = _loc.Get("ModPacks.Wizard.Confirm");
            CustomFullConfirmButton.IsEnabled = true;
            CustomFullSubError.IsVisible = false;

            if (CustomFullAmongVersionCombo.Items.Count == 0 && _amongVersionValues.Count > 0)
            {
                foreach (var v in _amongVersionValues)
                    CustomFullAmongVersionCombo.Items.Add(v);
            }
            CustomFullAmongInfo.Text = _loc.Get("ModPacks.Wizard.Main.AmongVersionWhy");
        }
    }

    private void RenderAddons()
    {
        AddonsDescText.Text = _loc.Get("ModPacks.Wizard.Addons.Desc");

        var pending = Engine.RowsPendingDecision;
        var unverified = Engine.RowsUnverified;
        var reviewVisible = Engine.MainModReviewBannerVisible && (pending.Count > 0 || unverified.Count > 0);
        AddonsReviewBanner.IsVisible = reviewVisible || _showSourceReloadedNote;
        if (AddonsReviewBanner.IsVisible)
        {
            AddonsReviewTitle.Text = reviewVisible
                ? _loc.Get("ModPacks.Wizard.Addons.ReviewTitle")
                : _loc.Get("ModPacks.Wizard.Addons.SourceReloaded");
        }

        if (reviewVisible)
        {
            AddonsReviewRows.Children.Clear();
            foreach (var row in pending)
            {
                var line = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Avalonia.Thickness(0, 2, 0, 2)
                };
                line.Children.Add(new TextBlock
                {
                    Text = $"{row.Mod.ModName} v{row.Mod.ModVersion} — {_loc.Get("ModPacks.Wizard.Addons.VersionMismatch")}",
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
                var keep = new Button
                {
                    Content = _loc.Get("ModPacks.Wizard.Addons.Keep"),
                    Padding = new Avalonia.Thickness(8, 3),
                    Tag = row.Mod.Id
                };
                keep.Click += (_, _) => { if (keep.Tag is int id) Engine.ResolveAddonDecision(id, keep: true); };
                var drop = new Button
                {
                    Content = _loc.Get("ModPacks.CustomDlls.RemoveFile"),
                    Padding = new Avalonia.Thickness(8, 3),
                    Tag = row.Mod.Id
                };
                drop.Click += (_, _) => { if (drop.Tag is int id) Engine.ResolveAddonDecision(id, keep: false); };
                line.Children.Add(keep);
                line.Children.Add(drop);
                AddonsReviewRows.Children.Add(line);
            }

            AddonsReviewUnverified.IsVisible = unverified.Count > 0;
            if (unverified.Count > 0)
                AddonsReviewUnverified.Text = _loc.Get("ModPacks.Wizard.Addons.UnverifiedNote") + " " +
                                              string.Join(", ", unverified.Select(r => r.Mod.ModName));
        }

        DllModsPanel.Children.Clear();
        foreach (var row in Engine.AddonRows)
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            var cb = new CheckBox
            {
                Content = row.ReviewState == ModPackWizardAddonReviewState.NeedsDecision
                    ? $"{row.Mod.ModName} v{row.Mod.ModVersion} — ⚠"
                    : $"{row.Mod.ModName} v{row.Mod.ModVersion}",
                IsChecked = row.IsChecked,
                IsEnabled = !_isExecuting
            };
            cb.IsCheckedChanged += (_, _) =>
            {
                if (!_syncing)
                    Engine.SetAddonChecked(row.Mod.Id, cb.IsChecked == true);
            };
            line.Children.Add(cb);
            if (row.ReviewState == ModPackWizardAddonReviewState.KeptWithWarning)
            {
                line.Children.Add(new TextBlock
                {
                    Text = _loc.Get("ModPacks.Wizard.Addons.KeptWithWarning"),
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            DllModsPanel.Children.Add(line);
        }

        // Pliki DLL spoza katalogu — widoczne gdy powstaje instancja lokalna lub zestaw idzie na serwer.
        CustomDllPanel.IsVisible = Engine.CreatesLocalInstance || Engine.SharesOnline;
        CustomDllTitle.Text = _loc.Get("ModPacks.CustomDlls.Title");
        CustomDllNoticeText.Text = _loc.Get(
            Engine.SharesOnline
                ? Engine.CreatesLocalInstance
                    ? "ModPacks.CustomDlls.CreateAndShareNotice"
                    : "ModPacks.CustomDlls.ServerScanNotice"
                : "ModPacks.CustomDlls.LocalNotice");
        RefreshCustomDllFilesPanel();

        GithubDllSection.IsVisible = Engine.SharesOnline && !_isExecuting;
        UseGithubDllCheck.IsChecked = Engine.UseGithubDllSources;
        CustomGithubDllPanel.IsVisible = Engine.UseGithubDllSources;
        RefreshGithubDllEntriesPanel();
    }

    private void RenderNameSettings()
    {
        PackNameLabel.Text = _loc.Get(Engine.SharesOnline
            ? "ModPacks.PackNameLabel"
            : "UI.Packs.LocalDisplayNameLabel");

        if (PackNameBox.Text != Engine.PackName)
            PackNameBox.Text = Engine.PackName;

        var suggestion = Engine.EntryMode == ModPackWizardEntryMode.ShareExisting
            ? Engine.SelectedShareSource?.DefaultPackName
            : Engine.SelectedCatalogMod?.ModName ?? Engine.ConfirmedCustomFull?.Name;
        PackNameBox.PlaceholderText = string.IsNullOrWhiteSpace(suggestion) ? null : suggestion;
        PackNameHint.Text = _loc.Get("ModPacks.Wizard.Name.Hint");

        IncludeTouCheck.IsVisible = Engine.IsTouConfigAvailable;
        IncludeTouCheck.IsChecked = Engine.IncludeTouConfig;
        IncludeTouCheck.IsEnabled = !_isExecuting;

        // Brak przełącznika gdy brak snapshotu — informacja zamiast pozornie działającej opcji.
        TouUnavailableHint.IsVisible = Engine.SharesOnline && !Engine.IsTouConfigAvailable &&
                                       Engine.EntryMode == ModPackWizardEntryMode.ShareExisting;
        if (TouUnavailableHint.IsVisible)
            TouUnavailableHint.Text = _loc.Get("ModPacks.Wizard.Tou.Unavailable");


        ShareOnlyPanel.IsVisible = Engine.SharesOnline;
        if (CreatorNameBox.Text != (Engine.CreatorName ?? string.Empty))
            CreatorNameBox.Text = Engine.CreatorName ?? string.Empty;
        if (DiscordInviteBox.Text != (Engine.DiscordInvite ?? string.Empty))
            DiscordInviteBox.Text = Engine.DiscordInvite ?? string.Empty;
        TtlLabel.Text = _loc.Get("ModPacks.Wizard.TtlQuestion");
        TtlCombo.SelectedIndex = Engine.TtlDays switch { 7 => 0, 90 => 2, _ => 1 };
    }

    private void RenderSummary()
    {
        SummaryCompositionPanel.Children.Clear();
        SummaryCompositionPanel.Children.Add(Header(_loc.Get("ModPacks.Wizard.Summary.Composition")));
        SummaryCompositionPanel.Children.Add(Line($"{_loc.Get("ModPacks.Wizard.Summary.MainMod")}: {Engine.SummaryMainLine}", true));
        var addonNames = Engine.SummaryAddonLines;
        SummaryCompositionPanel.Children.Add(Line(
            _loc.GetFormatted("ModPacks.Wizard.Summary.Addons", addonNames.Count) +
            (addonNames.Count > 0 ? " — " + string.Join(", ", addonNames) : string.Empty), false));
        foreach (var outOf in Engine.SummaryOutOfCatalogLines)
        {
            SummaryCompositionPanel.Children.Add(Line($"{_loc.Get("ModPacks.Wizard.OutOfCatalog")}: {outOf}", false));
        }

        SummaryScopePanel.Children.Clear();
        SummaryScopePanel.Children.Add(Header(_loc.Get("ModPacks.Wizard.Summary.Scope")));
        SummaryScopePanel.Children.Add(Line(_loc.GetFormatted("ModPacks.Wizard.Summary.Platform", Engine.Platform), false));
        SummaryScopePanel.Children.Add(Line(
            _loc.Get(Engine.CreatesLocalInstance
                ? "ModPacks.Wizard.Summary.LocalInstallYes"
                : "ModPacks.Wizard.Summary.LocalInstallNo"), false));
        if (Engine.SharesOnline && Engine.IsTouConfigAvailable)
        {
            SummaryScopePanel.Children.Add(Line(
                _loc.Get(Engine.IncludeTouConfig
                    ? "ModPacks.Wizard.Summary.TouAttached"
                    : "ModPacks.Wizard.Summary.TouDetached"), false));
        }
        if (Engine.SharesOnline)
        {
            SummaryScopePanel.Children.Add(Line(_loc.Get("ModPacks.Wizard.Summary.UploadNotice"), false));
            SummaryScopePanel.Children.Add(Line(_loc.Get("ModPacks.CustomDlls.ServerScanNotice"), false));
        }

        SummarySharePanel.IsVisible = Engine.SharesOnline;
        SummarySharePanel.Children.Clear();
        if (Engine.SharesOnline)
        {
            SummarySharePanel.Children.Add(Header(_loc.Get("ModPacks.Wizard.Summary.Share")));
            SummarySharePanel.Children.Add(Line($"{_loc.Get("ModPacks.Wizard.TtlQuestion")}: {FormatTtl(Engine.TtlDays)}", false));
            if (!string.IsNullOrWhiteSpace(Engine.CreatorName))
                SummarySharePanel.Children.Add(Line($"{_loc.Get("ModPacks.CreatorNameLabel")}: {Engine.CreatorName}", false));
            if (!string.IsNullOrWhiteSpace(Engine.DiscordInvite))
                SummarySharePanel.Children.Add(Line($"{_loc.Get("ModPacks.DiscordLabel")}: {Engine.DiscordInvite}", false));
            SummarySharePanel.Children.Add(Line(_loc.Get("ModPacks.Wizard.Summary.ShareNote"), false));
        }

        static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };

        static TextBlock Line(string text, bool bold) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            FontSize = 13
        };
    }

    private string FormatTtl(int days) => days switch
    {
        7 => _loc.Get("ModPacks.Ttl7Days"),
        90 => _loc.Get("ModPacks.Ttl90Days"),
        _ => _loc.Get("ModPacks.Ttl30Days")
    };

    private void RenderExecution()
    {
        ExecutionStagesPanel.Children.Clear();
        foreach (var stage in Engine.Stages)
        {
            var border = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(10, 6, 10, 6)
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var glyph = new TextBlock
            {
                Text = stage.State switch
                {
                    ModPackWizardStageState.Pending => "○",
                    ModPackWizardStageState.Running => "●",
                    ModPackWizardStageState.Done => "✓",
                    ModPackWizardStageState.Failed => "✗",
                    ModPackWizardStageState.Stopped => "■",
                    _ => "–"
                },
                Margin = new Avalonia.Thickness(0, 0, 8, 0),
                FontWeight = FontWeight.Bold
            };
            Grid.SetColumn(glyph, 0);
            grid.Children.Add(glyph);

            var titleStack = new StackPanel { Spacing = 2 };
            titleStack.Children.Add(new TextBlock { Text = _loc.Get(stage.TitleKey) });
            if (!string.IsNullOrWhiteSpace(stage.Detail))
            {
                titleStack.Children.Add(new TextBlock
                {
                    Text = stage.Detail,
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            Grid.SetColumn(titleStack, 1);
            grid.Children.Add(titleStack);

            var state = new TextBlock
            {
                Text = _loc.Get($"ModPacks.Wizard.State.{stage.State}"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = stage.State switch
                {
                    ModPackWizardStageState.Failed => Brushes.IndianRed,
                    ModPackWizardStageState.Done => Brushes.SeaGreen,
                    _ => Brushes.Gray
                }
            };
            Grid.SetColumn(state, 2);
            grid.Children.Add(state);

            border.Child = grid;
            ExecutionStagesPanel.Children.Add(border);
        }

        var settled = Engine.ExecutionStarted && !Engine.IsExecuting;
        var allDone = Engine.Stages.Count > 0 &&
                      Engine.Stages.All(s => s.State is ModPackWizardStageState.Done or ModPackWizardStageState.Skipped);

        StopButton.IsVisible = Engine.IsExecuting && Engine.RunningStage is { Cancellable: true };
        StopButton.Content = _loc.Get("ModPacks.Wizard.Stop");

        RetryButton.IsVisible = settled && !allDone && Engine.Stages.Count > 0;
        RetryButton.Content = _loc.Get(Engine.PackAlreadyCreatedOnServer
            ? "ModPacks.Wizard.Resume"
            : "ModPacks.Wizard.Retry");

        ExecutionResultText.IsVisible = settled;
        if (settled)
        {
            if (allDone)
            {
                ExecutionResultText.Text = _loc.Get(Engine.SharesOnline
                    ? "ModPacks.Wizard.Result.ShareReady"
                    : "ModPacks.Wizard.Result.LocalReady");
            }
            else
            {
                var parts = new List<string>
                {
                    _loc.Get(Engine.IsExecuting ? "ModPacks.Wizard.Result.Running" : "ModPacks.Wizard.Result.Partial")
                };
                if (Engine.InstanceCreated)
                    parts.Add(_loc.Get("ModPacks.Wizard.Result.InstanceCreated"));
                if (Engine.FailedDllNames.Count > 0)
                    parts.Add(_loc.GetFormatted("ModPacks.Wizard.Result.AddonsFailed",
                        string.Join(", ", Engine.FailedDllNames)));
                if (Engine.FailedCustomFileNames.Count > 0)
                    parts.Add(_loc.GetFormatted("ModPacks.Wizard.Result.CustomFilesFailed",
                        string.Join(", ", Engine.FailedCustomFileNames)));
                if (Engine.PackAlreadyCreatedOnServer)
                    parts.Add(_loc.Get("ModPacks.FinalizeRetryHint"));
                if (Engine.RequiresDuplicateProbeBeforeRetry)
                    parts.Add(_loc.Get("ModPacks.Wizard.CreateUncertain"));
                ExecutionResultText.Text = string.Join("\n", parts);
            }
        }
    }

    private void RenderFooter()
    {
        CancelButton.Content = _loc.Get(Engine.ExecutionStarted
            ? "UI.Buttons.Close"
            : "UI.Buttons.Cancel");

        BackButton.IsVisible = Engine.StepIndex > 0 && !Engine.ExecutionStarted;
        BackButton.Content = _loc.Get("ModPacks.Wizard.Back");

        var installerMissing = Engine.CreatesLocalInstance && _instanceInstaller == null;
        var noShareSources = Engine.EntryMode == ModPackWizardEntryMode.ShareExisting &&
                             Engine.ShareSources.Count == 0;

        PrimaryButton.IsEnabled = !_isExecuting && !installerMissing && !noShareSources;
        PrimaryButton.Content = Engine.PrimaryAction switch
        {
            ModPackWizardPrimaryAction.Next => _loc.Get("ModPacks.Wizard.Next"),
            ModPackWizardPrimaryAction.InstallLocal => _loc.Get("ModPacks.Wizard.Action.InstallPack"),
            ModPackWizardPrimaryAction.InstallAndShare => _loc.Get("ModPacks.Wizard.Action.InstallAndLink"),
            ModPackWizardPrimaryAction.ShareExisting => _loc.Get("ModPacks.Wizard.Action.CreateLink"),
            ModPackWizardPrimaryAction.Finish => _loc.Get("ModPacks.Wizard.Finish"),
            _ => _loc.Get("ModPacks.Wizard.Next")
        };

        if (Engine.CurrentStep == ModPackWizardStep.Execution && Engine.ExecutionStarted && !Engine.IsExecuting)
        {
            var allDone = Engine.Stages.Count > 0 &&
                          Engine.Stages.All(s => s.State is ModPackWizardStageState.Done or ModPackWizardStageState.Skipped);
            PrimaryButton.Content = allDone
                ? _loc.Get("ModPacks.Wizard.Finish")
                : _loc.Get("UI.Buttons.Close");
            PrimaryButton.IsEnabled = true;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Nawigacja
    // ══════════════════════════════════════════════════════════════════

    private string LocalizeMessage(ModPackWizardMessage message) =>
        message.Args.Length == 0
            ? _loc.Get(message.Key)
            : _loc.GetFormatted(message.Key, message.Args.Select(a => (object?)a ?? string.Empty).ToArray());

    private async void PrimaryButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isExecuting)
            return;

        ErrorText.IsVisible = false;

        if (Engine.CurrentStep == ModPackWizardStep.MainMod && _customFullSubOpen)
        {
            ShowError(_loc.Get("ModPacks.Wizard.Main.ConfirmRequired"));
            return;
        }

        switch (Engine.CurrentStep)
        {
            case ModPackWizardStep.Goal:
            case ModPackWizardStep.MainMod:
            case ModPackWizardStep.Addons:
            case ModPackWizardStep.NameSettings:
            {
                if (!Engine.TryNext(out var error))
                {
                    if (error != null)
                        ShowError(LocalizeMessage(error));
                }

                if (Engine.CurrentStep == ModPackWizardStep.MainMod &&
                    Engine.EntryMode != ModPackWizardEntryMode.ShareExisting &&
                    Engine.VersionOptions.Count == 0 && !Engine.UsingCustomFull)
                {
                    await RefreshVersionsForSelectedMainAsync();
                    RenderAll();
                }

                break;
            }

            case ModPackWizardStep.Summary:
            {
                var preflight = Engine.ValidateAllContentSteps();
                if (preflight != null)
                {
                    ShowError(LocalizeMessage(preflight));
                    break;
                }

                Engine.TryNext(out _); // Summary → Execution
                await RunExecutionAsync();
                break;
            }

            case ModPackWizardStep.Execution:
            {
                if (Engine.IsExecuting)
                    break;
                Complete(BuildDialogResult());
                break;
            }
        }
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        Engine.GoBack();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Zamknięcie z potwierdzeniem (bez fikcyjnego rollbacku)
    // ══════════════════════════════════════════════════════════════════

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryBeginHostClose())
            return;
        Complete(Engine.ExecutionStarted ? BuildDialogResult() : null);
    }

    /// <summary>
    /// Punkt wejścia dla zewnętrznego przycisku „X” hosta. True = można zamknąć od razu;
    /// false = kreator pokazał potwierdzenie i sam wywoła Completed po decyzji użytkownika.
    /// </summary>
    public bool TryBeginHostClose()
    {
        if (_engine == null)
            return true;
        if (Engine.RequiresCloseConfirmation)
        {
            ShowCloseConfirmation(Engine.IsExecuting);
            return false;
        }

        // Wykonanie już wystartowało — zwracamy realny stan (nawet częściowy) zamiast cichego null.
        if (Engine.ExecutionStarted)
        {
            Complete(BuildDialogResult());
            return false;
        }

        return true;
    }

    private void ShowCloseConfirmation(bool duringRun)
    {
        CloseConfirmTitle.Text = _loc.Get("ModPacks.Wizard.CloseConfirm.Title");
        CloseConfirmBody.Text = _loc.Get(duringRun
            ? "ModPacks.Wizard.CloseConfirm.RunningBody"
            : "ModPacks.Wizard.CloseConfirm.Body");
        CloseConfirmStayButton.Content = _loc.Get("ModPacks.Wizard.CloseConfirm.Stay");
        CloseConfirmDiscardButton.Content = _loc.Get(duringRun
            ? "ModPacks.Wizard.CloseConfirm.StopAndClose"
            : "ModPacks.Wizard.CloseConfirm.Discard");
        CloseConfirmOverlay.IsVisible = true;
    }

    private void CloseConfirmStayButton_Click(object? sender, RoutedEventArgs e) =>
        CloseConfirmOverlay.IsVisible = false;

    private void CloseConfirmDiscardButton_Click(object? sender, RoutedEventArgs e)
    {
        CloseConfirmOverlay.IsVisible = false;

        if (Engine.IsExecuting)
        {
            // Realne zatrzymanie = anulowanie tokena tam, gdzie operacja go obsługuje.
            // Nie udajemy rollbacku: zwracamy faktycznie utworzone elementy.
            _runCts?.Cancel();
            _closeAfterExecution = true;
            ShowError(_loc.Get("ModPacks.Wizard.Stopping"));
            return;
        }

        Complete(Engine.ExecutionStarted ? BuildDialogResult() : null);
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Engine.RunningStage is { Cancellable: true })
            _runCts?.Cancel();
    }

    private async void RetryButton_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        await RunExecutionAsync();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Podkrok: własny mod główny z GitHuba (prowadzony, w tym modalu)
    // ══════════════════════════════════════════════════════════════════

    private async void AddCustomFullButton_Click(object? sender, RoutedEventArgs e)
    {
        _customFullSubOpen = true;
        _customFullUrlPhase = true;
        CustomFullUrlBox.Text = string.Empty;
        CustomFullNameBox.Text = string.Empty;
        CustomFullVersionBox.Text = string.Empty;
        CustomFullAmongVersionCombo.SelectedIndex = -1;
        RenderAll();
        await EnsureAmongVersionsLoadedAsync();
        RenderAll();
    }

    private async void CustomFullEditButton_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = Engine.ConfirmedCustomFull;
        _customFullSubOpen = true;
        _customFullUrlPhase = false;
        if (confirmed != null)
        {
            CustomFullUrlBox.Text = confirmed.GithubUrl;
            CustomFullNameBox.Text = confirmed.Name;
            CustomFullVersionBox.Text = confirmed.Version;
        }

        await EnsureAmongVersionsLoadedAsync();
        if (confirmed != null && _amongVersionValues.Contains(confirmed.AmongVersion))
            CustomFullAmongVersionCombo.SelectedIndex = _amongVersionValues.IndexOf(confirmed.AmongVersion);
        RenderAll();
    }

    private void CustomFullRemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        _customFullSubOpen = false;
        Engine.RemoveCustomFull();
        RenderAll();
    }

    private void CustomFullBackButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_customFullUrlPhase)
        {
            _customFullSubOpen = false;
        }
        else
        {
            _customFullUrlPhase = true;
        }

        RenderAll();
    }

    private async void CustomFullConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_customFullUrlPhase)
        {
            var url = CustomFullUrlBox.Text?.Trim();
            if (!ModPackWizardSourceGuard.IsAllowedGithubReleaseAssetUrl(url))
            {
                CustomFullSubError.Text = string.IsNullOrWhiteSpace(url)
                    ? _loc.Get("ModPacks.CustomFull.UrlRequired")
                    : _loc.Get("ModPacks.CustomFull.ReleaseAssetRequired");
                CustomFullSubError.IsVisible = true;
                return;
            }

            CustomFullSubError.IsVisible = false;
            _customFullUrlPhase = false;
            if (string.IsNullOrWhiteSpace(CustomFullNameBox.Text))
            {
                // Uzupełniamy tylko wiarygodnie ustalone dane (nazwa pliku assetu + repo).
                CustomFullNameBox.Text = ModPackWizardSourceGuard.SuggestArtifactName(url);
            }

            await EnsureAmongVersionsLoadedAsync();
            RenderAll();
            return;
        }

        var among = GetSelectedAmongVersion();
        var draft = new ModPackWizardCustomFullDraft
        {
            Name = CustomFullNameBox.Text?.Trim() ?? string.Empty,
            Version = CustomFullVersionBox.Text?.Trim() ?? string.Empty,
            AmongVersion = among ?? string.Empty,
            GithubUrl = CustomFullUrlBox.Text?.Trim() ?? string.Empty
        };

        if (!Engine.TryConfirmCustomFull(draft, out var error))
        {
            if (error != null)
            {
                CustomFullSubError.Text = LocalizeMessage(error);
                CustomFullSubError.IsVisible = true;
            }

            return;
        }

        _customFullSubOpen = false;
        Engine.ApplySuggestedPackName(draft.Name);
        RenderAll();
    }

    private string? GetSelectedAmongVersion()
    {
        var idx = CustomFullAmongVersionCombo.SelectedIndex;
        if (idx < 0 || idx >= _amongVersionValues.Count)
            return null;

        var value = AmongUsVersionHelper.NormalizeAmongVersion(_amongVersionValues[idx]);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private void UpdateCustomFullSubPhase()
    {
        if (_customFullSubOpen && _customFullUrlPhase)
            RenderCustomFullSubPanel();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Dodatki spoza katalogu: plik DLL z dysku + GitHub DLL (podkrok)
    // ══════════════════════════════════════════════════════════════════

    private async void AddCustomDllFiles_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
        {
            ShowError(_loc.Get("ModPacks.CustomDlls.FilePickerUnavailable"));
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _loc.Get("ModPacks.CustomDlls.AddFiles"),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(_loc.Get("ModPacks.CustomDlls.FileTypeDll"))
                {
                    Patterns = new[] { "*.dll" },
                    MimeTypes = new[] { "application/octet-stream" }
                },
                FilePickerFileTypes.All
            }
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            if (!string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            var info = new FileInfo(path);
            Engine.AddCustomDllFile(new ModPackWizardCustomDllRow
            {
                FilePath = path,
                FileName = info.Name,
                FileSize = info.Length,
                Sha256 = ModPackService.ComputeFileSha256(path),
                StatusText = _loc.Get("ModPacks.CustomDlls.ScanPending")
            });
        }
    }

    private void RefreshCustomDllFilesPanel()
    {
        CustomDllFilesPanel.Children.Clear();
        foreach (var entry in Engine.CustomDllRows.ToList())
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Avalonia.Thickness(0, 2, 0, 2)
            };

            var statusSuffix = string.IsNullOrWhiteSpace(entry.StatusText)
                ? string.Empty
                : $" — {entry.StatusText}";
            var text = new TextBlock
            {
                Text = $"{_loc.Get("ModPacks.Wizard.OutOfCatalog")}: {entry.FileName} — {ModPackWizardEngine.FormatSize(entry.FileSize)} — {entry.Sha256Preview}{statusSuffix}",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            };
            Grid.SetColumn(text, 0);
            row.Children.Add(text);

            var remove = new Button
            {
                Content = _loc.Get("ModPacks.CustomDlls.RemoveFile"),
                Padding = new Avalonia.Thickness(8, 4),
                IsEnabled = !_isExecuting,
                Tag = entry
            };
            remove.Click += (_, _) =>
            {
                if (remove.Tag is ModPackWizardCustomDllRow selected)
                    Engine.RemoveCustomDllFile(selected);
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);

            CustomDllFilesPanel.Children.Add(row);
        }
    }

    private void AddGithubDll_Click(object? sender, RoutedEventArgs e)
    {
        _githubSubEditingIndex = -1;
        _ = OpenGithubSubPanelAsync(githubUrl: string.Empty, displayName: string.Empty, version: string.Empty);
    }

    private async Task OpenGithubSubPanelAsync(string githubUrl, string displayName, string version)
    {
        GithubSubPanel.IsVisible = true;
        GithubUrlBox.Text = githubUrl;
        GithubNameBox.Text = displayName;
        GithubVersionBox.Text = version;
        GithubSubError.IsVisible = false;
        SetGithubSubPhase(urlPhase: true);
        await Task.CompletedTask;
    }

    private void SetGithubSubPhase(bool urlPhase)
    {
        GithubFieldsPanel.IsVisible = !urlPhase;
        GithubSubTitle.Text = _loc.Get("ModPacks.CustomGitHub.Title");
        if (urlPhase)
        {
            GithubSubBackButton.Content = _loc.Get("UI.Buttons.Cancel");
            GithubSubConfirmButton.Content = _loc.Get("ModPacks.Wizard.Next");
            GithubSubConfirmButton.IsEnabled = ModPackWizardSourceGuard.IsAllowedGithubReleaseAssetUrl(GithubUrlBox.Text);
        }
        else
        {
            GithubSubBackButton.Content = _loc.Get("ModPacks.Wizard.Back");
            GithubSubConfirmButton.Content = _loc.Get("ModPacks.Wizard.Confirm");
            GithubSubConfirmButton.IsEnabled = true;
        }
    }

    private bool _githubUrlPhase = true;

    private void GithubSubBackButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_githubUrlPhase)
        {
            GithubSubPanel.IsVisible = false;
            _githubSubEditingIndex = -1;
        }
        else
        {
            _githubUrlPhase = true;
            SetGithubSubPhase(true);
        }
    }

    private void GithubSubConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        var url = GithubUrlBox.Text?.Trim();

        if (_githubUrlPhase)
        {
            if (!ModPackWizardSourceGuard.IsAllowedGithubReleaseAssetUrl(url))
            {
                GithubSubError.Text = string.IsNullOrWhiteSpace(url)
                    ? _loc.Get("ModPacks.Wizard.Github.UrlRequired")
                    : _loc.Get("ModPacks.CustomGitHub.ReleaseAssetRequired");
                GithubSubError.IsVisible = true;
                return;
            }

            GithubSubError.IsVisible = false;
            _githubUrlPhase = false;
            if (string.IsNullOrWhiteSpace(GithubNameBox.Text))
            {
                GithubNameBox.Text = ModPackWizardSourceGuard.SuggestArtifactName(url);
            }

            SetGithubSubPhase(false);
            return;
        }

        var name = GithubNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            GithubSubError.Text = _loc.Get("ModPacks.CustomGitHub.NameRequired");
            GithubSubError.IsVisible = true;
            return;
        }

        if (_githubSubEditingIndex >= 0 && _githubSubEditingIndex < Engine.GithubDllRows.Count)
        {
            var existing = Engine.GithubDllRows[_githubSubEditingIndex];
            existing.GithubUrl = url ?? string.Empty;
            existing.DisplayName = name ?? string.Empty;
            existing.Version = GithubVersionBox.Text?.Trim() ?? string.Empty;
        }
        else
        {
            Engine.AddGithubDll(new ModPackWizardGithubDllRow
            {
                DisplayName = name ?? string.Empty,
                Version = GithubVersionBox.Text?.Trim() ?? string.Empty,
                GithubUrl = url ?? string.Empty
            });
        }

        _githubSubEditingIndex = -1;
        _githubUrlPhase = true;
        GithubSubPanel.IsVisible = false;
        RefreshGithubDllEntriesPanel();
    }

    private void RefreshGithubDllEntriesPanel()
    {
        GithubDllEntriesPanel.Children.Clear();
        for (var i = 0; i < Engine.GithubDllRows.Count; i++)
        {
            var entry = Engine.GithubDllRows[i];
            var idx = i;

            var card = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(8),
                Margin = new Avalonia.Thickness(0, 2, 0, 2)
            };

            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = $"{_loc.Get("ModPacks.Wizard.OutOfCatalog")}: {entry.DisplayName}" +
                       (string.IsNullOrWhiteSpace(entry.Version) ? string.Empty : $" v{entry.Version}"),
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock { Text = entry.GithubUrl, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(entry.StatusText))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = entry.StatusText,
                    FontSize = 11,
                    Foreground = Brushes.Gray
                });
            }

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var edit = new Button
            {
                Content = _loc.Get("ModPacks.Wizard.Change"),
                Padding = new Avalonia.Thickness(6, 2),
                FontSize = 11,
                IsEnabled = !_isExecuting,
                Tag = idx
            };
            edit.Click += async (_, _) =>
            {
                _githubSubEditingIndex = idx;
                await OpenGithubSubPanelAsync(entry.GithubUrl, entry.DisplayName, entry.Version);
            };
            btnRow.Children.Add(edit);

            var remove = new Button
            {
                Content = _loc.Get("ModPacks.CustomDlls.RemoveFile"),
                Padding = new Avalonia.Thickness(6, 2),
                FontSize = 11,
                IsEnabled = !_isExecuting,
                Tag = entry
            };
            remove.Click += (_, _) =>
            {
                if (remove.Tag is ModPackWizardGithubDllRow sel)
                    Engine.RemoveGithubDll(sel);
            };
            btnRow.Children.Add(remove);
            stack.Children.Add(btnRow);

            card.Child = stack;
            GithubDllEntriesPanel.Children.Add(card);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Wykonanie: etapy = rzeczywisty przepływ usług
    // ══════════════════════════════════════════════════════════════════

    private async Task RunExecutionAsync()
    {
        if (_isExecuting)
            return;

        _isExecuting = true;
        Engine.StartExecution();
        RenderAll();

        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        var failed = false;
        try
        {
            if (Engine.SharesOnline)
                SaveShareProfileFromForm();

            foreach (var stage in Engine.Stages.ToList())
            {
                if (stage.State == ModPackWizardStageState.Done)
                    continue;

                failed = stage.Id switch
                {
                    ModPackWizardStageId.InstallMain => !await RunInstallMainAsync(ct),
                    ModPackWizardStageId.InstallAddons => !await RunInstallAddonsAsync(ct),
                    ModPackWizardStageId.CopyCustomFiles => !await RunCopyCustomFilesAsync(ct),
                    ModPackWizardStageId.CreateSharePack => !await RunCreateShareAsync(ct),
                    ModPackWizardStageId.SendCustomContent => !await RunSendCustomContentAsync(ct),
                    ModPackWizardStageId.FinalValidation => !await RunFinalValidationAsync(ct),
                    _ => false
                };

                if (failed)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            if (Engine.RunningStage != null)
                Engine.SetStageStopped(Engine.RunningStage.Id);
            failed = true;
            ShowError(_loc.Get("ModPacks.Wizard.Stopped"));
        }
        catch (Exception ex)
        {
            if (Engine.RunningStage != null)
                Engine.SetStageFailed(Engine.RunningStage.Id, _loc.Get("ModPacks.CreateFailed"));
            failed = true;
            System.Diagnostics.Debug.WriteLine($"[ModPackWizard] execution failed: {ex.GetType().Name}");
            ShowError(_loc.Get("ModPacks.CreateFailed"));
        }
        finally
        {
            Engine.MarkExecutionSettled(finished: !failed);
            _isExecuting = false;
            RenderAll();
            if (_closeAfterExecution)
                Complete(BuildDialogResult());
        }
    }

    private ModConfiguration? ResolveModToInstall()
    {
        if (Engine.UsingCustomFull)
        {
            var confirmed = Engine.ConfirmedCustomFull;
            if (confirmed == null)
            {
                ShowError(_loc.Get("ModPacks.Wizard.Main.ConfirmRequired"));
                return null;
            }

            if (!ModPackWizardSourceGuard.IsAllowedGithubReleaseAssetUrl(confirmed.GithubUrl))
            {
                ShowError(_loc.Get("ModPacks.CustomFull.ReleaseAssetRequired"));
                return null;
            }

            return new ModConfiguration
            {
                Id = 0,
                ModName = confirmed.Name,
                ModType = "full",
                ModVersion = string.IsNullOrWhiteSpace(confirmed.Version) ? "custom" : confirmed.Version,
                AmongVersion = confirmed.AmongVersion,
                GitHubRepoOrLink = confirmed.GithubUrl,
                EpicGitHubRepoOrLink = confirmed.GithubUrl,
                DllInstallPath = "BepInEx/plugins"
            };
        }

        var catalogMod = Engine.SelectedCatalogMod;
        if (catalogMod == null)
        {
            ShowError(_loc.Get("ModPacks.SelectFullMod"));
            return null;
        }

        return CloneModWithVersion(catalogMod, Engine.SelectedVersionValue);
    }

    private async Task<bool> RunInstallMainAsync(CancellationToken ct)
    {
        if (Engine.InstanceCreated)
        {
            Engine.SetStageDone(ModPackWizardStageId.InstallMain);
            _runInstance ??= FindRunInstance();
            return true;
        }

        var modToInstall = ResolveModToInstall();
        if (modToInstall == null)
        {
            Engine.SetStageFailed(ModPackWizardStageId.InstallMain);
            return false;
        }

        Engine.SetStageRunning(ModPackWizardStageId.InstallMain, cancellable: false);
        try
        {
            var instance = await _instanceInstaller!.InstallFullModInstanceAsync(
                modToInstall,
                Engine.PackName,
                _platform,
                new SimpleProgressReporter(p => UpdateStageDetail(ModPackWizardStageId.InstallMain, $"{p}%")),
                new SimpleDiagnosticsOutput(),
                new ModManagerUserCallbacks(),
                origin: "manual");

            Engine.CreatedInstanceId = instance.InstanceId;
            _runInstance = instance;
            ct.ThrowIfCancellationRequested();
            Engine.SetStageDone(ModPackWizardStageId.InstallMain, instance.DisplayName);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModPackWizard] installation failed: {ex.GetType().Name}");
            Engine.SetStageFailed(ModPackWizardStageId.InstallMain, _loc.Get("ModPacks.InstallFailed"));
            ShowError(_loc.Get("ModPacks.InstallFailed"));
            return false;
        }
    }

    private ModInstance? FindRunInstance() =>
        Engine.CreatedInstanceId == null ? null : _instances.GetInstance(Engine.CreatedInstanceId);

    private async Task<bool> RunInstallAddonsAsync(CancellationToken ct)
    {
        var instanceId = Engine.CreatedInstanceId ?? _runInstance?.InstanceId;
        var rows = Engine.AddonRows.Where(r => r.IsChecked).ToList();
        if (rows.Count == 0 || instanceId == null)
        {
            Engine.SetStageDone(ModPackWizardStageId.InstallAddons, _loc.Get("ModPacks.Wizard.Stage.SkippedNothing"));
            Engine.AddonsStageProcessed = true;
            return true;
        }

        Engine.SetStageRunning(ModPackWizardStageId.InstallAddons, cancellable: false);
        var pending = rows
            .Where(r => !Engine.InstalledDllIds.Contains(r.Mod.Id))
            .ToList();
        var total = rows.Count;
        var processed = rows.Count - pending.Count;
        for (var i = 0; i < pending.Count; i++)
        {
            var dll = pending[i].Mod;
            ct.ThrowIfCancellationRequested();
            UpdateStageDetail(ModPackWizardStageId.InstallAddons, $"{processed}/{total}");
            try
            {
                await _instanceInstaller!.InstallDllToInstanceAsync(dll, instanceId, _platform);
                if (!Engine.InstalledDllIds.Contains(dll.Id))
                    Engine.InstalledDllIds.Add(dll.Id);
            }
            catch (Exception ex)
            {
                Engine.FailedDllNames.Add(dll.ModName ?? $"DLL#{dll.Id}");
                System.Diagnostics.Debug.WriteLine($"[ModPackWizard] DLL install failed: {dll.ModName} — {ex.Message}");
            }

            processed++;
        }

        Engine.AddonsStageProcessed = Engine.FailedDllNames.Count == 0;
        var detail = Engine.FailedDllNames.Count == 0
            ? $"{processed}/{total}"
            : $"{Engine.InstalledDllIds.Count}/{total} — ✗ {string.Join(", ", Engine.FailedDllNames)}";
        Engine.SetStageDone(ModPackWizardStageId.InstallAddons, detail);
        return true;
    }

    private async Task<bool> RunCopyCustomFilesAsync(CancellationToken ct)
    {
        _runInstance ??= FindRunInstance();
        var instance = _runInstance;
        if (instance == null)
        {
            Engine.SetStageFailed(ModPackWizardStageId.CopyCustomFiles);
            ShowError(_loc.Get("UI.Packs.InstallerUnavailable"));
            return false;
        }

        Engine.SetStageRunning(ModPackWizardStageId.CopyCustomFiles, cancellable: false);
        foreach (var entry in Engine.CustomDllRows.Where(r => !r.CopiedToInstance).ToList())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await Task.Run(() => CopyCustomDllToInstance(instance, entry), ct);
                entry.CopiedToInstance = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Engine.FailedCustomFileNames.Add(entry.FileName);
                System.Diagnostics.Debug.WriteLine($"[ModPackWizard] Custom DLL copy failed: {entry.FileName} — {ex.Message}");
            }

            RefreshCustomDllFilesPanel();
        }


        Engine.CustomFilesCopiedStageDone = Engine.CustomDllRows.All(r => r.CopiedToInstance);
        var detail = Engine.FailedCustomFileNames.Count == 0
            ? null
            : $"✗ {string.Join(", ", Engine.FailedCustomFileNames)}";
        Engine.SetStageDone(ModPackWizardStageId.CopyCustomFiles, detail);
        return true;
    }

    private async Task<bool> RunCreateShareAsync(CancellationToken ct)
    {
        if (Engine.PackAlreadyCreatedOnServer && !Engine.RequiresDuplicateProbeBeforeRetry)
        {
            // Wznowienie istniejącej paczki — NIE tworzymy drugiej.
            Engine.SetStageDone(ModPackWizardStageId.CreateSharePack, Engine.PendingSharePackCode);
            return true;
        }

        var request = BuildShareRequest();
        if (request == null)
        {
            Engine.SetStageFailed(ModPackWizardStageId.CreateSharePack);
            return false;
        }

        Engine.SetStageRunning(ModPackWizardStageId.CreateSharePack, cancellable: true);

        if (!Engine.CanSubmitNewShareRequest)
        {
            // Listing is not an idempotency guarantee: delayed visibility or same-name packs are ambiguous.
            Engine.SetStageFailed(ModPackWizardStageId.CreateSharePack, _loc.Get("ModPacks.Wizard.CreateUncertain"));
            ShowError(_loc.Get("ModPacks.Wizard.CreateUncertain"));
            return false;
        }

        // Also covers cancellation/exception after the server accepted a POST.
        Engine.RequiresDuplicateProbeBeforeRetry = true;
        var result = await _modPackService.CreatePackAsync(request, ct);
        if (result.Success && !string.IsNullOrWhiteSpace(result.PackCode))
        {
            AdoptCreateResult(result, request);
            Engine.SetStageDone(ModPackWizardStageId.CreateSharePack, result.PackCode);
            return true;
        }

        if (string.Equals(result.ErrorCode, "PACK_LIMIT_REACHED", StringComparison.OrdinalIgnoreCase))
        {
            Engine.RequiresDuplicateProbeBeforeRetry = false;
            Engine.SetStageFailed(ModPackWizardStageId.CreateSharePack, _loc.Get("ModPacks.PackLimitReached"));
            ShowError(_loc.Get("ModPacks.PackLimitReached"));
            _isExecuting = false;
            Engine.MarkExecutionSettled(finished: false);
            RenderAll();
            return false;
        }

        // Ponowienie bez duplikatu: niepewna porażka → najpierw sprawdź, czy serwer już utworzył paczkę.
        if (ModPackWizardCreateGuard.RequiresProbeBeforeRetry(result.ErrorCode))
        {
            Engine.RequiresDuplicateProbeBeforeRetry = true;
            Engine.SetStageFailed(ModPackWizardStageId.CreateSharePack, _loc.Get("ModPacks.Wizard.CreateUncertain"));
            ShowError(_loc.Get("ModPacks.Wizard.CreateUncertain"));
            return false;
        }

        Engine.SetStageFailed(ModPackWizardStageId.CreateSharePack);
        Engine.RequiresDuplicateProbeBeforeRetry = false; // Only an explicit, definite rejection reaches this branch.
        ShowError(FormatCreatePackError(result));
        return false;
    }

    private void AdoptCreateResult(ModPackCreateResult result, ModPackCreateRequest request)
    {
        Engine.RequiresDuplicateProbeBeforeRetry = false;
        _shareResult = result;
        Engine.ResultPackCode = result.PackCode;
        Engine.ResultShareUrl = result.ShareUrl;

        if (HasCustomContentToSubmit())
        {
            // Paczka jest na serwerze, ale custom content/finalizacja jeszcze nie —
            // zatrzymanie w tym miejscu musi być wznawialne, nie powtarzalne.
            Engine.PendingSharePackCode = result.PackCode;
        }
        else
        {
            Engine.PendingSharePackCode = null;
        }
    }

    private async Task<bool> RunSendCustomContentAsync(CancellationToken ct)
    {
        var packCode = Engine.PendingSharePackCode ?? Engine.ResultPackCode;
        if (string.IsNullOrWhiteSpace(packCode))
        {
            // Bez kodu paczki nie mamy dokąd wysyłać custom content — to stan błędny, nie „sukces”.
            Engine.SetStageFailed(ModPackWizardStageId.SendCustomContent,
                _loc.Get("ModPacks.CreateFailed"));
            return false;
        }

        Engine.SetStageRunning(ModPackWizardStageId.SendCustomContent, cancellable: true);

        foreach (var entry in Engine.CustomDllRows)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.Uploaded)
            {
            entry.StatusText = _loc.Get("ModPacks.CustomDlls.Uploading");
            RefreshCustomDllFilesPanel();

            var uploaded = await _modPackService.UploadCustomDllAsync(packCode, entry.FilePath, ct);
            if (uploaded == null)
            {
                entry.StatusText = _loc.Get("ModPacks.CustomDlls.UploadFailed");
                RefreshCustomDllFilesPanel();
                Engine.SetStageFailed(ModPackWizardStageId.SendCustomContent, entry.FileName);
                ShowError(string.Format(_loc.Get("ModPacks.CustomDlls.UploadFailedForFile"), entry.FileName));
                return false;
            }

            entry.ArtifactId = uploaded.ArtifactId;
            entry.Sha256 = string.IsNullOrWhiteSpace(uploaded.Sha256) ? entry.Sha256 : uploaded.Sha256;
            entry.RawScanStatus = uploaded.Status;
            entry.StatusText = MapCustomArtifactStatus(uploaded.Status);
            entry.Uploaded = true;
            RefreshCustomDllFilesPanel();
            }

            if (!await WaitForCustomDllCleanAsync(packCode, entry, ct))
            {
                Engine.SetStageFailed(ModPackWizardStageId.SendCustomContent, entry.FileName);
                return false;
            }
        }

        if (!await DeclareAndWaitForGithubDllsAsync(packCode, ct))
        {
            Engine.SetStageFailed(ModPackWizardStageId.SendCustomContent);
            return false;
        }

        if (!await DeclareAndWaitForCustomFullAsync(packCode, ct))
        {
            Engine.SetStageFailed(ModPackWizardStageId.SendCustomContent);
            return false;
        }

        Engine.SetStageDone(ModPackWizardStageId.SendCustomContent);
        return true;
    }

    private async Task<bool> DeclareAndWaitForGithubDllsAsync(string packCode, CancellationToken ct)
    {
        if (!Engine.UseGithubDllSources || Engine.GithubDllRows.Count == 0)
            return true;

        for (var i = 0; i < Engine.GithubDllRows.Count; i++)
        {
            var entry = Engine.GithubDllRows[i];
            ct.ThrowIfCancellationRequested();

            var githubUrl = (entry.GithubUrl ?? string.Empty).Trim();
            var displayName = (entry.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(githubUrl))
            {
                ShowError(string.Format(_loc.Get("ModPacks.CustomGitHub.UrlRequiredFor"), i + 1));
                return false;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                ShowError(string.Format(_loc.Get("ModPacks.CustomGitHub.NameRequiredFor"), i + 1));
                return false;
            }

            if (!ModPackWizardSourceGuard.IsAllowedGithubReleaseAssetUrl(githubUrl))
            {
                ShowError(_loc.Get("ModPacks.CustomGitHub.ReleaseAssetRequired"));
                return false;
            }

            var dllInstallPath = string.IsNullOrWhiteSpace(entry.DllInstallPath)
                ? "BepInEx/plugins"
                : entry.DllInstallPath.Trim();

            if (!ModPackWizardSourceGuard.IsSafeDllInstallPath(dllInstallPath))
            {
                ShowError(_loc.Get("ModPacks.CustomGitHub.DllInstallPathInvalid"));
                return false;
            }

            if (!string.IsNullOrWhiteSpace(entry.ArtifactId))
            {
                // Artefakt zgłoszony w poprzedniej próbie — nie dublujemy declare; dokańczamy tylko skan.
                if (IsBlockingCustomArtifactStatus(entry.RawScanStatus))
                {
                    ShowError(string.Format(_loc.Get("ModPacks.CustomGitHub.Blocked"), displayName, entry.StatusText));
                    return false;
                }

                if (!string.Equals(entry.RawScanStatus, "clean", StringComparison.OrdinalIgnoreCase))
                {
                    var stillWaiting = await WaitForGithubDllEntryCleanAsync(packCode, entry.ArtifactId,
                        raw => ApplyGithubScanStatus(entry, raw), ct);
                    if (!stillWaiting)
                        return false;
                }

                continue;
            }

            entry.StatusText = _loc.Get("ModPacks.CustomGitHub.Declaring");
            RefreshGithubDllEntriesPanel();

            var artifact = await _modPackService.DeclareGitHubCustomModAsync(packCode, new ModPackCustomGithubModRequest
            {
                SourceKind = "github_dll",
                ModType = "dll",
                DisplayName = displayName,
                Version = string.IsNullOrWhiteSpace(entry.Version) ? null : entry.Version.Trim(),
                GithubUrl = githubUrl,
                DllInstallPath = dllInstallPath
            }, ct);

            if (artifact == null || string.IsNullOrWhiteSpace(artifact.ArtifactId))
            {
                entry.StatusText = _loc.Get("ModPacks.CustomGitHub.DeclareFailed");
                RefreshGithubDllEntriesPanel();
                ShowError(_loc.Get("ModPacks.CustomGitHub.DeclareFailed"));
                return false;
            }

            entry.ArtifactId = artifact.ArtifactId;
            entry.RawScanStatus = artifact.Status;
            entry.StatusText = MapCustomArtifactStatus(artifact.Status);
            RefreshGithubDllEntriesPanel();

            if (!await WaitForGithubDllEntryCleanAsync(packCode, entry.ArtifactId,
                    raw => ApplyGithubScanStatus(entry, raw), ct))
                return false;
        }

        return true;
    }

    /// <summary>Jedno miejsce mapowania statusu skanu artefaktu GitHub na stan wiersza + odświeżenie listy.</summary>
    private void ApplyGithubScanStatus(ModPackWizardGithubDllRow entry, string? rawStatus)
    {
        if (rawStatus == null)
        {
            RefreshGithubDllEntriesPanel();
            return;
        }

        entry.RawScanStatus = rawStatus;
        entry.StatusText = MapCustomArtifactStatus(rawStatus);
        RefreshGithubDllEntriesPanel();
    }

    private async Task<bool> DeclareAndWaitForCustomFullAsync(string packCode, CancellationToken ct)
    {
        var confirmed = Engine.ConfirmedCustomFull;
        if (confirmed == null || !Engine.SharesOnline || !Engine.CreatesLocalInstance)
            return true;

        if (!ModPackWizardSourceGuard.IsAllowedGithubReleaseAssetUrl(confirmed.GithubUrl))
        {
            ShowError(_loc.Get("ModPacks.CustomFull.ReleaseAssetRequired"));
            return false;
        }

        if (string.IsNullOrWhiteSpace(confirmed.AmongVersion))
        {
            ShowError(_loc.Get("ModPacks.CustomFull.AmongVersionRequired"));
            return false;
        }

        SetCustomFullCardStatus(_loc.Get("ModPacks.CustomFull.Declaring"));

        var artifact = await _modPackService.DeclareGitHubCustomModAsync(packCode, new ModPackCustomGithubModRequest
        {
            SourceKind = "github_full",
            ModType = "full",
            DisplayName = confirmed.Name,
            Version = string.IsNullOrWhiteSpace(confirmed.Version) ? null : confirmed.Version.Trim(),
            AmongVersion = confirmed.AmongVersion,
            GithubUrl = confirmed.GithubUrl
        }, ct);

        if (artifact == null || string.IsNullOrWhiteSpace(artifact.ArtifactId))
        {
            SetCustomFullCardStatus(_loc.Get("ModPacks.CustomFull.DeclareFailed"));
            ShowError(_loc.Get("ModPacks.CustomFull.DeclareFailed"));
            return false;
        }

        SetCustomFullCardStatus(MapCustomArtifactStatus(artifact.Status));
        return await WaitForGithubDllEntryCleanAsync(packCode, artifact.ArtifactId,
            s => SetCustomFullCardStatus(MapCustomArtifactStatus(s)), ct);
    }

    private void SetCustomFullCardStatus(string displayText)
    {
        // Status własnego moda głównego wyświetlamy na jego karcie (bez osobnego okna dialogowego).
        if (CustomFullCard.IsVisible)
            CustomFullCardBadge.Text = $"{_loc.Get("ModPacks.Wizard.OutOfCatalog")} · {displayText}";
    }

    private async Task<bool> WaitForCustomDllCleanAsync(string packCode, ModPackWizardCustomDllRow entry, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var status = await _modPackService.GetExternalDllStatusAsync(packCode, entry.Sha256, ct);
            if (!status.Success)
            {
                entry.StatusText = status.ErrorCode ?? _loc.Get("ModPacks.CustomDlls.UploadFailed");
                RefreshCustomDllFilesPanel();
                ShowError(status.ErrorMessage ?? _loc.Get("ModPacks.CustomDlls.UploadFailed"));
                return false;
            }

            entry.RawScanStatus = status.Status;
            entry.StatusText = MapCustomArtifactStatus(status.Status);
            RefreshCustomDllFilesPanel();

            if (string.Equals(status.Status, "clean", StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsBlockingCustomArtifactStatus(status.Status))
            {
                ShowError(string.Format(_loc.Get("ModPacks.CustomDlls.BlockedForFile"), entry.FileName, entry.StatusText));
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        ShowError(_loc.Get("ModPacks.CustomContent.InstallBlockedPendingScan"));
        return false;
    }

    private async Task<bool> WaitForGithubDllEntryCleanAsync(
        string packCode, string artifactId, Action<string?> setStatus, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var status = await _modPackService.GetCustomArtifactStatusAsync(packCode, artifactId, ct);
            if (!status.Success)
            {
                setStatus(null);
                ShowError(status.ErrorMessage ?? _loc.Get("ModPacks.CustomGitHub.StatusFailed"));
                return false;
            }

            setStatus(status.Status);

            if (string.Equals(status.Status, "clean", StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsBlockingCustomArtifactStatus(status.Status))
            {
                ShowError(_loc.Get("ModPacks.CustomContent.InstallBlockedSuspicious"));
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        ShowError(_loc.Get("ModPacks.CustomContent.InstallBlockedPendingScan"));
        return false;
    }

    private async Task<bool> RunFinalValidationAsync(CancellationToken ct)
    {
        var packCode = Engine.PendingSharePackCode ?? Engine.ResultPackCode;
        if (string.IsNullOrWhiteSpace(packCode))
        {
            Engine.SetStageDone(ModPackWizardStageId.FinalValidation);
            return true;
        }

        Engine.SetStageRunning(ModPackWizardStageId.FinalValidation, cancellable: true);
        var finalize = await _modPackService.FinalizePackAsync(packCode, ct);
        if (!finalize.Success || !finalize.Installable)
        {
            Engine.SetStageFailed(ModPackWizardStageId.FinalValidation);
            ShowError(finalize.ErrorMessage ?? _loc.Get("ModPacks.CustomContent.InstallBlockedPendingScan"));
            return false;
        }

        if (_shareResult != null)
        {
            _shareResult.Status = finalize.Status;
            _shareResult.Installable = finalize.Installable;
            if (!string.IsNullOrWhiteSpace(finalize.ShareUrl))
                _shareResult.ShareUrl = finalize.ShareUrl;
            if (!string.IsNullOrWhiteSpace(finalize.DeepLink))
                _shareResult.DeepLink = finalize.DeepLink;
        }

        Engine.PendingSharePackCode = null;
        Engine.ResultShareUrl = _shareResult?.ShareUrl;
        Engine.SetStageDone(ModPackWizardStageId.FinalValidation, MapCustomArtifactStatus(finalize.Status));
        return true;
    }

    private void UpdateStageDetail(ModPackWizardStageId id, string detail)
    {
        var stage = Engine.Stages.FirstOrDefault(s => s.Id == id);
        if (stage == null)
            return;
        stage.Detail = detail;
        if (_detached)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_detached)
                RenderExecution();
        });
    }

    // ══════════════════════════════════════════════════════════════════
    //  Żądania udostępnienia i pomocnicze (przeniesione 1:1 z dawnego formularza)
    // ══════════════════════════════════════════════════════════════════

    private ModPackCreateRequest? BuildShareRequest()
    {
        var name = Engine.PackName;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError(_loc.Get("UI.Packs.DisplayNameRequired"));
            return null;
        }

        if (Engine.EntryMode == ModPackWizardEntryMode.ShareExisting)
        {
            var source = Engine.SelectedShareSource;
            if (source == null)
            {
                ShowError(_loc.Get("UI.Packs.SelectShareSource"));
                return null;
            }

            if (!string.IsNullOrEmpty(source.InstanceId))
            {
                var request = _mapper.Map(
                    source.InstanceId,
                    name,
                    Engine.TtlDays,
                    Engine.CreatorName,
                    Engine.DiscordInvite);
                request.DllMods = BuildDllModRequestsFromEngine();
                if (!Engine.IncludeTouConfig)
                    request.TouConfig = null;
                return request;
            }

            return new ModPackCreateRequest
            {
                CreatorName = Engine.CreatorName,
                FullModId = source.TargetMod.Id,
                FullModVersion = GetSelectedFullModVersionFromTarget(source.TargetMod),
                ModName = name,
                DiscordInvite = Engine.DiscordInvite,
                TtlDays = Engine.TtlDays,
                DllMods = BuildDllModRequestsFromEngine()
            };
        }

        // CreateAndShare (lub InstallLocal z celem „dla znajomych”)
        var instanceId = Engine.CreatedInstanceId ?? _runInstance?.InstanceId;
        if (Engine.UsingCustomFull)
        {
            var confirmed = Engine.ConfirmedCustomFull!;
            return new ModPackCreateRequest
            {
                CreatorName = Engine.CreatorName,
                FullModId = 0,
                FullModVersion = string.IsNullOrWhiteSpace(confirmed.Version) ? "custom" : confirmed.Version.Trim(),
                ModName = name,
                DiscordInvite = Engine.DiscordInvite,
                TtlDays = Engine.TtlDays,
                DllMods = BuildInstalledDllRequests(),
                Metadata = BuildAmongVersionMetadata(confirmed.AmongVersion)
            };
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            ShowError(_loc.Get("ModPacks.Wizard.MissingInstanceForShare"));
            return null;
        }

        var shareRequest = _mapper.Map(
            instanceId,
            name,
            Engine.TtlDays,
            Engine.CreatorName,
            Engine.DiscordInvite);
        shareRequest.DllMods = BuildInstalledDllRequests();
        return shareRequest;
    }

    private List<ModPackDllModRequest> BuildDllModRequestsFromEngine() => Engine.AddonRows
        .Where(r => r.IsChecked)
        .Select(r => new ModPackDllModRequest
        {
            DllModId = r.Mod.Id,
            DllModVersion = r.Mod.ModVersion ?? "latest"
        })
        .ToList();

    private List<ModPackDllModRequest> BuildInstalledDllRequests() => Engine.AddonRows
        .Where(r => r.IsChecked && Engine.InstalledDllIds.Contains(r.Mod.Id))
        .Select(r => new ModPackDllModRequest
        {
            DllModId = r.Mod.Id,
            DllModVersion = r.Mod.ModVersion ?? "latest"
        })
        .ToList();

    private bool HasCustomContentToSubmit() =>
        Engine.CustomDllRows.Count > 0 ||
        (Engine.UseGithubDllSources && Engine.GithubDllRows.Count > 0) ||
        (Engine.UsingCustomFull && Engine.CreatesLocalInstance);

    private static ModConfiguration CloneModWithVersion(ModConfiguration source, string version)
    {
        var clone = new ModConfiguration
        {
            Id = source.Id,
            ModName = source.ModName,
            ModType = source.ModType,
            ModVersion = source.ModVersion,
            AmongVersion = source.AmongVersion,
            GitHubRepoOrLink = source.GitHubRepoOrLink,
            EpicGitHubRepoOrLink = source.EpicGitHubRepoOrLink,
            Description = source.Description,
            PngFileName = source.PngFileName,
            DllInstallPath = source.DllInstallPath
        };

        if (!string.IsNullOrWhiteSpace(version) &&
            !string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
        {
            clone.ModVersion = version;
        }

        return clone;
    }

    private string GetSelectedFullModVersionFromTarget(ModConfiguration target) =>
        string.IsNullOrWhiteSpace(target.ModVersion) ? "latest" : target.ModVersion;

    private static JsonElement BuildAmongVersionMetadata(string amongVersion) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["amongVersion"] = amongVersion
        });

    private void CopyCustomDllToInstance(ModInstance instance, ModPackWizardCustomDllRow entry)
    {
        var actualPath = PathSettings.GetActualModPath(instance.InstallPath);
        var pluginsDir = Path.Combine(actualPath, "BepInEx", "plugins");
        var dest = Path.Combine(pluginsDir, entry.FileName);

        Directory.CreateDirectory(pluginsDir);
        File.Copy(entry.FilePath, dest, overwrite: true);

        var now = DateTime.UtcNow.ToString("O");
        _instances.AddDll(new ModInstanceDll
        {
            InstanceId = instance.InstanceId,
            DllModId = null,
            DllName = entry.FileName,
            DllVersion = string.Empty,
            Source = "external",
            Sha256 = entry.Sha256,
            VtStatus = "unknown",
            InstalledPath = entry.FileName,
            CreatedAt = now
        });
    }

    private void SaveShareProfileFromForm()
    {
        try
        {
            var settings = _userSettingsService.LoadUserSettings();
            settings.ModPackShareCreatorName = Engine.CreatorName ?? string.Empty;
            settings.ModPackShareDiscordInvite = Engine.DiscordInvite ?? string.Empty;
            _userSettingsService.SaveUserSettings(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModPackWizard] Failed to save share profile: {ex.Message}");
        }
    }

    private string FormatCreatePackError(ModPackCreateResult result)
    {
        if (string.Equals(result.ErrorCode, "PACK_LIMIT_REACHED", StringComparison.OrdinalIgnoreCase))
            return _loc.Get("ModPacks.PackLimitReached");

        if (string.Equals(result.ErrorCode, "INVALID_CREATOR_HASH", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(result.ErrorMessage) &&
             result.ErrorMessage.Contains("creatorHash", StringComparison.OrdinalIgnoreCase) &&
             result.ErrorMessage.Contains("64 hex", StringComparison.OrdinalIgnoreCase)))
        {
            return _loc.Get("ModPacks.InvalidCreatorHash");
        }

        return result.ErrorMessage ?? _loc.Get("ModPacks.CreateFailed");
    }

    private string MapCustomArtifactStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "clean" => _loc.Get("ModPacks.CustomDlls.ScanClean"),
        "suspicious" => _loc.Get("ModPacks.CustomDlls.ScanSuspicious"),
        "rejected" => _loc.Get("ModPacks.CustomDlls.ScanRejected"),
        "expired" => _loc.Get("ModPacks.CustomDlls.ScanExpired"),
        "scanning" => _loc.Get("ModPacks.CustomDlls.Scanning"),
        "pending" => _loc.Get("ModPacks.CustomDlls.ScanPending"),
        _ => _loc.Get("ModPacks.CustomDlls.ScanPending")
    };

    private static bool IsBlockingCustomArtifactStatus(string? status) =>
        string.Equals(status, "suspicious", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "rejected", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase);

    // ══════════════════════════════════════════════════════════════════
    //  Wynik kreatora
    // ══════════════════════════════════════════════════════════════════

    private ModPackCreatorDialogResult BuildDialogResult()
    {
        var allDone = Engine.ExecutionAllDone;
        var noFailures = Engine.FailedDllNames.Count == 0 && Engine.FailedCustomFileNames.Count == 0;
        var shareFinalized = _shareResult is { Success: true } &&
                             string.IsNullOrWhiteSpace(Engine.PendingSharePackCode);
        var fullyOk = allDone && noFailures && (!Engine.SharesOnline || shareFinalized);

        var effectiveMode = Engine.SharesOnline
            ? (Engine.CreatesLocalInstance ? ModPackCreatorMode.CreateAndShare : ModPackCreatorMode.ShareExisting)
            : ModPackCreatorMode.InstallLocal;

        // Paczek zwracamy tylko gdy jest sfinalizowany — częściowy stan idzie przez WasCancelled,
        // więc host nie otwiera okienka kodu dla niedokończonego udostępnienia.
        var shareResultToSend = shareFinalized ? _shareResult : null;

        return new ModPackCreatorDialogResult
        {
            Mode = effectiveMode,
            ShareResult = shareResultToSend,
            CreatedInstanceId = Engine.CreatedInstanceId ?? _runInstance?.InstanceId,
            InstalledDllModIds = Engine.InstalledDllIds.ToList(),
            FailedDllNames = Engine.FailedDllNames.ToList(),
            WasCancelled = !fullyOk,
            CreatedPackCodeBeforeStop = shareResultToSend == null
                ? Engine.PendingSharePackCode ?? Engine.ResultPackCode
                : null
        };
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private sealed class SimpleProgressReporter : IProgressReporter
    {
        private readonly Action<int> _onProgress;
        public SimpleProgressReporter(Action<int> onProgress) => _onProgress = onProgress;
        public void Report(int percent, string? message = null) => _onProgress(percent);
    }

    private sealed class SimpleDiagnosticsOutput : IDiagnosticsOutput
    {
        public void Write(string message) =>
            System.Diagnostics.Debug.WriteLine($"[ModPackWizard] {message}");
    }
}
