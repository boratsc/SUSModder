using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SUSModder.Models;
using SUSModder.Services;
using SUSModder.Core.Services.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SUSModder.Views
{
    public partial class RolesWindow : Window
    {
        private readonly RolesService _rolesService;
        private readonly ILocalizationService _localizationService;
        private List<Role> _allRoles = new();
        private List<Role> _filteredRoles = new();
        private readonly int _configId;
        private readonly int _modId; // Zmienione z _modName na _modId
        private readonly string _modName;
        private Role? _selectedRole;
        private bool _overlayVisible;
        private const int OverlayAnimationMs = 180;

        public RolesWindow()
        {
            InitializeComponent();
            _rolesService = new RolesService();
            _localizationService = App.GetService<ILocalizationService>();
            _configId = 0;
            _modId = 0;
            _modName = string.Empty;

            Title = _localizationService.Get("RolesWindow.WindowTitle");
            TitleText.Text = _localizationService.Get("RolesWindow.Header");
            SubtitleText.Text = _localizationService.Get("RolesWindow.LoadingSubtitle");

            KeyDown += OnWindowKeyDown;
        }

        public RolesWindow(int configId, int modId, string modName) // Dodany parametr modId
        {
            InitializeComponent();
            _rolesService = new RolesService();
            _localizationService = App.GetService<ILocalizationService>();
            _configId = configId;
            _modId = modId;
            _modName = modName;

            Title = $"{_localizationService.Get("RolesWindow.WindowTitle")} - {modName}";
            TitleText.Text = _localizationService.Get("RolesWindow.Header");
            SubtitleText.Text = string.Format(_localizationService.Get("RolesWindow.ModSubtitle"), modName);

            Loaded += OnWindowLoaded;
            Closing += OnWindowClosing;
            KeyDown += OnWindowKeyDown;
        }

        // Konstruktor dla kompatybilności wstecznej
        public RolesWindow(int configId, string modName) : this(configId, 0, modName)
        {
        }

        private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
        {
            await LoadRolesAsync();
        }

        private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            // RolesService używa statycznego HttpClient, więc nie wymaga Dispose
        }

        private async Task LoadRolesAsync()
        {
            try
            {
                ShowLoadingState();

                // Pobierz wszystkie role
                var allRoles = await _rolesService.GetRolesAsync(_configId);

                // Filtruj role dla konkretnego moda po Id
                // _modId to ID moda z config.json
                // role.Id to ID moda do którego należy rola (z endpointu)
                _allRoles = allRoles.Where(role => role.Id == _modId).ToList();

                if (_allRoles.Any())
                {
                    _filteredRoles = new List<Role>(_allRoles);
                    ShowRolesState();
                    UpdateRolesList();
                    UpdateSubtitle();
                }
                else
                {
                    ShowNoResultsState();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading roles: {ex.Message}");
                ShowErrorState();
            }
        }

        // Reszta metod pozostaje bez zmian...
        private void ShowLoadingState()
        {
            LoadingPanel.IsVisible = true;
            ErrorPanel.IsVisible = false;
            RolesItemsControl.IsVisible = false;
            NoResultsPanel.IsVisible = false;
        }

        private void ShowErrorState()
        {
            LoadingPanel.IsVisible = false;
            ErrorPanel.IsVisible = true;
            RolesItemsControl.IsVisible = false;
            NoResultsPanel.IsVisible = false;
        }

        private void ShowRolesState()
        {
            LoadingPanel.IsVisible = false;
            ErrorPanel.IsVisible = false;
            RolesItemsControl.IsVisible = true;
            NoResultsPanel.IsVisible = false;
        }

        private void ShowNoResultsState()
        {
            LoadingPanel.IsVisible = false;
            ErrorPanel.IsVisible = false;
            RolesItemsControl.IsVisible = false;
            NoResultsPanel.IsVisible = true;
        }

        private void UpdateRolesList()
        {
            RolesItemsControl.ItemsSource = _filteredRoles;

            if (!_filteredRoles.Any() && _allRoles.Any())
            {
                ShowNoResultsState();
            }
            else if (_filteredRoles.Any())
            {
                ShowRolesState();
            }
        }

        private void UpdateSubtitle()
        {
            var totalCount = _allRoles.Count;
            var filteredCount = _filteredRoles.Count;

            if (totalCount == filteredCount)
            {
                SubtitleText.Text = string.Format(_localizationService.Get("RolesWindow.RoleCountSingle"), _modName, totalCount);
            }
            else
            {
                SubtitleText.Text = string.Format(_localizationService.Get("RolesWindow.RoleCountFiltered"), _modName, filteredCount, totalCount);
            }
        }

        private void ApplyFilters()
        {
            var searchText = SearchBox.Text?.ToLower() ?? string.Empty;
            var selectedCategory = (CategoryFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var selectedType = (TypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();

            var allCategoriesText = _localizationService.Get("RolesWindow.AllCategories");
            var allTypesText = _localizationService.Get("RolesWindow.AllTypes");

            _filteredRoles = _allRoles.Where(role =>
            {
                // Filtr wyszukiwania
                var matchesSearch = string.IsNullOrEmpty(searchText) ||
                                  role.Name.ToLower().Contains(searchText) ||
                                  role.Description.ToLower().Contains(searchText) ||
                                  role.Abilities.Any(a => a.Name.ToLower().Contains(searchText));

                // Filtr kategorii
                var matchesCategory = selectedCategory == allCategoriesText ||
                                    selectedCategory == null ||
                                    role.Category.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase);

                // Filtr typu
                var matchesType = selectedType == allTypesText ||
                                selectedType == null ||
                                role.Type.Equals(selectedType, StringComparison.OrdinalIgnoreCase);

                return matchesSearch && matchesCategory && matchesType;
            }).ToList();

            UpdateRolesList();
            UpdateSubtitle();
        }

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void OnCategoryFilterChanged(object? sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void OnTypeFilterChanged(object? sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private async void OnRetryClick(object? sender, RoutedEventArgs e)
        {
            await LoadRolesAsync();
        }

        private void OnRoleCardClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is Role role)
            {
                ShowRoleDetails(role);
            }
        }

        private void ShowRoleDetails(Role role)
        {
            _selectedRole = role;

            DetailSheet.DataContext = role;
            DetailAbilitiesPanel.IsVisible = role.Abilities.Any();

            DetailOverlay.IsVisible = true;
            DetailSheet.IsVisible = true;
            DetailOverlay.IsHitTestVisible = true;
            DetailSheet.IsHitTestVisible = true;

            DetailOverlay.Opacity = 0;
            DetailSheet.Opacity = 0;
            DetailSheet.Margin = new Thickness(0, 0, -12, 0);

            _overlayVisible = true;

            Dispatcher.UIThread.Post(() =>
            {
                if (!_overlayVisible)
                {
                    return;
                }

                DetailOverlay.Opacity = 1;
                DetailSheet.Margin = new Thickness(0);
                DetailSheet.Opacity = 1;
                DetailSheet.Focus();
            }, DispatcherPriority.Background);
        }

        private async Task HideRoleDetailsAsync()
        {
            if (!_overlayVisible)
            {
                return;
            }

            _overlayVisible = false;

            DetailOverlay.IsHitTestVisible = false;
            DetailSheet.IsHitTestVisible = false;

            DetailOverlay.Opacity = 0;
            DetailSheet.Opacity = 0;
            DetailSheet.Margin = new Thickness(0, 0, -12, 0);

            await Task.Delay(OverlayAnimationMs);

            DetailOverlay.IsVisible = false;
            DetailSheet.IsVisible = false;
            _selectedRole = null;

            SearchBox.Focus();
        }

        private async void OnOverlayBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender == DetailOverlay)
            {
                await HideRoleDetailsAsync();
                e.Handled = true;
            }
        }

        private async void OnCloseOverlayClick(object? sender, RoutedEventArgs e)
        {
            await HideRoleDetailsAsync();
        }

        private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _overlayVisible)
            {
                await HideRoleDetailsAsync();
                e.Handled = true;
            }
        }
    }
}
