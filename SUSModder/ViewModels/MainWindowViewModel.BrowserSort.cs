using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ReactiveUI;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// K2: sortowanie katalogu modów — wybór w nagłówku przeglądarki, trwały zapis
    /// w ustawieniach użytkownika (SQLite) i przesorowanie listy z zachowaniem filtrów.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private ModSortOption _modSortOption = ModSortOption.PopularityDesc;
        private ModSortOptionItem? _selectedModSortOption;

        public ObservableCollection<ModSortOptionItem> ModSortOptions { get; } = new();

        private ReactiveCommand<ModSortOptionItem, Unit>? _selectModSortOptionCommand;

        /// <summary>Wybór opcji z menu przycisku sortowania (ten sam setter co dawniej: zapis + przesorowanie).</summary>
        public ReactiveCommand<ModSortOptionItem, Unit> SelectModSortOptionCommand =>
            _selectModSortOptionCommand ??= ReactiveCommand.Create<ModSortOptionItem>(item => SelectedModSortOption = item);

        /// <summary>Tooltip i nazwa automatyczna przycisku sortowania — aktywna opcja, odświeżane przy zmianie języka.</summary>
        public string SortButtonLabel =>
            _localizationService.GetFormatted("UI.Browser.Sort.ButtonLabel", SelectedModSortOption?.Label ?? string.Empty);

        public ModSortOptionItem? SelectedModSortOption
        {
            get => _selectedModSortOption;
            set
            {
                if (value == null)
                    return;
                this.RaiseAndSetIfChanged(ref _selectedModSortOption, value);
                UpdateSortOptionFlags();
                this.RaisePropertyChanged(nameof(SortButtonLabel));
                if (_modSortOption == value.Option)
                    return;
                _modSortOption = value.Option;
                try
                {
                    _userSettingsService.UpdateUserSetting(s => s.ModListSort = value.Option.ToString());
                }
                catch (Exception ex)
                {
                    // Zapis ustawienia nie może wywrócić UI — sortowanie działa dalej w pamięci.
                    System.Diagnostics.Debug.WriteLine($"[BrowserSort] Nie udało się zapisać sortowania: {ex.Message}");
                }
                // Zachowaj zaznaczony mod i otwarty panel — ten sam guarded path co odświeżenie po operacjach.
                ResortCatalogKeepSelection(SelectedMod);
            }
        }

        private void InitializeBrowserSort()
        {
            _modSortOption = CatalogBrowserProjection.ParseSortOption(
                _userSettingsService.LoadUserSettings().ModListSort);
            foreach (var option in Enum.GetValues<ModSortOption>())
                ModSortOptions.Add(new ModSortOptionItem(option, _localizationService.Get(SortLabelKey(option))));
            _selectedModSortOption = ModSortOptions.First(o => o.Option == _modSortOption);
            UpdateSortOptionFlags();
            this.RaisePropertyChanged(nameof(SelectedModSortOption));
            this.RaisePropertyChanged(nameof(SortButtonLabel));
        }

        /// <summary>Znacznik aktywnej opcji w menu (MenuItem.IsChecked wiąże się z IsSelected).</summary>
        private void UpdateSortOptionFlags()
        {
            foreach (var option in ModSortOptions)
                option.IsSelected = option == _selectedModSortOption;
        }

        /// <summary>Wywoływane z handlera zmiany języka: świeże etykiety opcji sortowania.</summary>
        private void RefreshBrowserSortLocalization()
        {
            foreach (var option in ModSortOptions)
                option.Label = _localizationService.Get(SortLabelKey(option.Option));
            this.RaisePropertyChanged(nameof(SortButtonLabel));
        }

        private static string SortLabelKey(ModSortOption option) => $"UI.Browser.Sort.{option}";
    }
}