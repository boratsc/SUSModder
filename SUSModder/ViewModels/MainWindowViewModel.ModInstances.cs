using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Services;
using SUSModder.ViewModels.Helpers;
using SUSModder.Views;

namespace SUSModder.ViewModels
{
    public partial class MainWindowViewModel
    {
        public enum ModBrowserTab
        {
            MyPacks,
            Catalog,
            DllAddons,
            Roles,
            Discords
        }

        private ModBrowserTab _activeBrowserTab = ModBrowserTab.Catalog;
        private ModInstanceItem? _selectedPackInstance;
        private readonly ObservableCollection<ModInstanceItem> _packInstances = new();
        private int _packRefreshGeneration;
        private int _browserNavigationVersion;

        public ObservableCollection<ModInstanceItem> PackInstances => _packInstances;

        public ModBrowserTab ActiveBrowserTab
        {
            get => _activeBrowserTab;
            set
            {
                // Kliknięcie zakładki sidebara (także aktywnej) zamyka panel ustawień.
                if (IsAppSettingsVisible)
                    IsAppSettingsVisible = false;

                if (_activeBrowserTab == value)
                    return;

                CaptureBrowserTabState();
                _browserNavigationVersion++;
                if (IsBulkSelectionMode)
                    IsBulkSelectionMode = false;

                SelectedMod = null;
                SelectedPackInstance = null;
                CloseDllDetail();
                SelectedRoleItem = null;
                this.RaiseAndSetIfChanged(ref _activeBrowserTab, value);
                RestoreBrowserTabFilters();
                RaiseBulkUiProperties();
                this.RaisePropertyChanged(nameof(IsMyPacksTab));
                this.RaisePropertyChanged(nameof(IsCatalogTab));
                this.RaisePropertyChanged(nameof(IsDllAddonsTab));
                this.RaisePropertyChanged(nameof(IsRolesTab));
                this.RaisePropertyChanged(nameof(IsDiscordsTab));
                NotifySidebarTabActive();
                this.RaisePropertyChanged(nameof(IsCustomHeaderTab));
                this.RaisePropertyChanged(nameof(ActiveViewTitle));
                this.RaisePropertyChanged(nameof(BreadcrumbSelection));
                this.RaisePropertyChanged(nameof(ActiveViewSubtitle));
                this.RaisePropertyChanged(nameof(IsBrowserGridVisible));
                this.RaisePropertyChanged(nameof(IsPackInstancesGridVisible));
                this.RaisePropertyChanged(nameof(IsDllAddonsGridVisible));
                this.RaisePropertyChanged(nameof(ShowPackInstancesEmptyState));
                this.RaisePropertyChanged(nameof(ShowBrowserNoResults));
                this.RaisePropertyChanged(nameof(ShowDllAddonsEmptyState));
                ApplyBrowserSearchFilter();
                this.RaisePropertyChanged(nameof(IsBulkChipVisible));
                this.RaisePropertyChanged(nameof(IsModPanelVisible));
                this.RaisePropertyChanged(nameof(IsPackInstancePanelVisible));
                this.RaisePropertyChanged(nameof(IsDllPanelVisible));
                this.RaisePropertyChanged(nameof(IsBrowserDetailPanelVisible));

                if (value == ModBrowserTab.MyPacks)
                {
                    SelectedMod = null;
                    _ = RefreshPackInstancesAsync();
                }
                else
                {
                    SelectedPackInstance = null;
                    if (value != ModBrowserTab.DllAddons)
                        CloseDllDetail();
                }

                if (value == ModBrowserTab.DllAddons)
                    ActivateDllAddonsTab();

                if (value == ModBrowserTab.Roles)
                    ActivateRolesTab();

                if (value == ModBrowserTab.Discords)
                    ActivateDiscordsTab();

                RestoreBrowserTabSelection();
            }
        }

        public bool IsMyPacksTab => ActiveBrowserTab == ModBrowserTab.MyPacks;
        public bool IsCatalogTab => ActiveBrowserTab == ModBrowserTab.Catalog;
        public bool IsDllAddonsTab => ActiveBrowserTab == ModBrowserTab.DllAddons;
        public bool IsDiscordsTab => ActiveBrowserTab == ModBrowserTab.Discords;

        /// <summary>Sidebar: podświetlenie zakładki tylko gdy panel ustawień nie zajmuje treści okna.</summary>
        public bool IsCatalogTabActive => IsCatalogTab && !IsAppSettingsVisible;
        public bool IsMyPacksTabActive => IsMyPacksTab && !IsAppSettingsVisible;
        public bool IsDllAddonsTabActive => IsDllAddonsTab && !IsAppSettingsVisible;
        public bool IsRolesTabActive => IsRolesTab && !IsAppSettingsVisible;
        public bool IsDiscordsTabActive => IsDiscordsTab && !IsAppSettingsVisible;

        private void NotifySidebarTabActive()
        {
            this.RaisePropertyChanged(nameof(IsCatalogTabActive));
            this.RaisePropertyChanged(nameof(IsMyPacksTabActive));
            this.RaisePropertyChanged(nameof(IsDllAddonsTabActive));
            this.RaisePropertyChanged(nameof(IsRolesTabActive));
            this.RaisePropertyChanged(nameof(IsDiscordsTabActive));
        }

        /// <summary>F3d: zakładki z własnym nagłówkiem w panelu (Role, Discordy) — generyczny nagłówek i skeleton ukryte.</summary>
        public bool IsCustomHeaderTab => IsRolesTab || IsDiscordsTab;

        /// <summary>F3d: aktywacja zakładki Discordów — jak Role: zamknij menu „Więcej"; odświeżenie danych po stronie panelu.</summary>
        private void ActivateDiscordsTab()
        {
            IsPaneOpen = false;
        }

        /// <summary>Nazwa aktywnego widoku (breadcrumb na górnej belce + nagłówek listy).</summary>
        public string ActiveViewTitle => IsAppSettingsVisible
            ? _localizationService.Get("UI.Menu.Settings")
            : ActiveBrowserTab switch
            {
                ModBrowserTab.MyPacks => _localizationService.Get("UI.Tabs.MyPacks"),
                ModBrowserTab.DllAddons => _localizationService.Get("UI.Tabs.DllAddons"),
                ModBrowserTab.Roles => _localizationService.Get("UI.Tabs.Roles"),
                ModBrowserTab.Discords => _localizationService.Get("UI.Menu.RecommendedDiscords"),
                _ => _localizationService.Get("UI.Tabs.Catalog")
            };

        /// <summary>E15: segment breadcrumb na górnej belce — nazwa zaznaczenia (pusty = brak zaznaczenia).</summary>
        public string BreadcrumbSelection => IsAppSettingsVisible
            ? string.Empty
            : ResolveBreadcrumbSelection(
                ActiveBrowserTab, SelectedMod, SelectedPackInstance, SelectedDllMod, SelectedRoleModName);

        /// <summary>Czyste mapowanie zaznaczenie → nazwa segmentu (testowalne bez DI; wzorzec: ResolveIconKey).</summary>
        public static string ResolveBreadcrumbSelection(
            ModBrowserTab tab,
            ModItem? selectedMod,
            ModInstanceItem? selectedPackInstance,
            ModItem? selectedDllMod,
            string selectedRoleModName) => tab switch
        {
            ModBrowserTab.Catalog => selectedMod?.Name ?? string.Empty,
            ModBrowserTab.MyPacks => selectedPackInstance?.DisplayName ?? string.Empty,
            ModBrowserTab.DllAddons => selectedDllMod?.Name ?? string.Empty,
            ModBrowserTab.Roles => selectedRoleModName,
            _ => string.Empty
        };

        /// <summary>Podtytuł aktywnego widoku (muted, pod nagłówkiem listy).</summary>
        public string ActiveViewSubtitle => ActiveBrowserTab switch
        {
            ModBrowserTab.MyPacks => _localizationService.Get("UI.Browser.MyPacksSubtitle"),
            ModBrowserTab.DllAddons => _localizationService.Get("UI.Browser.DllAddonsSubtitle"),
            ModBrowserTab.Roles => _localizationService.Get("UI.Roles.Subtitle"),
            ModBrowserTab.Discords => _localizationService.Get("RecommendedDiscords.Subtitle"),
            _ => _localizationService.Get("UI.Browser.CatalogSubtitle")
        };
        public bool IsPackInstancesGridVisible =>
            IsMyPacksTab && !IsModsLoading && HasPackInstances && !ShowBrowserNoResults;
        public bool IsBrowserGridVisible =>
            IsCatalogTab && !IsModsLoading && !ShowBrowserNoResults;
        public bool IsBulkChipVisible => !IsModsLoading;
        public bool HasPackInstances => PackInstances.Count > 0;
        public bool ShowPackInstancesEmptyState =>
            IsMyPacksTab && !IsModsLoading && !HasPackInstances && !ShowBrowserNoResults;

        public ModInstanceItem? SelectedPackInstance
        {
            get => _selectedPackInstance;
            set
            {
                if (ReferenceEquals(_selectedPackInstance, value))
                    return;

                if (value != null && _selectedMod != null)
                {
                    _selectedMod = null;
                    this.RaisePropertyChanged(nameof(SelectedMod));
                    this.RaisePropertyChanged(nameof(IsModSelected));
                    this.RaisePropertyChanged(nameof(IsModPanelVisible));
                }

                if (value != null && _selectedDllMod != null)
                {
                    CloseDllDetail();
                }

                this.RaiseAndSetIfChanged(ref _selectedPackInstance, value);
                IsPackContentsSectionExpanded = false;
                this.RaisePropertyChanged(nameof(IsPackInstanceSelected));
                this.RaisePropertyChanged(nameof(BreadcrumbSelection));
                this.RaisePropertyChanged(nameof(IsModPanelVisible));
                this.RaisePropertyChanged(nameof(IsPackInstancePanelVisible));
                this.RaisePropertyChanged(nameof(IsBrowserDetailPanelVisible));
                this.RaisePropertyChanged(nameof(SelectedPackOriginLabel));
                this.RaisePropertyChanged(nameof(SelectedPackContentsText));
            }
        }

        public bool IsPackInstanceSelected => SelectedPackInstance != null;
        public bool IsPackInstancePanelVisible => IsPackInstanceSelected && !IsAnyToolModalOpen;

        public string SelectedPackOriginLabel =>
            SelectedPackInstance == null
                ? string.Empty
                : _localizationService.Get($"UI.Packs.Origin.{CapitalizeOriginKey(SelectedPackInstance.Origin)}");

        public string SelectedPackContentsText =>
            SelectedPackInstance == null
                ? string.Empty
                : BuildPackContentsText(SelectedPackInstance);

        // ── E4: rozwijana sekcja „Zawartość zestawu" (presentation-only, wzorzec E3) ──
        private bool _isPackContentsSectionExpanded;

        public bool IsPackContentsSectionExpanded
        {
            get => _isPackContentsSectionExpanded;
            private set => this.RaiseAndSetIfChanged(ref _isPackContentsSectionExpanded, value);
        }

        public ReactiveCommand<Unit, Unit> TogglePackContentsSectionCommand { get; private set; } = null!;

        public ReactiveCommand<ModBrowserTab, Unit> SelectBrowserTabCommand { get; private set; } = null!;
        /// <summary>E13: klik „#zestaw" w sidebarze — przechodzi do Moich zestawów i zaznacza ten zestaw.</summary>
        public ReactiveCommand<ModInstanceItem, Unit> SelectPackQuickAccessCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> LaunchPackInstanceCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> UpdatePackInstanceCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> RenamePackInstanceCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ClonePackInstanceCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ApplyPackTouConfigCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> CapturePackTouConfigCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> DeletePackInstanceCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> SharePackInstanceCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ClosePackInstanceDetailCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> OpenPackInstanceFolderCommand { get; private set; } = null!;
        public ReactiveCommand<ModInstanceItem, Unit> PackInstanceDoubleClickCommand { get; private set; } = null!;

        private void InitializeModInstances()
        {
            SelectBrowserTabCommand = ReactiveCommand.Create<ModBrowserTab>(tab => ActiveBrowserTab = tab);
            SelectPackQuickAccessCommand = ReactiveCommand.Create<ModInstanceItem>(item =>
            {
                ActiveBrowserTab = ModBrowserTab.MyPacks;
                // Filtr wyszukiwania mógł ukryć zestaw na liście — wtedy czyścimy wyszukiwanie.
                if (!PackInstances.Contains(item))
                    BrowserSearchText = string.Empty;
                SelectedPackInstance = PackInstances.FirstOrDefault(p => p.InstanceId == item.InstanceId) ?? item;
            });
            LaunchPackInstanceCommand = ReactiveCommand.CreateFromTask(LaunchSelectedPackInstanceAsync);
            UpdatePackInstanceCommand = ReactiveCommand.CreateFromTask(
                UpdateSelectedPackInstanceAsync,
                this.WhenAnyValue(x => x.SelectedPackInstance)
                    .Select(p => p != null && p.HasUpdateAvailable));
            RenamePackInstanceCommand = ReactiveCommand.CreateFromTask(RenameSelectedPackInstanceAsync);
            ClonePackInstanceCommand = ReactiveCommand.CreateFromTask(CloneSelectedPackInstanceAsync);
            ApplyPackTouConfigCommand = ReactiveCommand.CreateFromTask(ApplySelectedPackTouConfigAsync);
            CapturePackTouConfigCommand = ReactiveCommand.CreateFromTask(CaptureSelectedPackTouConfigAsync);
            DeletePackInstanceCommand = ReactiveCommand.CreateFromTask(DeleteSelectedPackInstanceAsync);
            SharePackInstanceCommand = ReactiveCommand.CreateFromTask(ShareSelectedPackInstanceViaCreatorAsync);
            ClosePackInstanceDetailCommand = ReactiveCommand.Create(ClosePackInstanceDetail);
            OpenPackInstanceFolderCommand = ReactiveCommand.Create(OpenSelectedPackInstanceFolder);
            PackInstanceDoubleClickCommand = ReactiveCommand.CreateFromTask<ModInstanceItem>(item =>
            {
                SelectedPackInstance = item;
                return LaunchSelectedPackInstanceAsync();
            });
            TogglePackContentsSectionCommand = ReactiveCommand.Create(() =>
            {
                IsPackContentsSectionExpanded = !IsPackContentsSectionExpanded;
            });
        }

        /// <summary>
        /// Lista do szybkiego uruchamiania z zasobnika — preferuje ostatnio uruchamiane zestawy (instancje).
        /// </summary>
        public IReadOnlyList<TrayModInfo> GetTrayQuickLaunchMods()
        {
            try
            {
                var repo = App.GetService<IModInstanceRepository>();
                var instances = repo.GetPackInstances()
                    .Where(i => !string.IsNullOrWhiteSpace(i.InstallPath) && Directory.Exists(i.InstallPath))
                    .OrderByDescending(i => ParseTraySortDate(i.LastLaunchedAt) ?? ParseTraySortDate(i.UpdatedAt))
                    .Take(3)
                    .Select(i => new TrayModInfo
                    {
                        Id = i.BaseModId,
                        InstanceId = i.InstanceId,
                        Name = i.DisplayName
                    })
                    .ToList();

                if (instances.Count > 0)
                    return instances;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Tray] Instancje: {ex.Message}");
            }

            return Mods?
                .Where(m => m.IsInstalled && m.IsFullMod && !string.IsNullOrEmpty(m.InstallPath))
                .OrderByDescending(m => m.LastUpdated ?? DateTime.MinValue)
                .Take(3)
                .Select(m => new TrayModInfo { Id = m.Id, Name = m.Name })
                .ToList() ?? new List<TrayModInfo>();
        }

        public async Task LaunchPackInstanceByIdAsync(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return;

            var pack = _packInstances.FirstOrDefault(p => p.InstanceId == instanceId);
            if (pack != null)
            {
                SelectedPackInstance = pack;
                await LaunchSelectedPackInstanceAsync();
                return;
            }

            var repo = App.GetService<IModInstanceRepository>();
            var instance = repo.GetInstance(instanceId);
            if (instance == null)
                return;

            var catalog = new ConfigService().LoadConfig();
            var catalogMod = catalog.FirstOrDefault(c => c.Id == instance.BaseModId);
            if (catalogMod == null)
                return;

            var modConfig = new ModConfiguration
            {
                Id = catalogMod.Id,
                ModName = catalogMod.ModName,
                ModType = "full",
                ModVersion = instance.FullModVersion,
                AmongVersion = instance.AmongVersion,
                InstallPath = instance.InstallPath,
                GitHubRepoOrLink = catalogMod.GitHubRepoOrLink,
                EpicGitHubRepoOrLink = catalogMod.EpicGitHubRepoOrLink,
                PngFileName = catalogMod.PngFileName
            };

            var pseudoItem = new ModItem
            {
                Id = catalogMod.Id,
                Name = instance.DisplayName,
                ModType = "full",
                InstallPath = instance.InstallPath,
                ModVersion = instance.FullModVersion,
                AmongVersion = instance.AmongVersion,
                PngFileName = catalogMod.PngFileName
            };

            await LaunchPackInstanceCoreAsync(pseudoItem, modConfig, instanceId);
        }

        public async Task RefreshPackInstancesAsync()
        {
            var generation = System.Threading.Interlocked.Increment(ref _packRefreshGeneration);
            var navigationVersion = _browserNavigationVersion;
            if (IsMyPacksTab && SelectedPackInstance != null)
                _browserSession.For(ModBrowserTab.MyPacks).InstanceId = SelectedPackInstance.InstanceId;
            var repo = App.GetService<IModInstanceRepository>();
            var configService = new ConfigService();
            var catalog = configService.LoadConfig();

            var items = new List<ModInstanceItem>();
            foreach (var instance in repo.GetPackInstances().OrderByDescending(i => i.UpdatedAt))
            {
                var catalogMod = catalog.FirstOrDefault(c => c.Id == instance.BaseModId);
                var dllCount = repo.GetDlls(instance.InstanceId).Count;
                var hasTou = repo.GetConfigs(instance.InstanceId)
                    .Any(c => string.Equals(c.ConfigType, "tou", StringComparison.OrdinalIgnoreCase));
                var item = new ModInstanceItem(instance, catalogMod?.PngFileName, dllCount, hasTou);
                item.HasUpdateAvailable = ModInstanceUpdateChecker.HasCatalogUpdate(instance, catalogMod);
                items.Add(item);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _packRefreshGeneration)
                    return;
                var preserveView = IsMyPacksTab && navigationVersion == _browserNavigationVersion;
                var liveSelectedId = preserveView ? SelectedPackInstance?.InstanceId : null;
                if (preserveView) BrowserScrollCaptureRequested?.Invoke(ModBrowserTab.MyPacks);
                _packInstances.Clear();
                foreach (var item in items)
                    _packInstances.Add(item);

                this.RaisePropertyChanged(nameof(HasPackInstances));
                this.RaisePropertyChanged(nameof(ShowPackInstancesEmptyState));

                RefreshTrayQuickLaunchList();
                CapturePackInstancesSnapshot();
                if (preserveView)
                {
                    SelectedPackInstance = PackInstances.FirstOrDefault(item => item.InstanceId == liveSelectedId);
                    BrowserScrollRestoreRequested?.Invoke(ModBrowserTab.MyPacks,
                        _browserSession.For(ModBrowserTab.MyPacks).ScrollOffset);
                }
            });
        }

        private static DateTime? ParseTraySortDate(string? iso) =>
            DateTime.TryParse(iso, out var dt) ? dt : null;

        private void RefreshTrayQuickLaunchList()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow is Views.MainWindow mainWindow)
                {
                    mainWindow.UpdateTrayModsList();
                }
            });
        }

        private async Task UpdateSelectedPackInstanceAsync()
        {
            var pack = SelectedPackInstance;
            if (pack == null || !pack.HasUpdateAvailable)
                return;

            var repo = App.GetService<IModInstanceRepository>();
            var instance = repo.GetInstance(pack.InstanceId);
            if (instance == null)
                return;

            pack.IsBusy = true;
            pack.Progress = 0;
            pack.StatusMessage = _localizationService.Get("UI.Packs.UpdateProgress");

            try
            {
                var platform = DeterminePlatform();
                var progressReporter = new UIProgressReporter((percent, _) =>
                {
                    pack.Progress = percent;
                });
                var diagnostics = new UIDiagnosticsOutput(msg =>
                    System.Diagnostics.Debug.WriteLine($"[PackUpdate] {msg}"));

                var silentInteraction = new InstallationSilentUserInteraction();
                var callbacks = new ModManagerUserCallbacks
                {
                    ConfirmAsync = silentInteraction.ShowConfirmAsync,
                    ShowErrorAsync = silentInteraction.ShowErrorAsync,
                    ShowInfoAsync = silentInteraction.ShowInfoAsync,
                    RunSteamQrDownloadAsync = _userInteractionService.RunSteamQrDownloadAsync
                };

                if (!string.IsNullOrEmpty(instance.SourcePackCode))
                {
                    var modPackService = App.GetService<IModPackService>();
                    var remotePack = await modPackService.GetPackAsync(instance.SourcePackCode);
                    if (remotePack == null)
                    {
                        ToastService.ShowInfo(_localizationService.Get("ModPacks.PackNotFound"));
                        await RefreshPackInstancesAsync();
                        return;
                    }

                    var configService = new ConfigService();
                    var installer = new ModPackInstaller(
                        _configuration ?? App.GetService<Microsoft.Extensions.Configuration.IConfiguration>(),
                        configService,
                        _dllModificationService,
                        diagnostics,
                        App.GetService<ModInstanceInstaller>(),
                        repo);

                    var installResult = await installer.UpdateExistingInstanceAsync(
                        pack.InstanceId,
                        remotePack,
                        platform,
                        new Progress<(int percent, string message)>(p =>
                        {
                            pack.Progress = p.percent;
                            if (!string.IsNullOrWhiteSpace(p.message))
                                pack.StatusMessage = p.message;
                        }),
                        callbacks);

                    if (!installResult.Success && installResult.InstalledMods.Count == 0)
                    {
                        ToastService.ShowInfo(
                            installResult.ErrorMessage ?? _localizationService.Get("UI.Packs.NoUpdateAvailable"));
                        await RefreshPackInstancesAsync();
                        return;
                    }

                    await RefreshPackInstancesAsync();
                    await CheckForModUpdatesForStatusBarAsync(force: true);
                    await CheckModPackUpdatesAsync();

                    var newVersion = remotePack.HasCustomFullMod
                        ? remotePack.CustomFullMod?.Version
                        : remotePack.FullMod?.Version;
                    ToastService.ShowSuccess(
                        _localizationService.GetFormatted(
                            "UI.Packs.UpdateSuccess",
                            pack.DisplayName,
                            newVersion ?? instance.FullModVersion ?? "?"));
                    return;
                }

                var catalogConfigService = new ConfigService();
                var updatedMod = await catalogConfigService.CheckInstanceUpdateAsync(instance);
                if (updatedMod == null)
                {
                    ToastService.ShowInfo(_localizationService.Get("UI.Packs.NoUpdateAvailable"));
                    await RefreshPackInstancesAsync();
                    return;
                }

                var catalog = catalogConfigService.LoadConfig();

                await App.GetService<ModInstanceInstaller>().UpdateInstanceAsync(
                    pack.InstanceId,
                    updatedMod,
                    catalog,
                    platform,
                    progressReporter,
                    diagnostics,
                    callbacks);

                await RefreshPackInstancesAsync();
                await CheckForModUpdatesForStatusBarAsync(force: true);

                ToastService.ShowSuccess(
                    _localizationService.GetFormatted("UI.Packs.UpdateSuccess", pack.DisplayName, updatedMod.ModVersion));
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(ex.Message, _localizationService.Get("UI.Packs.Update"));
            }
            finally
            {
                pack.IsBusy = false;
                pack.Progress = 0;
                pack.StatusMessage = string.Empty;
            }
        }

        /// <summary>
        /// F3b: instalacja zestawu z listy polecanych Discordów — reuse flow podglądu i instalacji
        /// (OpenModPackFlowAsync). Flow po udanej instalacji sam przechodzi do „Moje zestawy";
        /// przy anulowaniu użytkownik wraca do zakładki Discordów.
        /// </summary>
        public Task InstallServerPackAsync(string packCode)
        {
            return OpenModPackFlowAsync(packCode, autoInstall: false);
        }

        /// <summary>
        /// F3b: aktualizacja zestawu z listy polecanych Discordów — wybiera instancję po SourcePackCode
        /// (preferując shared_pack, jak checker aktualizacji) i reuse istniejącą ścieżkę aktualizacji
        /// (UpdateSelectedPackInstanceAsync). Przed startem potwierdzenie (config ToU z paczki nadpisuje
        /// lokalny). Brak instancji = instalacja.
        /// </summary>
        public async Task UpdateServerPackAsync(string packCode, string? packName = null, string? serverName = null)
        {
            var repo = App.GetService<IModInstanceRepository>();
            var instance = DiscordServerViewModel.FindPackInstance(repo.GetPackInstances(), packCode);
            if (instance == null)
            {
                await InstallServerPackAsync(packCode);
                return;
            }

            var confirmed = await ShowConfirmDialogAsync(
                _localizationService.GetFormatted("RecommendedDiscords.UpdateConfirmMessage",
                    packName ?? instance.DisplayName, serverName ?? packCode),
                _localizationService.Get("RecommendedDiscords.UpdateConfirmTitle"),
                _localizationService.Get("RecommendedDiscords.UpdatePack"),
                _localizationService.Get("UI.Buttons.Cancel"));
            if (!confirmed)
                return;

            ActiveBrowserTab = ModBrowserTab.MyPacks;
            await RefreshPackInstancesAsync();

            var item = PackInstances.FirstOrDefault(p => p.InstanceId == instance.InstanceId);
            if (item == null)
            {
                // W3: filtr wyszukiwania mógł ukryć zestaw na liście — wyczyść go i szukaj ponownie
                // (wzorzec SelectPackQuickAccessCommand), zamiast cicho kończyć po potwierdzeniu.
                BrowserSearchText = string.Empty;
                item = PackInstances.FirstOrDefault(p => p.InstanceId == instance.InstanceId);
            }
            if (item == null)
            {
                // I11b: instancja zniknęła po potwierdzeniu (równoległe usunięcie) — neutralny toast zamiast ciszy.
                ToastService.ShowInfo(_localizationService.Get("ModPacks.PackNotFound"));
                return;
            }
            SelectedPackInstance = item;

            // W2: gate z manifestu paczki (PackUpdateInfo), nie ze sticky flagi katalogowej —
            // katalog wyprzedza pin instancji w normalnym stanie i dawał aktualizację-widmo.
            // onlyNew:true — bez delta-toasta wszystkich aktualizacji w środku jawnego flow (I11c).
            if (!item.HasManifestPackUpdate)
                await CheckModPackUpdatesAsync(onlyNew: true);

            if (!item.HasManifestPackUpdate)
            {
                ToastService.ShowInfo(_localizationService.Get("UI.Packs.NoUpdateAvailable"));
                return;
            }

            await UpdateSelectedPackInstanceAsync();
        }

        private async Task LaunchSelectedPackInstanceAsync()
        {
            var pack = SelectedPackInstance;
            if (pack == null)
                return;

            var configService = new ConfigService();
            var catalog = configService.LoadConfig();
            var catalogMod = catalog.FirstOrDefault(c => c.Id == pack.BaseModId);
            if (catalogMod == null)
            {
                await ShowErrorDialogAsync(
                    _localizationService.Get("UI.Packs.LaunchCatalogMissing"),
                    _localizationService.Get("UI.Buttons.Launch"));
                return;
            }

            var modConfig = new ModConfiguration
            {
                Id = catalogMod.Id,
                ModName = catalogMod.ModName,
                ModType = "full",
                ModVersion = pack.FullModVersion,
                AmongVersion = pack.AmongVersion,
                InstallPath = pack.InstallPath,
                GitHubRepoOrLink = catalogMod.GitHubRepoOrLink,
                EpicGitHubRepoOrLink = catalogMod.EpicGitHubRepoOrLink,
                PngFileName = catalogMod.PngFileName
            };

            var pseudoItem = new ModItem
            {
                Id = catalogMod.Id,
                Name = pack.DisplayName,
                ModType = "full",
                InstallPath = pack.InstallPath,
                ModVersion = pack.FullModVersion,
                AmongVersion = pack.AmongVersion,
                PngFileName = pack.PngFileName
            };

            pack.IsBusy = true;
            pack.Progress = 0;
            try
            {
                SelectedMod = pseudoItem;
                await LaunchPackInstanceCoreAsync(pseudoItem, modConfig, pack.InstanceId);
            }
            finally
            {
                pack.IsBusy = false;
                pack.Progress = 0;
                pack.StatusMessage = string.Empty;
            }
        }

        private async Task LaunchPackInstanceCoreAsync(ModItem uiItem, ModConfiguration modConfig, string instanceId)
        {
            var statsChoice = await HandleSUStatsChoice(modConfig);
            await RemoveApiSetFileIfExists(modConfig);
            if (statsChoice == null)
                return;

            if (statsChoice.Value)
                await CreateApiSetFileIfNeeded(modConfig);
            else
            {
                await RemoveApiSetFileIfExists(modConfig);
                ClearSUStatsSelection();
            }

            uiItem.ShowProgress = true;
            uiItem.IsInstalling = true;

            try
            {
                ModInstanceTouConfigService.TryApplyInstanceConfigToGlobal(
                    App.GetService<IModInstanceRepository>(),
                    instanceId);

                var userSettings = _userSettingsService.LoadUserSettings();
                var mode = userSettings.Mode ?? "steam";

                if (mode.Equals("epic", StringComparison.OrdinalIgnoreCase))
                    await LaunchEpicGameAsync(uiItem, modConfig);
                else
                    await LaunchSteamGameAsync(uiItem, modConfig);

                App.GetService<ModInstanceInstaller>().MarkInstanceLaunched(instanceId);
                await RefreshPackInstancesAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(ex.Message, _localizationService.Get("UI.Buttons.Launch"));
            }
            finally
            {
                uiItem.ShowProgress = false;
                uiItem.IsInstalling = false;
            }
        }

        private async Task RenameSelectedPackInstanceAsync()
        {
            var pack = SelectedPackInstance;
            if (pack == null)
                return;

            var newName = await ShowPromptDialogAsync(
                _localizationService.Get("UI.Packs.RenamePrompt"),
                _localizationService.Get("UI.Packs.Rename"));

            if (string.IsNullOrWhiteSpace(newName))
                return;

            try
            {
                await App.GetService<ModInstanceInstaller>()
                    .RenameInstanceAsync(pack.InstanceId, newName, _diagnosticsOutput);
                await RefreshPackInstancesAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(ex.Message, _localizationService.Get("UI.Packs.Rename"));
            }
        }

        private async Task ApplySelectedPackTouConfigAsync()
        {
            var pack = SelectedPackInstance;
            if (pack == null || !pack.HasTouConfig)
                return;

            try
            {
                var applied = ModInstanceTouConfigService.TryApplyInstanceConfigToGlobal(
                    App.GetService<IModInstanceRepository>(),
                    pack.InstanceId);
                if (!applied)
                {
                    await ShowMessageAsync(
                        _localizationService.Get("UI.Packs.TouConfigMissing"),
                        _localizationService.Get("UI.Packs.EditTouConfig"));
                    return;
                }

                await ShowMessageAsync(
                    _localizationService.Get("UI.Packs.TouConfigApplied"),
                    _localizationService.Get("UI.Packs.EditTouConfig"));
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(ex.Message, _localizationService.Get("UI.Packs.EditTouConfig"));
            }
        }

        private async Task CaptureSelectedPackTouConfigAsync()
        {
            var pack = SelectedPackInstance;
            if (pack == null)
                return;

            try
            {
                var captured = ModInstanceTouConfigService.TryCaptureGlobalToInstance(
                    App.GetService<IModInstanceRepository>(),
                    pack.InstanceId);
                if (!captured)
                {
                    await ShowMessageAsync(
                        _localizationService.Get("UI.Packs.TouConfigGlobalMissing"),
                        _localizationService.Get("UI.Packs.SaveTouConfig"));
                    return;
                }

                await RefreshPackInstancesAsync();
                await ShowMessageAsync(
                    _localizationService.Get("UI.Packs.TouConfigCaptured"),
                    _localizationService.Get("UI.Packs.SaveTouConfig"));
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(ex.Message, _localizationService.Get("UI.Packs.SaveTouConfig"));
            }
        }

        private async Task CloneSelectedPackInstanceAsync()
        {
            var pack = SelectedPackInstance;
            if (pack == null)
                return;

            var mainWindow = GetMainWindow();
            if (mainWindow == null)
                return;

            var suggestedName = _localizationService.GetFormatted("UI.Packs.CloneSuggestedName", pack.DisplayName);
            var dialog = new PackInstanceCloneDialog(_localizationService, suggestedName);
            var options = await dialog.ShowDialog<ModInstanceCloneOptions?>(mainWindow);
            if (options == null)
                return;

            pack.IsBusy = true;
            try
            {
                var clone = await App.GetService<ModInstanceInstaller>().CloneInstanceAsync(
                    pack.InstanceId,
                    options,
                    log: _diagnosticsOutput);

                await RefreshPackInstancesAsync();
                SelectedPackInstance = PackInstances.FirstOrDefault(p => p.InstanceId == clone.InstanceId);
                await ShowMessageAsync(
                    _localizationService.Get("UI.Packs.CloneSuccess"),
                    _localizationService.Get("UI.Packs.CloneTitle"));
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(ex.Message, _localizationService.Get("UI.Packs.CloneTitle"));
            }
            finally
            {
                pack.IsBusy = false;
            }
        }

        private async Task DeleteSelectedPackInstanceAsync()
        {
            var pack = SelectedPackInstance;
            if (pack == null)
                return;

            var title = _localizationService.Get("UI.Packs.DeleteTitle");
            var message = _localizationService.GetFormatted("UI.Packs.DeleteConfirmation", pack.DisplayName, pack.InstallPath);
            if (!await ShowConfirmDialogAsync(message, title))
                return;

            var deleteFiles = await ShowConfirmDialogAsync(
                _localizationService.Get("UI.Packs.DeleteFilesConfirmation"),
                title);

            try
            {
                await App.GetService<ModInstanceInstaller>()
                    .DeleteInstanceAsync(pack.InstanceId, deleteFiles, _diagnosticsOutput);
                SelectedPackInstance = null;
                await RefreshPackInstancesAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(FormatPackInstanceError(ex.Message), title);
            }
        }

        private Task ShareSelectedPackInstanceViaCreatorAsync() =>
            ShowModPackCreatorDialogAsync(ModPackCreatorMode.ShareExisting);

        private void ClosePackInstanceDetail()
        {
            SelectedPackInstance = null;
        }

        private void OpenSelectedPackInstanceFolder()
        {
            var path = SelectedPackInstance?.InstallPath;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignoruj błędy otwierania eksploratora
            }
        }

        private static string CapitalizeOriginKey(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return "Manual";

            return origin.Trim().ToLowerInvariant() switch
            {
                "shared_pack" => "SharedPack",
                "clone" => "Clone",
                "legacy" => "Legacy",
                _ => "Manual"
            };
        }

        private string FormatPackInstanceError(string? errorCodeOrMessage)
        {
            if (string.IsNullOrWhiteSpace(errorCodeOrMessage))
                return _localizationService.Get("ModPacks.CreateFailed");

            return errorCodeOrMessage switch
            {
                "mod_instance_delete_map_mismatch" => _localizationService.Get("UI.Packs.DeleteMapMismatch"),
                "mod_instance_not_found" => _localizationService.Get("UI.Packs.InstanceNotFound"),
                "mod_instance_clone_map_mismatch" => _localizationService.Get("UI.Packs.CloneMapMismatch"),
                _ => errorCodeOrMessage
            };
        }

        private string BuildPackContentsText(ModInstanceItem pack)
        {
            var repo = App.GetService<IModInstanceRepository>();
            var lines = new List<string>();
            foreach (var dll in repo.GetDlls(pack.InstanceId))
            {
                var version = string.IsNullOrWhiteSpace(dll.DllVersion) ? "" : $" v{dll.DllVersion}";
                lines.Add($"- {dll.DllName}{version}");
            }

            if (pack.HasTouConfig)
                lines.Add($"- {_localizationService.Get("ModPacks.TouConfigLabel")}");

            if (lines.Count == 0)
                return _localizationService.Get("UI.Packs.ContentsEmpty");

            return string.Join(Environment.NewLine, lines);
        }

        // ─────────────────────────────────────────────
        // Modpack Update Notification Check
        // ─────────────────────────────────────────────

        // F3g: live monit aktualizacji paczek — toast tylko przy NOWEJ zmianie od ostatniego checku.
        private HashSet<string> _notifiedPackUpdateInstanceIds = new();

        // W1: guard współbieżności checku paczek (wzorzec _modUpdateCheckSemaphore monitora FULL) —
        // startup/timer/otwarcie zakładki nie mogą się nakładać (podwójny toast, stackowanie checków sieciowych).
        private readonly SemaphoreSlim _packUpdateCheckSemaphore = new(1, 1);

        // Rzadszy niż monitor modów (5 min) — admin paczki zmienia ją rzadko.
        private static readonly TimeSpan PackUpdatesRefreshInterval = TimeSpan.FromMinutes(30);

        /// <summary>F3g: po odświeżeniu stanu aktualizacji paczek — panel Discordów odświeża badge wierszy.</summary>
        public event Action? PackUpdateStateRefreshed;

        /// <param name="onlyNew">true = toast tylko dla aktualizacji niepowiadomionych wcześniej (timer, otwarcie zakładki).</param>
        internal async Task CheckModPackUpdatesAsync(bool onlyNew = false)
        {
            // W1: gdy check jest w locie (np. startup), pomijamy zamiast stackować kolejny;
            // event w finally tamtego checku i tak odświeży otwarty panel.
            if (!await _packUpdateCheckSemaphore.WaitAsync(0))
                return;

            try
            {
                var modPackService = App.GetService<IModPackService>();
                var instanceRepo = App.GetService<IModInstanceRepository>();
                var configService = new ConfigService();

                if (modPackService == null || instanceRepo == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ModPackUpdates] Brak wymaganych serwisów, pomijam");
                    return;
                }

                var checker = new ModPackUpdateChecker(
                    modPackService, instanceRepo,
                    _diagnosticsOutput ?? new UIDiagnosticsOutput(_ => { }),
                    configService);

                var results = await checker.CheckAllInstancesAsync();

                if (!results.Any())
                {
                    System.Diagnostics.Debug.WriteLine("[ModPackUpdates] Brak instancji shared_pack do sprawdzenia");
                    return;
                }

                // Zaktualizuj HasUpdateAvailable w istniejących ModInstanceItem
                foreach (var result in results)
                {
                    var packItem = PackInstances.FirstOrDefault(p => p.InstanceId == result.InstanceId);
                    if (packItem != null)
                    {
                        packItem.HasUpdateAvailable = packItem.HasUpdateAvailable || result.HasUpdate;
                        packItem.PackUpdateInfo = result;
                        packItem.PackUpdateChangesText = result.HasUpdate && result.Changes.Count > 0
                            ? string.Join(
                                Environment.NewLine,
                                result.Changes.Select(c =>
                                    "• " + Helpers.ModPackChangeFormatter.Format(_localizationService, c)))
                            : string.Empty;
                    }
                }

                var (toastCount, next) = ComputePackUpdateDelta(results, _notifiedPackUpdateInstanceIds, onlyNew);
                _notifiedPackUpdateInstanceIds = next;
                if (toastCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[ModPackUpdates] Nowe aktualizacje paczek: {toastCount} instancji");
                    ToastService.ShowInfo(
                        _localizationService.GetFormatted("ModPacks.UpdateAvailableCount", toastCount));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ModPackUpdates] Brak nowych aktualizacji paczek");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModPackUpdates] Błąd: {ex.Message}");
            }
            finally
            {
                _packUpdateCheckSemaphore.Release();
                PackUpdateStateRefreshed?.Invoke();
            }
        }

        /// <summary>
        /// F3g: czysta delta powiadomień o aktualizacjach paczek. onlyNew=false liczy wszystkie
        /// aktualne aktualizacje (jak dotąd); onlyNew=true tylko te, których id nie ma w notified.
        /// !HasUpdate czyści id — po aktualizacji i KOLEJNEJ zmianie admina monit wraca.
        /// Nieudany check (CheckSucceeded=false) nie zmienia stanu notified (bez powtórnego toasta po błędzie sieci).
        /// </summary>
        internal static (int ToastCount, HashSet<string> Next) ComputePackUpdateDelta(
            IReadOnlyList<ModPackUpdateInfo> results, HashSet<string> notified, bool onlyNew)
        {
            var next = new HashSet<string>(notified);
            var toastCount = 0;
            foreach (var result in results)
            {
                if (!result.CheckSucceeded)
                    continue;
                if (result.HasUpdate)
                {
                    if (!onlyNew || !notified.Contains(result.InstanceId))
                        toastCount++;
                    next.Add(result.InstanceId);
                }
                else
                {
                    next.Remove(result.InstanceId);
                }
            }
            return (toastCount, next);
        }

        /// <summary>
        /// F3g: timer live monitora paczek (wzorzec StartModUpdatesAutoRefresh). Check na wątku UI
        /// (jak pozostałe wywołania), więc delta i badge bez wyścigów; event odświeża otwartą zakładkę Discordów.
        /// </summary>
        private void StartPackUpdatesAutoRefresh()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(PackUpdatesRefreshInterval);

                    try
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => CheckModPackUpdatesAsync(onlyNew: true));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Auto-refresh pack updates error: {ex.Message}");
                    }
                }
            });
        }
    }
}
