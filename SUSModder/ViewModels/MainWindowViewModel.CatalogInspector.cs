using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Services;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.ViewModels
{
    public partial class MainWindowViewModel
    {
        private readonly ObservableCollection<DllCompatibilityLineItem> _catalogCompatibleDllLines = new();
        private CompatibilityService? _catalogCompatibilityService;
        private CancellationTokenSource? _catalogCompatLoadCts;

        public ObservableCollection<DllCompatibilityLineItem> CatalogCompatibleDllLines => _catalogCompatibleDllLines;

        public bool HasCatalogCompatibleDllLines => CatalogCompatibleDllLines.Count > 0;

        public bool IsCatalogCompatibleDllLoading { get; private set; }

        public bool ShowCatalogInspectorCompatibleDlls =>
            SelectedMod != null && SelectedMod.IsFullMod && !SelectedMod.IsVanilla;

        public ReactiveCommand<DllCompatibilityLineItem, Unit> OpenDllAddonFromCatalogCommand { get; private set; } = null!;

        private void InitializeCatalogInspector()
        {
            OpenDllAddonFromCatalogCommand = ReactiveCommand.Create<DllCompatibilityLineItem>(OpenDllAddonFromCatalog);
            if (_configuration == null)
                return;

            try
            {
                _catalogCompatibilityService = new CompatibilityService(
                    _configuration,
                    _diagnosticsOutput ?? new UIDiagnosticsOutput(_ => { }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatalogInspector] CompatibilityService: {ex.Message}");
            }
        }

        private void OnCatalogModInspectorChanged()
        {
            ResetInspectorSections();
            if (SelectedMod != null && !SelectedMod.IsVanilla)
                EnsureInspectorLobbyEmbedViewModel();
            this.RaisePropertyChanged(nameof(ShowCatalogInspectorCompatibleDlls));
            _ = LoadCatalogCompatibleDllsAsync();
        }

        private void ClearCatalogCompatibleDlls()
        {
            _catalogCompatLoadCts?.Cancel();
            CatalogCompatibleDllLines.Clear();
            _catalogCompatibleDllDisplay.Clear();
            IsCatalogCompatibleDllExpanded = false;
            IsCatalogCompatibleDllLoading = false;
            NotifyCatalogInspectorProperties();
        }

        private async Task LoadCatalogCompatibleDllsAsync()
        {
            var mod = SelectedMod;
            if (mod == null || !mod.IsFullMod || mod.IsVanilla || _catalogCompatibilityService == null)
            {
                ClearCatalogCompatibleDlls();
                return;
            }

            _catalogCompatLoadCts?.Cancel();
            _catalogCompatLoadCts = new CancellationTokenSource();
            var token = _catalogCompatLoadCts.Token;

            IsCatalogCompatibleDllExpanded = false;
            IsCatalogCompatibleDllLoading = true;
            NotifyCatalogInspectorProperties();

            try
            {
                var matrix = await _catalogCompatibilityService
                    .GetCompatibilityMatrixForFullModAsync(mod.Id, token);

                if (token.IsCancellationRequested)
                    return;

                var dllMods = _dllModificationService.GetDllMods();
                var lines = new List<(int Priority, DllCompatibilityLineItem Line)>();

                foreach (var dll in dllMods)
                {
                    if (dll.Id <= 0)
                        continue;

                    matrix.TryGetValue(dll.Id, out var compat);

                    var line = CreateCompatLine(dll.ModName, compat, dll.Id);
                    if (line == null)
                        continue;

                    lines.Add((CompatibilityDisplayHelper.GetSortPriority(line.Status), line));
                }

                CatalogCompatibleDllLines.Clear();
                foreach (var entry in lines.OrderBy(x => x.Priority).ThenBy(x => x.Line.TargetName))
                    CatalogCompatibleDllLines.Add(entry.Line);

                RefreshCatalogCompatibleDllDisplay();
            }
            catch (OperationCanceledException)
            {
                // Przełączenie moda — ignoruj
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatalogInspector] Compatible DLLs: {ex.Message}");
                CatalogCompatibleDllLines.Clear();
            }
            finally
            {
                IsCatalogCompatibleDllLoading = false;
                NotifyCatalogInspectorProperties();
            }
        }

        private void OpenDllAddonFromCatalog(DllCompatibilityLineItem? line)
        {
            if (line?.DllModId is not int dllId)
                return;

            ActiveBrowserTab = ModBrowserTab.DllAddons;
            var dll = DllMods.FirstOrDefault(d => d.Id == dllId);
            if (dll != null)
                SelectDllMod(dll);
        }

        private void NotifyCatalogInspectorProperties()
        {
            this.RaisePropertyChanged(nameof(HasCatalogCompatibleDllLines));
            this.RaisePropertyChanged(nameof(IsCatalogCompatibleDllLoading));
            this.RaisePropertyChanged(nameof(ShowCatalogInspectorCompatibleDlls));
            this.RaisePropertyChanged(nameof(ShowCatalogCompatibleDllToggle));
            this.RaisePropertyChanged(nameof(CatalogCompatibleDllToggleLabel));
        }
    }
}
