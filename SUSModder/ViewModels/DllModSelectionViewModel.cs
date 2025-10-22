using System;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using SUSModder.Core.Services;
using SUSModder.Core.Configuration;
using System.Reactive;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using SUSModder.Core.Models;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;

namespace SUSModder.ViewModels
{
    public class DllModSelectionViewModel : ViewModelBase
    {
        private readonly DllModificationService _dllModificationService;
        private readonly CompatibilityService? _compatibilityService;
        private ModConfiguration _targetMod = null!;
        private ObservableCollection<ModConfiguration> _dllMods = new();
        private Dictionary<int, CompatibilityInfo?> _compatibilityCache = new();
        private List<int> _initiallyInstalledDllIds = new();

        // Dodaj event do powiadomienia o potrzebie zamknięcia widoku
        public event EventHandler? CloseRequested;

        // Nowe pola dla stanu po instalacji
        private bool _isInstallationComplete;
        private string _installationSummary = "";
        private string _actionButtonText = "Zatwierdź zmiany";

        public ObservableCollection<ModConfiguration> DllMods
        {
            get => _dllMods;
            set => this.RaiseAndSetIfChanged(ref _dllMods, value);
        }

        // Nowe właściwości
        public bool IsInstallationComplete
        {
            get => _isInstallationComplete;
            set => this.RaiseAndSetIfChanged(ref _isInstallationComplete, value);
        }

        public string InstallationSummary
        {
            get => _installationSummary;
            set => this.RaiseAndSetIfChanged(ref _installationSummary, value);
        }

        public string ActionButtonText
        {
            get => _actionButtonText;
            set => this.RaiseAndSetIfChanged(ref _actionButtonText, value);
        }

        public ReactiveCommand<Unit, Unit> OkCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyChangesCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        public DllModSelectionViewModel(
            DllModificationService dllModificationService, 
            ModConfiguration targetMod, 
            string platform = "steam",
            IConfiguration? configuration = null,
            IDiagnosticsOutput? diagnostics = null)
        {
            _dllModificationService = dllModificationService;
            _targetMod = targetMod;
            Platform = platform;
            _isInstallationComplete = false;

            // Inicjalizacja CompatibilityService jeśli konfiguracja dostępna
            if (configuration != null && diagnostics != null)
            {
                try
                {
                    _compatibilityService = new CompatibilityService(configuration, diagnostics);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Nie udało się zainicjalizować CompatibilityService: {ex.Message}");
                    _compatibilityService = null;
                }
            }

            System.Diagnostics.Debug.WriteLine($"🔍 DllModSelectionViewModel - Mod docelowy: {targetMod?.ModName}, Platforma: {platform}");

            // Inicjalizacja asynchroniczna
            _ = InitializeAsync();

            ApplyChangesCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                await ApplyChangesAsync();
            });

            OkCommand = ReactiveCommand.Create(() =>
            {
                IsInstallationComplete = false;
            });

            CloseCommand = ReactiveCommand.Create(() =>
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            });
        }

        private async Task InitializeAsync()
        {
            try
            {
                // 1. Wczytaj listę zainstalowanych DLL IDs
                _initiallyInstalledDllIds = await _dllModificationService.GetInstalledDllIdsAsync(_targetMod);
                System.Diagnostics.Debug.WriteLine($"✅ Zainstalowane DLL IDs: {string.Join(", ", _initiallyInstalledDllIds)}");

                // 2. Załaduj kompatybilność w tle
                await LoadCompatibilityDataAsync();

                // 3. Załaduj i posortuj mody DLL
                await LoadAndSortDllModsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Błąd podczas inicjalizacji: {ex.Message}");
            }
        }

        private Task LoadAndSortDllModsAsync()
        {
            var allDllMods = _dllModificationService.GetDllMods();
            
            // Filtruj i sortuj
            var filteredAndSorted = new List<ModConfiguration>();
            
            foreach (var mod in allDllMods)
            {
                if (mod.Id == 0)
                    continue; // Pomiń mody bez ID

                // Pobierz kompatybilność
                var compat = _compatibilityCache.ContainsKey(mod.Id) 
                    ? _compatibilityCache[mod.Id] 
                    : null;

                // Filtruj niekompatybilne (NW - Not Work)
                if (compat?.Status == CompatibilityStatus.NotWork)
                {
                    System.Diagnostics.Debug.WriteLine($"⛔ Pomijam niekompatybilny mod: {mod.ModName}");
                    continue;
                }

                // Ustaw stan zainstalowania i zaznaczenia
                mod.IsInstalled = _initiallyInstalledDllIds.Contains(mod.Id);
                mod.IsSelected = mod.IsInstalled; // Automatycznie zaznacz zainstalowane

                // Ustaw informacje o kompatybilności
                mod.CompatibilityEmoji = compat?.Emoji ?? "❓";
                mod.CompatibilityDescription = compat?.Description ?? "Kompatybilność nieznana";
                mod.CompatibilityWarning = (compat != null && CompatibilityService.ShouldShowWarning(compat)) 
                    ? compat.Warning ?? "Ten mod może nie działać poprawnie." 
                    : null;

                // Przypisz priorytet sortowania
                int priority = compat?.Status switch
                {
                    CompatibilityStatus.Favorite => 1,
                    CompatibilityStatus.Works => 2,
                    CompatibilityStatus.NotTested => 3,
                    _ => 4
                };

                // Dodaj do listy z priorytetem
                filteredAndSorted.Add(mod);
            }

            // Sortuj według priorytetu, a następnie alfabetycznie
            var sorted = filteredAndSorted
                .OrderBy(m => {
                    var compat = _compatibilityCache.ContainsKey(m.Id) 
                        ? _compatibilityCache[m.Id] 
                        : null;
                    return compat?.Status switch
                    {
                        CompatibilityStatus.Favorite => 1,
                        CompatibilityStatus.Works => 2,
                        CompatibilityStatus.NotTested => 3,
                        _ => 4
                    };
                })
                .ThenBy(m => m.ModName)
                .ToList();

            // Obserwuj zmiany w zaznaczeniu
            foreach (var mod in sorted)
            {
                mod.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ModConfiguration.IsSelected))
                    {
                        UpdateActionButtonText();
                    }
                };
            }

            DllMods = new ObservableCollection<ModConfiguration>(sorted);
            UpdateActionButtonText();
            
            System.Diagnostics.Debug.WriteLine($"✅ Załadowano {DllMods.Count} modów DLL (przefiltrowane i posortowane)");
            
            return Task.CompletedTask;
        }

        private void UpdateActionButtonText()
        {
            var toInstall = DllMods.Where(m => m.IsSelected && !m.IsInstalled).ToList();
            var toUninstall = DllMods.Where(m => !m.IsSelected && m.IsInstalled).ToList();

            if (toInstall.Any() && toUninstall.Any())
                ActionButtonText = $"Zainstaluj ({toInstall.Count}) i usuń ({toUninstall.Count})";
            else if (toInstall.Any())
                ActionButtonText = $"Zainstaluj ({toInstall.Count})";
            else if (toUninstall.Any())
                ActionButtonText = $"Usuń ({toUninstall.Count})";
            else
                ActionButtonText = "Brak zmian";
        }

        private async Task ApplyChangesAsync()
        {
            var toInstall = DllMods.Where(m => m.IsSelected && !m.IsInstalled).ToList();
            var toUninstall = DllMods.Where(m => !m.IsSelected && m.IsInstalled).ToList();

            if (!toInstall.Any() && !toUninstall.Any())
            {
                System.Diagnostics.Debug.WriteLine("⚠️ Brak zmian do zastosowania");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"🔧 Zmiany: Instalacja={toInstall.Count}, Usuwanie={toUninstall.Count}");

            var summaryLines = new List<string>();
            bool allSuccessful = true;

            // Instalacja nowych
            foreach (var dllMod in toInstall)
            {
                System.Diagnostics.Debug.WriteLine($"⬇️ Instalowanie: {dllMod.ModName}...");
                string? installedPath = await _dllModificationService.InstallDllToModAsync(dllMod, _targetMod, Platform);
                
                if (!string.IsNullOrEmpty(installedPath) && File.Exists(installedPath))
                {
                    summaryLines.Add($"✅ Zainstalowano: {dllMod.ModName}");
                    dllMod.IsInstalled = true;
                }
                else
                {
                    summaryLines.Add($"❌ Błąd instalacji: {dllMod.ModName}");
                    allSuccessful = false;
                }
            }

            // Usuwanie
            foreach (var dllMod in toUninstall)
            {
                System.Diagnostics.Debug.WriteLine($"�️ Usuwanie: {dllMod.ModName}...");
                bool success = await _dllModificationService.UninstallDllFromModAsync(dllMod, _targetMod, Platform);
                
                if (success)
                {
                    summaryLines.Add($"✅ Usunięto: {dllMod.ModName}");
                    dllMod.IsInstalled = false;
                }
                else
                {
                    summaryLines.Add($"❌ Błąd usuwania: {dllMod.ModName}");
                    allSuccessful = false;
                }
            }

            // Podsumowanie
            string statusEmoji = allSuccessful ? "✅" : "⚠️";
            InstallationSummary = $"{statusEmoji} Operacje zakończone\n\n" + string.Join("\n", summaryLines);
            IsInstallationComplete = true;

            System.Diagnostics.Debug.WriteLine($"🏁 Zakończono operacje. Sukces: {allSuccessful}");
        }

        // Dodaj właściwość Platform
        public string Platform { get; }

        /// <summary>
        /// Pobiera informacje o kompatybilności dla danego moda DLL
        /// </summary>
        public async Task<CompatibilityInfo?> GetCompatibilityAsync(int dllModId)
        {
            if (_compatibilityService == null || _targetMod?.Id == null)
                return null;

            if (_compatibilityCache.TryGetValue(dllModId, out var cached))
                return cached;

            var compatibility = await _compatibilityService.CheckCompatibilityAsync(dllModId, _targetMod.Id);
            _compatibilityCache[dllModId] = compatibility;
            return compatibility;
        }

        /// <summary>
        /// Zwraca emoji statusu kompatybilności dla danego moda DLL
        /// </summary>
        public string GetCompatibilityEmoji(ModConfiguration dllMod)
        {
            if (dllMod?.Id == null || !_compatibilityCache.TryGetValue(dllMod.Id, out var compat))
                return "❓";
            return compat?.Emoji ?? "❓";
        }

        /// <summary>
        /// Zwraca opis kompatybilności dla danego moda DLL
        /// </summary>
        public string GetCompatibilityDescription(ModConfiguration dllMod)
        {
            if (dllMod?.Id == null || !_compatibilityCache.TryGetValue(dllMod.Id, out var compat))
                return "Kompatybilność nieznana";
            return compat?.Description ?? "Kompatybilność nieznana";
        }

        /// <summary>
        /// Zwraca ostrzeżenie o kompatybilności jeśli potrzebne
        /// </summary>
        public string? GetCompatibilityWarning(ModConfiguration dllMod)
        {
            if (dllMod?.Id == null || !_compatibilityCache.TryGetValue(dllMod.Id, out var compat))
                return null;
            if (CompatibilityService.ShouldShowWarning(compat))
                return compat?.Warning ?? "Ten mod może nie działać poprawnie.";
            return null;
        }

        /// <summary>
        /// Ładuje kompatybilność dla wszystkich modów DLL w tle
        /// </summary>
        public async Task LoadCompatibilityDataAsync()
        {
            if (_compatibilityService == null || _targetMod?.Id == null)
            {
                System.Diagnostics.Debug.WriteLine($"❌ LoadCompatibilityDataAsync: Early return - Service: {_compatibilityService != null}, TargetMod: {_targetMod != null}, Id: {_targetMod?.Id}");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"🔍 Ładowanie macierzy kompatybilności dla moda FULL ID={_targetMod.Id}");
                var matrix = await _compatibilityService.GetCompatibilityMatrixForFullModAsync(_targetMod.Id);
                System.Diagnostics.Debug.WriteLine($"✅ Załadowano dane kompatybilności dla {matrix.Count} modów DLL");
                
                foreach (var kvp in matrix)
                    _compatibilityCache[kvp.Key] = kvp.Value;
                    
                this.RaisePropertyChanged(nameof(DllMods));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Błąd podczas ładowania kompatybilności: {ex.Message}");
            }
        }
    }
}
