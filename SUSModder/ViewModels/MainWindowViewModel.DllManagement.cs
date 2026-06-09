using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Services;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Zarządzanie dodatkami DLL w układzie Browser + Inspector (zakładka Dodatki DLL).
    /// </summary>
    public partial class MainWindowViewModel
    {
        private readonly ObservableCollection<DllInstallTargetItem> _dllInstallTargets = new();
        private readonly ObservableCollection<DllCompatibilityLineItem> _dllCompatibilityLines = new();
        private readonly CompositeDisposable _dllTargetItemSubscriptions = new();
        private CompatibilityService? _dllCompatibilityService;
        private CancellationTokenSource? _dllCompatibilityLoadCts;

        public ObservableCollection<DllInstallTargetItem> DllInstallTargets => _dllInstallTargets;
        public ObservableCollection<DllCompatibilityLineItem> DllCompatibilityLines => _dllCompatibilityLines;
        public bool HasDllCompatibilityLines => DllCompatibilityLines.Count > 0;
        public bool IsDllCompatibilityLoading { get; private set; }

        public bool IsDllAddonsGridVisible =>
            IsDllAddonsTab && !IsModsLoading && !ShowBrowserNoResults;
        public bool IsDllPanelVisible => SelectedDllMod != null && !IsAnyToolModalOpen;
        public bool HasDllInstalledInTargets => DllInstallTargets.Any(t => t.WasInstalled);
        public bool HasDllTargetPendingChanges => DllInstallTargets.Any(t => t.HasPendingChange);
        public int DllHiddenIncompatibleTargetCount { get; private set; }
        public bool HasDllHiddenIncompatibleTargets => DllHiddenIncompatibleTargetCount > 0;

        public string SelectedDllModVersion => SelectedDllMod?.ModVersion ?? string.Empty;
        public string SelectedDllDescription => SelectedDllMod?.Description ?? string.Empty;

        public string SelectedDllInstalledInText =>
            string.Join(Environment.NewLine,
                DllInstallTargets.Where(t => t.WasInstalled).Select(t => $"• {t.DisplayName}"));

        public ReactiveCommand<Unit, Unit> CloseDllDetailCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ApplyDllTargetChangesCommand { get; private set; } = null!;

        private void InitializeDllBrowser()
        {
            if (_configuration != null)
            {
                try
                {
                    _dllCompatibilityService = new CompatibilityService(
                        _diagnosticsOutput ?? new UIDiagnosticsOutput(_ => { }));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DllInspector] CompatibilityService: {ex.Message}");
                }
            }

            CloseDllDetailCommand = ReactiveCommand.Create(CloseDllDetail);
            ApplyDllTargetChangesCommand = ReactiveCommand.CreateFromTask(ApplyDllTargetChangesAsync);
        }

        private void ActivateDllAddonsTab()
        {
            IsPaneOpen = false;
            IsDllModificationsVisible = false;
            IsDllInstallDialogVisible = false;
            IsInfoPanelVisible = false;
            IsAdditionalActionsVisible = false;
            IsSUStatsConfigVisible = false;
            IsAppSettingsVisible = false;
            IsRecommendedDiscordsVisible = false;
            IsRepairOptionsVisible = false;
            SelectedMod = null;
            SelectedPackInstance = null;
            LoadDllMods();
            this.RaisePropertyChanged(nameof(IsDllAddonsGridVisible));
            this.RaisePropertyChanged(nameof(IsModPanelVisible));
            this.RaisePropertyChanged(nameof(IsDllPanelVisible));
            this.RaisePropertyChanged(nameof(IsBrowserDetailPanelVisible));
        }

        private void ShowDllModifications()
        {
            ActiveBrowserTab = ModBrowserTab.DllAddons;
        }

        private void SelectDllMod(ModItem? dllMod)
        {
            if (dllMod == null)
                return;

            if (IsBulkSelectionMode && dllMod.IsDllBulkEligible)
            {
                dllMod.IsCheckedForBulk = !dllMod.IsCheckedForBulk;
                RaiseBulkUiProperties();
                return;
            }

            SelectedDllMod = dllMod;
            IsDllInstallDialogVisible = false;
            IsDllModificationsVisible = false;
            LoadDllInstallTargets();
            NotifyDllInspectorProperties();
        }

        private void CloseDllDetail()
        {
            _dllCompatibilityLoadCts?.Cancel();
            SelectedDllMod = null;
            DllInstallTargets.Clear();
            DllCompatibilityLines.Clear();
            _dllCompatibilityDisplay.Clear();
            IsDllCompatibilityExpanded = false;
            DllHiddenIncompatibleTargetCount = 0;
            NotifyDllInspectorProperties();
        }

        private void CloseDllDialog()
        {
            IsDllInstallDialogVisible = false;
        }

        private void LoadDllMods()
        {
            try
            {
                var dllConfigs = _dllModificationService.GetDllMods();
                var platform = DeterminePlatform();
                var instanceRepo = App.GetService<IModInstanceRepository>();
                var instances = instanceRepo.GetPackInstances()
                    .Where(i => !string.IsNullOrEmpty(i.InstallPath) && Directory.Exists(i.InstallPath))
                    .ToList();
                var catalog = new ConfigService().LoadConfig();

                DllMods.Clear();
                foreach (var config in dllConfigs)
                {
                    var item = ModItemAdapter.FromConfig(config);
                    var dllConfig = ModItemAdapter.ToConfig(item);
                    var count = CountDllInstallationsForMod(dllConfig, platform, instances, catalog);
                    item.InstalledInCount = count;
                    item.InstalledInSummary = count == 0
                        ? _localizationService.Get("UI.DllManager.NotInstalledAnywhere")
                        : _localizationService.GetFormatted("UI.DllManager.InstalledInCount", count);
                    DllMods.Add(item);
                }

                if (SelectedDllMod != null)
                {
                    var refreshed = DllMods.FirstOrDefault(d => d.Id == SelectedDllMod.Id);
                    SelectedDllMod = refreshed;
                    if (SelectedDllMod != null)
                        LoadDllInstallTargets();
                    else
                        CloseDllDetail();
                }

                CaptureDllModsSnapshot();
                System.Diagnostics.Debug.WriteLine($"Loaded {DllMods.Count} DLL mods");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading DLL mods: {ex.Message}");
            }
        }

        private int CountDllInstallationsForMod(
            ModConfiguration dllConfig,
            string platform,
            IReadOnlyList<ModInstance> instances,
            List<ModConfiguration> catalog)
        {
            var count = 0;
            var instancePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var instance in instances)
            {
                if (!string.IsNullOrEmpty(instance.InstallPath))
                    instancePaths.Add(Path.GetFullPath(instance.InstallPath));

                var catalogMod = catalog.FirstOrDefault(c => c.Id == instance.BaseModId);
                var target = BuildDllTargetConfig(instance, catalogMod);
                if (_dllModificationService.IsDllInstalledInMod(dllConfig, target, platform))
                    count++;
            }

            foreach (var mod in catalog.Where(m =>
                         m.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrEmpty(m.InstallPath) &&
                         Directory.Exists(m.InstallPath)))
            {
                var installPath = mod.InstallPath!;
                if (instancePaths.Contains(Path.GetFullPath(installPath)))
                    continue;

                if (_dllModificationService.IsDllInstalledInMod(dllConfig, mod, platform))
                    count++;
            }

            return count;
        }

        private void LoadDllInstallTargets()
        {
            _ = LoadDllInstallTargetsAndCompatibilityAsync();
        }

        private async Task LoadDllInstallTargetsAndCompatibilityAsync()
        {
            DllInstallTargets.Clear();
            _dllTargetItemSubscriptions.Clear();
            DllHiddenIncompatibleTargetCount = 0;

            if (SelectedDllMod == null)
                return;

            _dllCompatibilityLoadCts?.Cancel();
            _dllCompatibilityLoadCts = new CancellationTokenSource();
            var token = _dllCompatibilityLoadCts.Token;

            IsDllCompatibilityExpanded = false;
            IsDllCompatibilityLoading = true;
            this.RaisePropertyChanged(nameof(IsDllCompatibilityLoading));

            try
            {
                var matrix = new Dictionary<int, CompatibilityInfo>();
                if (_dllCompatibilityService != null)
                {
                    matrix = await _dllCompatibilityService.GetCompatibilityMatrixAsync(SelectedDllMod.Id, token);
                    if (token.IsCancellationRequested)
                        return;
                }

                string platform = DeterminePlatform();
                var dllConfig = ModItemAdapter.ToConfig(SelectedDllMod);
                var instanceRepo = App.GetService<IModInstanceRepository>();
                var instances = instanceRepo.GetPackInstances()
                    .Where(i => !string.IsNullOrEmpty(i.InstallPath) && Directory.Exists(i.InstallPath))
                    .OrderBy(i => i.DisplayName)
                    .ToList();
                var instancePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var catalog = new ConfigService().LoadConfig();
                var hiddenIncompatible = 0;

                foreach (var instance in instances)
                {
                    instancePaths.Add(Path.GetFullPath(instance.InstallPath!));
                    var catalogMod = catalog.FirstOrDefault(c => c.Id == instance.BaseModId);
                    var target = BuildDllTargetConfig(instance, catalogMod);
                    var fullModId = catalogMod?.Id ?? instance.BaseModId;
                    matrix.TryGetValue(fullModId, out var compat);
                    var installed = _dllModificationService.IsDllInstalledInMod(dllConfig, target, platform);

                    if (compat?.Status == CompatibilityStatus.NotWork && !installed)
                    {
                        hiddenIncompatible++;
                        continue;
                    }

                    var item = ModItemAdapter.FromConfig(target);
                    item.TargetInstanceId = instance.InstanceId;
                    item.Name = instance.DisplayName;
                    AddDllInstallTargetRow(CreateDllInstallTargetRow(item, installed, compat));
                }

                var legacyTargets = _dllModificationService.GetModsWithDllInstalled(dllConfig, platform)
                    .Concat(_dllModificationService.GetModsWithoutDllInstalled(dllConfig, platform))
                    .GroupBy(m => m.Id)
                    .Select(g => g.First())
                    .Where(m => !string.IsNullOrEmpty(m.InstallPath)
                                && Directory.Exists(m.InstallPath)
                                && !instancePaths.Contains(Path.GetFullPath(m.InstallPath)))
                    .OrderBy(m => m.ModName);

                foreach (var target in legacyTargets)
                {
                    matrix.TryGetValue(target.Id, out var compat);
                    var installed = _dllModificationService.IsDllInstalledInMod(dllConfig, target, platform);

                    if (compat?.Status == CompatibilityStatus.NotWork && !installed)
                    {
                        hiddenIncompatible++;
                        continue;
                    }

                    var item = ModItemAdapter.FromConfig(target);
                    AddDllInstallTargetRow(CreateDllInstallTargetRow(item, installed, compat));
                }

                DllHiddenIncompatibleTargetCount = hiddenIncompatible;
                PopulateDllCompatibilityLines(matrix, catalog);
                NotifyDllInspectorProperties();
            }
            catch (OperationCanceledException)
            {
                // Ignoruj anulowanie przy szybkim przełączaniu DLL
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading DLL targets: {ex.Message}");
            }
            finally
            {
                IsDllCompatibilityLoading = false;
                NotifyDllInspectorProperties();
            }
        }

        private DllInstallTargetItem CreateDllInstallTargetRow(
            ModItem item,
            bool installed,
            CompatibilityInfo? compat)
        {
            var tooltip = CompatibilityDisplayHelper.GetStatusLabel(compat, _localizationService);
            var warning = CompatibilityDisplayHelper.GetWarning(compat, _localizationService);
            if (!string.IsNullOrWhiteSpace(warning))
                tooltip = $"{tooltip}\n{warning}";

            return new DllInstallTargetItem(item, installed, compat, tooltip);
        }

        private void AddDllInstallTargetRow(DllInstallTargetItem row)
        {
            DllInstallTargets.Add(row);
            _dllTargetItemSubscriptions.Add(
                row.WhenAnyValue(r => r.IsSelected)
                    .Subscribe(_ => this.RaisePropertyChanged(nameof(HasDllTargetPendingChanges))));
        }

        private void PopulateDllCompatibilityLines(
            IReadOnlyDictionary<int, CompatibilityInfo> matrix,
            IReadOnlyList<ModConfiguration> catalog)
        {
            var fullMods = catalog
                .Where(c => c.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) && c.Id > 0)
                .OrderBy(c => c.ModName)
                .ToList();

            var lines = new List<(int Priority, DllCompatibilityLineItem Line)>();
            foreach (var fullMod in fullMods)
            {
                matrix.TryGetValue(fullMod.Id, out var compat);
                var line = CreateCompatLine(fullMod.ModName, compat);
                if (line == null)
                    continue;

                var priority = CompatibilityDisplayHelper.GetSortPriority(line.Status);
                lines.Add((priority, line));
            }

            DllCompatibilityLines.Clear();
            foreach (var entry in lines.OrderBy(x => x.Priority).ThenBy(x => x.Line.TargetName))
                DllCompatibilityLines.Add(entry.Line);

            RefreshDllCompatibilityDisplay();
        }

        private void NotifyDllInspectorProperties()
        {
            this.RaisePropertyChanged(nameof(IsDllPanelVisible));
            this.RaisePropertyChanged(nameof(IsBrowserDetailPanelVisible));
            this.RaisePropertyChanged(nameof(SelectedDllModName));
            this.RaisePropertyChanged(nameof(SelectedDllModPngFileName));
            this.RaisePropertyChanged(nameof(SelectedDllModVersion));
            this.RaisePropertyChanged(nameof(SelectedDllDescription));
            this.RaisePropertyChanged(nameof(SelectedDllInstalledInText));
            this.RaisePropertyChanged(nameof(HasDllInstalledInTargets));
            this.RaisePropertyChanged(nameof(HasDllTargetPendingChanges));
            this.RaisePropertyChanged(nameof(HasDllCompatibilityLines));
            this.RaisePropertyChanged(nameof(IsDllCompatibilityLoading));
            this.RaisePropertyChanged(nameof(ShowDllCompatibilityToggle));
            this.RaisePropertyChanged(nameof(DllCompatibilityToggleLabel));
            this.RaisePropertyChanged(nameof(DllHiddenIncompatibleTargetCount));
            this.RaisePropertyChanged(nameof(HasDllHiddenIncompatibleTargets));
        }

        private async Task ApplyDllTargetChangesAsync()
        {
            if (SelectedDllMod == null)
                return;

            var pending = DllInstallTargets.Where(t => t.HasPendingChange).ToList();
            if (pending.Count == 0)
                return;

            foreach (var row in pending)
            {
                if (row.IsSelected && !row.WasInstalled)
                {
                    if (row.IsInstallBlocked)
                    {
                        await ShowErrorDialogAsync(
                            _localizationService.GetFormatted(
                                "UI.DllManager.InstallBlockedIncompatible",
                                row.DisplayName),
                            _localizationService.Get("UI.DllManager.InstallTitle"));
                        continue;
                    }

                    await InstallDllToMod(row.Target);
                }
                else if (!row.IsSelected && row.WasInstalled)
                    await UninstallDllFromMod(row.Target);
            }

            LoadDllInstallTargets();
            LoadDllMods();
            await RefreshPackInstancesAsync();
        }

        private async Task InstallDllToMod(ModItem targetMod)
        {
            if (SelectedDllMod == null || targetMod == null)
                return;

            try
            {
                var dllConfig = ModItemAdapter.ToConfig(SelectedDllMod);
                var targetConfig = ModItemAdapter.ToConfig(targetMod);
                string platform = DeterminePlatform();

                if (await IsDllInstallBlockedForTargetAsync(targetConfig))
                {
                    await ShowErrorDialogAsync(
                        _localizationService.GetFormatted(
                            "UI.DllManager.InstallBlockedIncompatible",
                            targetMod.Name),
                        _localizationService.Get("UI.DllManager.InstallTitle"));
                    return;
                }

                if (!string.IsNullOrEmpty(targetMod.TargetInstanceId))
                {
                    await App.GetService<ModInstanceInstaller>()
                        .InstallDllToInstanceAsync(dllConfig, targetMod.TargetInstanceId, platform);
                    ToastService.ShowSuccess(
                        _localizationService.GetFormatted("Toast.DllInstalled", SelectedDllMod.Name),
                        _localizationService.GetFormatted("Toast.DllInstalledIn", targetMod.Name));
                    return;
                }

                string? installedPath = await _dllModificationService.InstallDllToModAsync(dllConfig, targetConfig, platform);

                if (!string.IsNullOrEmpty(installedPath))
                {
                    ToastService.ShowSuccess(
                        _localizationService.GetFormatted("Toast.DllInstalled", SelectedDllMod.Name),
                        _localizationService.GetFormatted("Toast.DllInstalledIn", targetMod.Name));
                }
                else
                {
                    await ShowErrorDialogAsync(
                        _localizationService.Get("UI.DllManager.InstallFailed"),
                        _localizationService.Get("UI.DllManager.InstallTitle"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error installing DLL: {ex.Message}");
                await ShowErrorDialogAsync(
                    _localizationService.GetFormatted("UI.DllManager.InstallError", ex.Message),
                    _localizationService.Get("UI.DllManager.InstallTitle"));
            }
        }

        private async Task UninstallDllFromMod(ModItem targetMod)
        {
            if (SelectedDllMod == null || targetMod == null)
                return;

            try
            {
                bool confirm = await ShowConfirmDialogAsync(
                    _localizationService.GetFormatted(
                        "UI.DllManager.UninstallConfirm",
                        SelectedDllMod.Name,
                        targetMod.Name),
                    _localizationService.Get("UI.DllManager.UninstallTitle"));

                if (!confirm)
                    return;

                var dllConfig = ModItemAdapter.ToConfig(SelectedDllMod);
                var targetConfig = ModItemAdapter.ToConfig(targetMod);
                string platform = DeterminePlatform();

                bool success = await _dllModificationService.UninstallDllFromModAsync(dllConfig, targetConfig, platform);

                if (success)
                {
                    if (!string.IsNullOrEmpty(targetMod.TargetInstanceId))
                        RemoveDllRowsForInstance(targetMod.TargetInstanceId, dllConfig.Id);

                    ToastService.ShowInfo(
                        _localizationService.GetFormatted("Toast.DllRemoved", SelectedDllMod.Name),
                        _localizationService.GetFormatted("Toast.DllRemovedFrom", targetMod.Name));
                }
                else
                {
                    await ShowErrorDialogAsync(
                        _localizationService.Get("UI.DllManager.UninstallFailed"),
                        _localizationService.Get("UI.DllManager.UninstallTitle"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error uninstalling DLL: {ex.Message}");
                await ShowErrorDialogAsync(
                    _localizationService.GetFormatted("UI.DllManager.UninstallError", ex.Message),
                    _localizationService.Get("UI.DllManager.UninstallTitle"));
            }
        }

        private async Task<bool> IsDllInstallBlockedForTargetAsync(ModConfiguration targetConfig)
        {
            if (_dllCompatibilityService == null || SelectedDllMod == null || targetConfig.Id <= 0)
                return false;

            var compat = await _dllCompatibilityService.CheckCompatibilityAsync(
                SelectedDllMod.Id,
                targetConfig.Id);

            return compat?.Status == CompatibilityStatus.NotWork;
        }

        private static void RemoveDllRowsForInstance(string instanceId, int dllModId)
        {
            var repo = App.GetService<IModInstanceRepository>();
            foreach (var row in repo.GetDlls(instanceId).Where(d => d.DllModId == dllModId).ToList())
                repo.RemoveDll(row.Id);
        }

        private static ModConfiguration BuildDllTargetConfig(ModInstance instance, ModConfiguration? catalogMod)
        {
            catalogMod ??= new ModConfiguration
            {
                Id = instance.BaseModId,
                ModName = instance.BaseModName,
                ModType = "full"
            };

            return new ModConfiguration
            {
                Id = catalogMod.Id,
                ModName = instance.DisplayName,
                ModType = "full",
                ModVersion = instance.FullModVersion,
                AmongVersion = instance.AmongVersion,
                InstallPath = instance.InstallPath,
                GitHubRepoOrLink = catalogMod.GitHubRepoOrLink,
                EpicGitHubRepoOrLink = catalogMod.EpicGitHubRepoOrLink,
                PngFileName = catalogMod.PngFileName
            };
        }
    }
}
