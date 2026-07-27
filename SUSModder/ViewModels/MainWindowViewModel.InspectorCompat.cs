using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ReactiveUI;
using SUSModder.Core.Models;
using SUSModder.Services;

namespace SUSModder.ViewModels
{
    public partial class MainWindowViewModel
    {
        private const int CompatPreviewCount = 4;

        private bool _isCatalogCompatibleDllExpanded;
        private bool _isCatalogCompatibleDllSectionExpanded;
        private bool _isDllCompatibilityExpanded;
        private readonly ObservableCollection<DllCompatibilityLineItem> _catalogCompatibleDllDisplay = new();
        private readonly ObservableCollection<DllCompatibilityLineItem> _dllCompatibilityDisplay = new();

        public ObservableCollection<DllCompatibilityLineItem> CatalogCompatibleDllDisplayLines => _catalogCompatibleDllDisplay;
        public ObservableCollection<DllCompatibilityLineItem> DllCompatibilityDisplayLines => _dllCompatibilityDisplay;

        public bool IsCatalogCompatibleDllExpanded
        {
            get => _isCatalogCompatibleDllExpanded;
            private set => this.RaiseAndSetIfChanged(ref _isCatalogCompatibleDllExpanded, value);
        }

        public bool IsCatalogCompatibleDllSectionExpanded
        {
            get => _isCatalogCompatibleDllSectionExpanded;
            private set => this.RaiseAndSetIfChanged(ref _isCatalogCompatibleDllSectionExpanded, value);
        }

        public bool IsDllCompatibilityExpanded
        {
            get => _isDllCompatibilityExpanded;
            private set => this.RaiseAndSetIfChanged(ref _isDllCompatibilityExpanded, value);
        }

        public bool ShowCatalogCompatibleDllToggle => CatalogCompatibleDllLines.Count > CompatPreviewCount;

        public bool ShowDllCompatibilityToggle => DllCompatibilityLines.Count > CompatPreviewCount;

        public string CatalogCompatibleDllToggleLabel =>
            IsCatalogCompatibleDllExpanded
                ? _localizationService.Get("UI.Inspector.ShowLess")
                : _localizationService.GetFormatted(
                    "UI.Inspector.ShowMore",
                    CatalogCompatibleDllLines.Count - CompatPreviewCount);

        public string DllCompatibilityToggleLabel =>
            IsDllCompatibilityExpanded
                ? _localizationService.Get("UI.Inspector.ShowLess")
                : _localizationService.GetFormatted(
                    "UI.Inspector.ShowMore",
                    DllCompatibilityLines.Count - CompatPreviewCount);

        public ReactiveCommand<Unit, Unit> ToggleCatalogCompatibleDllExpandedCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ToggleCatalogCompatibleDllSectionCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ToggleDllCompatibilityExpandedCommand { get; private set; } = null!;

        private void InitializeInspectorCompatExpand()
        {
            ToggleCatalogCompatibleDllExpandedCommand = ReactiveCommand.Create(ToggleCatalogCompatibleDllExpanded);
            ToggleCatalogCompatibleDllSectionCommand = ReactiveCommand.Create(ToggleCatalogCompatibleDllSection);
            ToggleDllCompatibilityExpandedCommand = ReactiveCommand.Create(ToggleDllCompatibilityExpanded);
        }

        private void ToggleCatalogCompatibleDllSection()
        {
            IsCatalogCompatibleDllSectionExpanded = !IsCatalogCompatibleDllSectionExpanded;
        }

        private void ToggleCatalogCompatibleDllExpanded()
        {
            IsCatalogCompatibleDllExpanded = !IsCatalogCompatibleDllExpanded;
            RefreshCatalogCompatibleDllDisplay();
            this.RaisePropertyChanged(nameof(IsCatalogCompatibleDllExpanded));
            this.RaisePropertyChanged(nameof(CatalogCompatibleDllToggleLabel));
        }

        private void ToggleDllCompatibilityExpanded()
        {
            IsDllCompatibilityExpanded = !IsDllCompatibilityExpanded;
            RefreshDllCompatibilityDisplay();
            this.RaisePropertyChanged(nameof(IsDllCompatibilityExpanded));
            this.RaisePropertyChanged(nameof(DllCompatibilityToggleLabel));
        }

        private void RefreshCatalogCompatibleDllDisplay()
        {
            _catalogCompatibleDllDisplay.Clear();
            var visible = IsCatalogCompatibleDllExpanded
                ? CatalogCompatibleDllLines
                : CatalogCompatibleDllLines.Take(CompatPreviewCount);

            foreach (var line in visible)
                _catalogCompatibleDllDisplay.Add(line);

            this.RaisePropertyChanged(nameof(ShowCatalogCompatibleDllToggle));
            this.RaisePropertyChanged(nameof(CatalogCompatibleDllToggleLabel));
        }

        private void RefreshDllCompatibilityDisplay()
        {
            _dllCompatibilityDisplay.Clear();
            var visible = IsDllCompatibilityExpanded
                ? DllCompatibilityLines
                : DllCompatibilityLines.Take(CompatPreviewCount);

            foreach (var line in visible)
                _dllCompatibilityDisplay.Add(line);

            this.RaisePropertyChanged(nameof(ShowDllCompatibilityToggle));
            this.RaisePropertyChanged(nameof(DllCompatibilityToggleLabel));
        }

        private DllCompatibilityLineItem? CreateCompatLine(string targetName, CompatibilityInfo? compat, int? dllModId = null)
        {
            if (!CompatibilityDisplayHelper.IsVisible(compat))
                return null;

            var status = compat?.Status ?? CompatibilityStatus.NotTested;
            var emoji = CompatibilityDisplayHelper.GetEmoji(compat);
            var label = CompatibilityDisplayHelper.GetStatusLabel(compat, _localizationService);
            return new DllCompatibilityLineItem(targetName, emoji, label, status, dllModId);
        }
    }
}
