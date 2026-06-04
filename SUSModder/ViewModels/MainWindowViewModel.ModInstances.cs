using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
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
            DllAddons
        }

        private ModBrowserTab _activeBrowserTab = ModBrowserTab.Catalog;
        private ModInstanceItem? _selectedPackInstance;
        private readonly ObservableCollection<ModInstanceItem> _packInstances = new();

        public ObservableCollection<ModInstanceItem> PackInstances => _packInstances;

        public ModBrowserTab ActiveBrowserTab
        {
            get => _activeBrowserTab;
            set
            {
                if (_activeBrowserTab == value)
                    return;

                if (IsBulkSelectionMode)
                    IsBulkSelectionMode = false;

                this.RaiseAndSetIfChanged(ref _activeBrowserTab, value);
                RaiseBulkUiProperties();
                this.RaisePropertyChanged(nameof(IsMyPacksTab));
                this.RaisePropertyChanged(nameof(IsCatalogTab));
                this.RaisePropertyChanged(nameof(IsDllAddonsTab));
                this.RaisePropertyChanged(nameof(IsBrowserGridVisible));
                this.RaisePropertyChanged(nameof(IsPackInstancesGridVisible));
                this.RaisePropertyChanged(nameof(IsDllAddonsGridVisible));
                this.RaisePropertyChanged(nameof(ShowPackInstancesEmptyState));
                this.RaisePropertyChanged(nameof(ShowBrowserNoResults));
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
            }
        }

        public bool IsMyPacksTab => ActiveBrowserTab == ModBrowserTab.MyPacks;
        public bool IsCatalogTab => ActiveBrowserTab == ModBrowserTab.Catalog;
        public bool IsDllAddonsTab => ActiveBrowserTab == ModBrowserTab.DllAddons;
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
                this.RaisePropertyChanged(nameof(IsPackInstanceSelected));
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

        public ReactiveCommand<ModBrowserTab, Unit> SelectBrowserTabCommand { get; private set; } = null!;
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
                _packInstances.Clear();
                foreach (var item in items)
                    _packInstances.Add(item);

                this.RaisePropertyChanged(nameof(HasPackInstances));
                this.RaisePropertyChanged(nameof(ShowPackInstancesEmptyState));

                if (SelectedPackInstance != null)
                {
                    var refreshed = _packInstances.FirstOrDefault(p => p.InstanceId == SelectedPackInstance.InstanceId);
                    SelectedPackInstance = refreshed;
                }

                RefreshTrayQuickLaunchList();
                CapturePackInstancesSnapshot();
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

            var configService = new ConfigService();
            var repo = App.GetService<IModInstanceRepository>();
            var instance = repo.GetInstance(pack.InstanceId);
            if (instance == null)
                return;

            pack.IsBusy = true;
            pack.Progress = 0;
            pack.StatusMessage = _localizationService.Get("UI.Packs.UpdateProgress");

            try
            {
                var updatedMod = await configService.CheckInstanceUpdateAsync(instance);
                if (updatedMod == null)
                {
                    ToastService.ShowInfo(_localizationService.Get("UI.Packs.NoUpdateAvailable"));
                    await RefreshPackInstancesAsync();
                    return;
                }

                var catalog = configService.LoadConfig();
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

                await App.GetService<ModInstanceInstaller>().UpdateInstanceAsync(
                    pack.InstanceId,
                    updatedMod,
                    catalog,
                    DeterminePlatform(),
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
                await ShowErrorDialogAsync(ex.Message, title);
            }
        }

        private Task ShareSelectedPackInstanceViaCreatorAsync() =>
            ShowModPackCreatorDialogAsync(ModPackCreatorMode.ShareOnline);

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
    }
}
