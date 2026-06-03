using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.ViewModels
{
    public partial class MainWindowViewModel
    {
        private bool _isBulkSelectionMode;
        private bool _isBulkQueueRunning;
        private int _bulkQueueCurrent;
        private int _bulkQueueTotal;
        private string _bulkQueueStatusText = string.Empty;
        private readonly ModInstallQueue _modInstallQueue = new();
        private List<string> _modsWithAvailableUpdates = new();

        public bool IsBulkSelectionMode
        {
            get => _isBulkSelectionMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _isBulkSelectionMode, value);
                this.RaisePropertyChanged(nameof(IsBulkActionBarVisible));
                if (!value)
                    ClearBulkSelection();
            }
        }

        public bool IsBulkQueueRunning
        {
            get => _isBulkQueueRunning;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isBulkQueueRunning, value);
                this.RaisePropertyChanged(nameof(IsBulkActionBarVisible));
                this.RaisePropertyChanged(nameof(IsBulkQueueBannerVisible));
            }
        }

        public bool IsBulkQueueBannerVisible => IsBulkQueueRunning;

        public bool IsBulkActionBarVisible => IsBulkSelectionMode || IsBulkQueueRunning;

        public int BulkSelectedCount => Mods.Count(m => m.IsCheckedForBulk && m.IsBulkEligible);

        public int BulkInstallEligibleCount =>
            GetBulkSelectedMods().Count(m => !m.IsInstalled);

        public int BulkUpdateEligibleCount =>
            GetBulkSelectedMods().Count(m => m.IsInstalled && m.HasUpdateAvailable);

        public int BulkUninstallEligibleCount =>
            GetBulkSelectedMods().Count(m => m.IsInstalled);

        public bool ShowBulkInstallButton => BulkInstallEligibleCount > 0;
        public bool ShowBulkUpdateButton => BulkUpdateEligibleCount > 0;
        public bool ShowBulkUninstallButton => BulkUninstallEligibleCount > 0;

        public string BulkSelectedCountText =>
            _localizationService.GetFormatted("UI.Bulk.SelectedCount", BulkSelectedCount);

        public string BulkInstallButtonLabel =>
            _localizationService.GetFormatted("UI.Bulk.InstallSelected", BulkInstallEligibleCount);

        public string BulkUpdateButtonLabel =>
            _localizationService.GetFormatted("UI.Bulk.UpdateSelected", BulkUpdateEligibleCount);

        public string BulkUninstallButtonLabel =>
            _localizationService.GetFormatted("UI.Bulk.UninstallSelected", BulkUninstallEligibleCount);

        private void RaiseBulkUiProperties()
        {
            this.RaisePropertyChanged(nameof(BulkSelectedCount));
            this.RaisePropertyChanged(nameof(BulkInstallEligibleCount));
            this.RaisePropertyChanged(nameof(BulkUpdateEligibleCount));
            this.RaisePropertyChanged(nameof(BulkUninstallEligibleCount));
            this.RaisePropertyChanged(nameof(ShowBulkInstallButton));
            this.RaisePropertyChanged(nameof(ShowBulkUpdateButton));
            this.RaisePropertyChanged(nameof(ShowBulkUninstallButton));
            this.RaisePropertyChanged(nameof(BulkSelectedCountText));
            this.RaisePropertyChanged(nameof(BulkInstallButtonLabel));
            this.RaisePropertyChanged(nameof(BulkUpdateButtonLabel));
            this.RaisePropertyChanged(nameof(BulkUninstallButtonLabel));
        }

        public int BulkQueueCurrent
        {
            get => _bulkQueueCurrent;
            private set => this.RaiseAndSetIfChanged(ref _bulkQueueCurrent, value);
        }

        public int BulkQueueTotal
        {
            get => _bulkQueueTotal;
            private set => this.RaiseAndSetIfChanged(ref _bulkQueueTotal, value);
        }

        public string BulkQueueStatusText
        {
            get => _bulkQueueStatusText;
            private set => this.RaiseAndSetIfChanged(ref _bulkQueueStatusText, value);
        }

        public ReactiveCommand<Unit, Unit> ToggleBulkSelectionModeCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> CloseModDetailCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> BulkInstallSelectedCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> BulkUpdateSelectedCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> BulkUninstallSelectedCommand { get; private set; } = null!;
        public ReactiveCommand<ModItem, Unit> ToggleBulkModCheckCommand { get; private set; } = null!;

        private void InitializeBulkOperations()
        {
            ToggleBulkSelectionModeCommand = ReactiveCommand.Create(ToggleBulkSelectionMode);
            CloseModDetailCommand = ReactiveCommand.Create(CloseModDetail);
            ToggleBulkModCheckCommand = ReactiveCommand.Create<ModItem>(ToggleBulkModCheck);
            BulkInstallSelectedCommand = ReactiveCommand.CreateFromTask(BulkInstallSelectedAsync);
            BulkUpdateSelectedCommand = ReactiveCommand.CreateFromTask(BulkUpdateSelectedAsync);
            BulkUninstallSelectedCommand = ReactiveCommand.CreateFromTask(BulkUninstallSelectedAsync);
        }

        private void ToggleBulkSelectionMode()
        {
            IsBulkSelectionMode = !IsBulkSelectionMode;
        }

        private void CloseModDetail()
        {
            if (SelectedMod?.IsInstalling == true)
                return;

            SelectedMod = null;
        }

        private void ClearBulkSelection()
        {
            foreach (var mod in Mods)
                mod.IsCheckedForBulk = false;

            RaiseBulkUiProperties();
        }

        private void ToggleBulkModCheck(ModItem mod)
        {
            if (mod == null || !IsBulkSelectionMode || !mod.IsBulkEligible)
                return;

            mod.IsCheckedForBulk = !mod.IsCheckedForBulk;
            RaiseBulkUiProperties();
        }

        /// <summary>
        /// Ustawia badge aktualizacji na kafelkach po sprawdzeniu aktualizacji.
        /// </summary>
        internal void SyncModUpdateBadges(IEnumerable<string> modNamesWithUpdates)
        {
            _modsWithAvailableUpdates = modNamesWithUpdates.ToList();
            var names = new HashSet<string>(_modsWithAvailableUpdates, StringComparer.OrdinalIgnoreCase);

            foreach (var mod in Mods)
                mod.HasUpdateAvailable = names.Contains(mod.Name);

            RaiseBulkUiProperties();
        }

        private List<ModItem> GetBulkSelectedMods() =>
            Mods.Where(m => m.IsCheckedForBulk && m.IsBulkEligible).ToList();

        private async Task BulkInstallSelectedAsync()
        {
            var selected = GetBulkSelectedMods()
                .Where(m => !m.IsInstalled)
                .ToList();

            if (selected.Count == 0)
                return;

            await RunBulkQueueAsync(
                selected.Select(m => new ModQueueItem
                {
                    ModId = m.Id,
                    ModName = m.Name,
                    Operation = ModQueueOperation.Install
                }).ToList(),
                async (item, _) =>
                {
                    var mod = Mods.FirstOrDefault(m => m.Id == item.ModId);
                    if (mod == null)
                    {
                        return new ModInstallQueueItemResult
                        {
                            ModName = item.ModName,
                            Success = false,
                            ErrorMessage = "Mod not found"
                        };
                    }

                    var success = await InstallModItemAsync(mod, showPostInstallFlow: false);
                    return new ModInstallQueueItemResult
                    {
                        ModName = item.ModName,
                        Success = success
                    };
                });
        }

        private async Task BulkUpdateSelectedAsync()
        {
            var selected = GetBulkSelectedMods()
                .Where(m => m.HasUpdateAvailable && m.IsInstalled)
                .ToList();

            if (selected.Count == 0)
                return;

            var updateManager = new ModUpdateManager();
            var check = await updateManager.CheckForUpdatesAsync();
            if (!check.Success)
                return;

            var updatesByName = check.InstalledModUpdates
                .ToDictionary(u => u.ModName, StringComparer.OrdinalIgnoreCase);

            await RunBulkQueueAsync(
                selected.Select(m => new ModQueueItem
                {
                    ModId = m.Id,
                    ModName = m.Name,
                    Operation = ModQueueOperation.Update
                }).ToList(),
                async (item, _) =>
                {
                    var mod = Mods.FirstOrDefault(m => m.Id == item.ModId);
                    if (mod == null || !updatesByName.TryGetValue(item.ModName, out var updateInfo))
                    {
                        return new ModInstallQueueItemResult
                        {
                            ModName = item.ModName,
                            Success = false,
                            ErrorMessage = "No update info"
                        };
                    }

                    var success = await UpdateSingleModWithDialogAsync(mod, updateInfo, progressDialog: null);
                    return new ModInstallQueueItemResult
                    {
                        ModName = item.ModName,
                        Success = success
                    };
                });

            await CheckForModUpdatesForStatusBarAsync(force: true);
        }

        private async Task BulkUninstallSelectedAsync()
        {
            var selected = GetBulkSelectedMods()
                .Where(m => m.IsInstalled)
                .ToList();

            if (selected.Count == 0)
                return;

            var confirmed = await _userInteractionService.ShowConfirmAsync(
                _localizationService.GetFormatted("UI.Bulk.UninstallConfirm", selected.Count),
                _localizationService.Get("UI.Bulk.UninstallTitle"));

            if (!confirmed)
                return;

            await RunBulkQueueAsync(
                selected.Select(m => new ModQueueItem
                {
                    ModId = m.Id,
                    ModName = m.Name,
                    Operation = ModQueueOperation.Uninstall
                }).ToList(),
                async (item, _) =>
                {
                    var mod = Mods.FirstOrDefault(m => m.Id == item.ModId);
                    if (mod == null)
                    {
                        return new ModInstallQueueItemResult
                        {
                            ModName = item.ModName,
                            Success = false,
                            ErrorMessage = "Mod not found"
                        };
                    }

                    await Dispatcher.UIThread.InvokeAsync(() => SelectedMod = mod);
                    var success = await UninstallModItemSilentAsync(mod);
                    return new ModInstallQueueItemResult
                    {
                        ModName = item.ModName,
                        Success = success
                    };
                });

            await RefreshModsListAsync();
        }

        private async Task RunBulkQueueAsync(
            IReadOnlyList<ModQueueItem> items,
            Func<ModQueueItem, CancellationToken, Task<ModInstallQueueItemResult>> processItemAsync)
        {
            if (items.Count == 0)
                return;

            IsBulkSelectionMode = false;
            IsBulkQueueRunning = true;
            BulkQueueTotal = items.Count;
            BulkQueueCurrent = 0;

            var progress = new Progress<ModInstallQueueProgress>(p =>
            {
                BulkQueueCurrent = p.CurrentIndex;
                BulkQueueTotal = p.Total;
                BulkQueueStatusText = _localizationService.GetFormatted(
                    "UI.Bulk.QueueProgress",
                    p.CurrentIndex,
                    p.Total,
                    p.CurrentModName);
            });

            try
            {
                await _modInstallQueue.RunAsync(items, processItemAsync, progress);
            }
            finally
            {
                IsBulkQueueRunning = false;
                BulkQueueStatusText = string.Empty;
                ClearBulkSelection();
                await RefreshModsListAsync();
                await RefreshStatusBarAsync();
            }
        }

        /// <summary>
        /// Odinstalowanie bez dialogu (po potwierdzeniu bulk).
        /// </summary>
        private async Task<bool> UninstallModItemSilentAsync(ModItem currentSelectedMod)
        {
            if (string.IsNullOrEmpty(currentSelectedMod.InstallPath))
                return false;

            try
            {
                currentSelectedMod.ShowProgress = true;
                currentSelectedMod.IsInstalling = true;
                currentSelectedMod.InstallStatusMessage = _localizationService.Get("ModOperations.DeletingFiles");

                var diagnosticsOutput = new UIDiagnosticsOutput(msg =>
                    System.Diagnostics.Debug.WriteLine($"[BulkUninstall] {msg}"));

                bool deleteSuccess = await FileSystemUtilities.SafeDeleteDirectoryAsync(
                    currentSelectedMod.InstallPath,
                    currentSelectedMod.Name,
                    diagnosticsOutput,
                    ShowConfirmDialogAsync);

                if (!deleteSuccess)
                    return false;

                var configService = new ConfigService();
                var configs = configService.LoadConfig();
                var modConfig = configs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);
                if (modConfig != null)
                {
                    modConfig.InstallPath = string.Empty;
                    ConfigManager.SaveConfig(configs);
                }

                currentSelectedMod.InstallPath = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BulkUninstall] {ex.Message}");
                return false;
            }
            finally
            {
                currentSelectedMod.ShowProgress = false;
                currentSelectedMod.IsInstalling = false;
                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;
            }
        }
    }
}
