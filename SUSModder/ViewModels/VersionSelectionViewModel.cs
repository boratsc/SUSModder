using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// ViewModel dla dialogu wyboru wersji moda
    /// </summary>
    public class VersionSelectionViewModel : ViewModelBase
    {
        private readonly ModVersionService _versionService;
        private readonly ModConfiguration _modConfig;
        private readonly ILocalizationService _localizationService;
        private ObservableCollection<ModVersionHistory> _versions = new();
        private ModVersionHistory? _selectedVersion;
        private bool _isLoading;
        private string _errorMessage = string.Empty;
        private bool _hasError;

        public ObservableCollection<ModVersionHistory> Versions
        {
            get => _versions;
            set => this.RaiseAndSetIfChanged(ref _versions, value);
        }

        public ModVersionHistory? SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedVersion, value);
                this.RaisePropertyChanged(nameof(SelectedVersionDisplayText));
            }
        }

        public string SelectedVersionDisplayText => SelectedVersion?.DisplayText ?? string.Empty;

        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => this.RaiseAndSetIfChanged(ref _hasError, value);
        }

        public string ModName => _modConfig.ModName;
        public string CurrentVersion => _modConfig.ModVersion ?? _localizationService.Get("VersionSelection.UnknownVersion");

        public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public event EventHandler<ModVersionHistory?>? VersionSelected;
        public event EventHandler? Cancelled;

        public VersionSelectionViewModel(ModConfiguration modConfig, IConfiguration configuration, ILocalizationService localizationService)
        {
            _modConfig = modConfig;
            _localizationService = localizationService;
            var diagnostics = new UIDiagnosticsOutput(msg => System.Diagnostics.Debug.WriteLine(msg));
            _versionService = new ModVersionService(diagnostics);

            ConfirmCommand = ReactiveCommand.Create(
                () =>
                {
                    VersionSelected?.Invoke(this, SelectedVersion);
                },
                this.WhenAnyValue(x => x.SelectedVersion).Select(selected => selected != null)
            );

            CancelCommand = ReactiveCommand.Create(() =>
            {
                Cancelled?.Invoke(this, EventArgs.Empty);
            });

            // Automatyczne ładowanie wersji
            _ = LoadVersionsAsync();
        }

        private async Task LoadVersionsAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;

            try
            {
                var versions = await _versionService.GetVersionHistoryAsync(_modConfig.Id);

                if (versions.Count == 0)
                {
                    HasError = true;
                    ErrorMessage = _localizationService.Get("VersionSelection.NoVersionsAvailable");
                }
                else
                {
                    Versions = new ObservableCollection<ModVersionHistory>(versions);

                    // Automatycznie wybierz najnowszą wersję (pierwsza na liście)
                    SelectedVersion = versions.FirstOrDefault();
                }
            }
            catch (TimeoutException)
            {
                HasError = true;
                ErrorMessage = _localizationService.Get("VersionSelection.TimeoutError");
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = _localizationService.GetFormatted("VersionSelection.FetchError", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void Dispose()
        {
        }

        public void CancelSelection()
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }
}
