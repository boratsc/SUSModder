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
        private ObservableCollection<ModConfiguration> _selectedDllMods = new();
        private Dictionary<int, CompatibilityInfo?> _compatibilityCache = new();

        // Dodaj event do powiadomienia o potrzebie zamknięcia widoku
        public event EventHandler? CloseRequested;

        // Nowe pola dla stanu po instalacji
        private bool _isInstallationComplete;
        private string _installationSummary;

        public ObservableCollection<ModConfiguration> DllMods
        {
            get => _dllMods;
            set => this.RaiseAndSetIfChanged(ref _dllMods, value);
        }

        public ObservableCollection<ModConfiguration> SelectedDllMods
        {
            get => _selectedDllMods;
            set => this.RaiseAndSetIfChanged(ref _selectedDllMods, value);
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

        public ReactiveCommand<Unit, Unit> OkCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallSelectedDllsCommand { get; }
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
            _installationSummary = "";

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

            // Dodaj debugowanie w konstruktorze
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG - KONSTRUKTOR DllModSelectionViewModel");
            System.Diagnostics.Debug.WriteLine($"🔍 Platforma: {platform}");
            System.Diagnostics.Debug.WriteLine($"🔍 Mod docelowy: {targetMod?.ModName ?? "Nieznany"}");
            System.Diagnostics.Debug.WriteLine($"🔍 Przekazana ścieżka: {targetMod?.InstallPath ?? "BRAK ŚCIEŻKI"}");

            // Próba naprawy pustej ścieżki dla Epic Games
            if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(targetMod?.InstallPath))
            {
                // Próba znalezienia ścieżki instalacji dla Epic Games
                string? epicPath = TryFindEpicInstallPath(targetMod?.ModName);
                
                if (!string.IsNullOrEmpty(epicPath))
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Automatycznie znaleziono ścieżkę dla Epic: {epicPath}");
                    _targetMod = new ModConfiguration
                    {
                        // Użyj operatora koalescencji null, aby uniknąć przypisania null
                        ModName = targetMod?.ModName ?? string.Empty,
                        Description = targetMod?.Description ?? string.Empty,
                        ModVersion = targetMod?.ModVersion ?? string.Empty,
                        InstallPath = epicPath,
                        // Inne właściwości...
                    };
                }
            }

            LoadDllMods();

            // Ładuj dane kompatybilności w tle (nie blokuj UI)
            System.Diagnostics.Debug.WriteLine($"🔍 Przygotowanie do ładowania kompatybilności - Service: {(_compatibilityService != null ? "OK" : "NULL")}, TargetMod.Id: {_targetMod?.Id}");
            _ = Task.Run(async () =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("🔍 Task.Run: Rozpoczynam ładowanie kompatybilności...");
                    await LoadCompatibilityDataAsync();
                    System.Diagnostics.Debug.WriteLine("🔍 Task.Run: Zakończono ładowanie kompatybilności");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Task.Run: Błąd podczas ładowania kompatybilności: {ex.Message}");
                }
            });

            InstallSelectedDllsCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                await InstallSelectedDllsAsync(Platform); // Użyj platformy z parametru
            });

            // Nowa komenda OK - przywraca widok wyboru modyfikacji
            OkCommand = ReactiveCommand.Create(() =>
            {
                IsInstallationComplete = false;
            });

            CloseCommand = ReactiveCommand.Create(() =>
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            });
        }
        private void LoadDllMods()
        {
            var mods = _dllModificationService.GetDllMods();
            foreach (var mod in mods)
            {
                mod.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ModConfiguration.IsSelected))
                    {
                        if (mod.IsSelected && !SelectedDllMods.Contains(mod))
                            SelectedDllMods.Add(mod);
                        else if (!mod.IsSelected && SelectedDllMods.Contains(mod))
                            SelectedDllMods.Remove(mod);

                        this.RaisePropertyChanged(nameof(SelectedDllMods));
                    }
                };
            }
            DllMods = new ObservableCollection<ModConfiguration>(mods);
        }

        // Metoda sprawdzająca czy mod jest zaznaczony
        public bool IsModSelected(ModConfiguration mod)
        {
            return SelectedDllMods.Contains(mod);
        }

        // Metoda do przełączania zaznaczenia modu
        public void ToggleModSelection(ModConfiguration mod)
        {
            if (SelectedDllMods.Contains(mod))
                SelectedDllMods.Remove(mod);
            else
                SelectedDllMods.Add(mod);

            this.RaisePropertyChanged(nameof(SelectedDllMods));
        }

        public async Task InstallSelectedDllsAsync(string platform)
        {
            SelectedDllMods.Clear();
            foreach (var mod in DllMods)
            {
                if (mod.IsSelected)
                    SelectedDllMods.Add(mod);
            }

            if (SelectedDllMods.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ Brak wybranych modów DLL do instalacji");
                return;
            }

            // Szczegółowe debugowanie
            System.Diagnostics.Debug.WriteLine("🔍 DEBUG - INSTALACJA DLL");
            System.Diagnostics.Debug.WriteLine($"🔍 Platforma: {platform}");
            System.Diagnostics.Debug.WriteLine($"🔍 Liczba wybranych modów DLL: {SelectedDllMods.Count}");
            System.Diagnostics.Debug.WriteLine($"🔍 Mod docelowy: {_targetMod?.ModName ?? "Nieznany"}");
            System.Diagnostics.Debug.WriteLine($"🔍 Oryginalna ścieżka instalacji: {_targetMod?.InstallPath ?? "Brak ścieżki"}");
            System.Diagnostics.Debug.WriteLine($"🔍 Czy ścieżka istnieje? {(_targetMod?.InstallPath != null && Directory.Exists(_targetMod.InstallPath) ? "TAK" : "NIE")}");
            
            // Sprawdź czy docelowy katalog istnieje
            if (_targetMod?.InstallPath == null || string.IsNullOrEmpty(_targetMod.InstallPath))
            {
                System.Diagnostics.Debug.WriteLine("❌ BŁĄD: Brak ścieżki instalacji dla moda docelowego!");
                InstallationSummary = "Błąd: Nie można zainstalować modyfikacji DLL, ponieważ ścieżka instalacji moda nie została określona.\n\n" +
                                     "Spróbuj poniższe rozwiązania:\n" +
                                     "1. Zamknij to okno i upewnij się, że mod jest zainstalowany\n" +
                                     "2. Wybierz mod ponownie i spróbuj jeszcze raz";
                IsInstallationComplete = true;
                return;
            }
            
            if (!Directory.Exists(_targetMod.InstallPath))
            {
                System.Diagnostics.Debug.WriteLine("❌ BŁĄD: Katalog docelowy nie istnieje!");
                InstallationSummary = $"Błąd: Katalog docelowy nie istnieje:\n{_targetMod.InstallPath}";
                IsInstallationComplete = true;
                return;
            }

            // Popraw ścieżkę dla Epic Games jeśli nie zawiera katalogu AmongUs
            ModConfiguration targetModWithCorrectPath = _targetMod;
            if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase) && 
                !_targetMod.InstallPath.EndsWith("\\AmongUs") && 
                !_targetMod.InstallPath.EndsWith("\\AmongUs\\"))
            {
                string epicPath = Path.Combine(_targetMod.InstallPath, "AmongUs");
                System.Diagnostics.Debug.WriteLine($"🔍 Dodano 'AmongUs' do ścieżki Epic: {epicPath}");
                System.Diagnostics.Debug.WriteLine($"🔍 Czy skorygowana ścieżka istnieje? {(Directory.Exists(epicPath) ? "TAK" : "NIE")}");
                
                if (!Directory.Exists(epicPath))
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Skorygowany katalog dla Epic nie istnieje, próbuję utworzyć...");
                    try
                    {
                        Directory.CreateDirectory(epicPath);
                        System.Diagnostics.Debug.WriteLine("✅ Pomyślnie utworzono katalog");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Nie można utworzyć katalogu: {ex.Message}");
                    }
                }

                // Klonuj ModConfiguration z poprawioną ścieżką
                targetModWithCorrectPath = new ModConfiguration
                {
                    ModName = _targetMod.ModName,
                    Description = _targetMod.Description,
                    ModVersion = _targetMod.ModVersion,
                    InstallPath = epicPath,
                    IsSelected = _targetMod.IsSelected,
                    DllInstallPath = _targetMod.DllInstallPath
                };
            }

            // Szczegółowe informacje o wszystkich modach DLL do instalacji
            int index = 0;
            foreach (var dllMod in SelectedDllMods)
            {
                index++;
                System.Diagnostics.Debug.WriteLine($"🔍 DLL #{index}: {dllMod.ModName}, Wersja: {dllMod.ModVersion}");
                System.Diagnostics.Debug.WriteLine($"   └── Ścieżka instalacji DLL: {dllMod.DllInstallPath ?? "BepInEx\\plugins"}");
            }

            // Instaluj wybrane mody z poprawioną ścieżką
            bool allSuccessful = true;
            List<string> installedFiles = new List<string>(); // Przechowujemy ścieżki do zainstalowanych plików
            
            foreach (var dllMod in SelectedDllMods)
            {
                System.Diagnostics.Debug.WriteLine($"⏱️ Rozpoczynanie instalacji: {dllMod.ModName}...");
                string? installedPath = await _dllModificationService.InstallDllToModAsync(dllMod, targetModWithCorrectPath, platform);
                
                if (!string.IsNullOrEmpty(installedPath))
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Instalacja {dllMod.ModName} zakończona sukcesem");
                    
                    // Sprawdź czy plik faktycznie istnieje
                    bool fileExists = File.Exists(installedPath);
                    System.Diagnostics.Debug.WriteLine($"   └── Sprawdzenie pliku {installedPath}: {(fileExists ? "ISTNIEJE" : "NIE ISTNIEJE")}");
                    
                    if (fileExists)
                        installedFiles.Add(installedPath);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Instalacja {dllMod.ModName} NIEUDANA");
                    allSuccessful = false;
                }
            }

            // Po instalacji, pokaż podsumowanie
            if (SelectedDllMods.Count > 0)
            {
                var modNames = string.Join(", ", SelectedDllMods.Select(m => m.ModName));
                string summary = allSuccessful 
                    ? $"Pomyślnie zainstalowano następujące modyfikacje DLL:\n{modNames}" 
                    : $"Instalacja zakończona z ostrzeżeniami. Zainstalowano:\n{modNames}";
                
                // Dodaj ścieżki zainstalowanych plików
                if (installedFiles.Any())
                {
                    summary += "\n\nZainstalowane pliki:\n";
                    summary += string.Join("\n", installedFiles);
                }
                
                InstallationSummary = summary;
                IsInstallationComplete = true;
                System.Diagnostics.Debug.WriteLine($"🏁 PODSUMOWANIE: {summary.Replace("\n", " ")}");
            }
        }

        // Dodaj właściwość Platform
        public string Platform { get; }

        // Dodaj nową metodę do znajdowania ścieżki dla Epic
        private string? TryFindEpicInstallPath(string? modName)
        {
            try
            {
                // Jeśli nazwa moda jest null, zwróć null
                if (string.IsNullOrEmpty(modName))
                {
                    System.Diagnostics.Debug.WriteLine("❌ Nie można szukać ścieżki instalacji - nazwa moda jest pusta");
                    return null; // Teraz to jest zgodne z sygnaturą metody
                }

                // Typowe lokalizacje dla Epic Games
                string[] potentialPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Among Us - Mody"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Among Us - Mody"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Among Us - Mody")
                };

                foreach (var basePath in potentialPaths)
                {
                    if (Directory.Exists(basePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"🔍 Znaleziono bazowy katalog: {basePath}");
                        
                        // Szukaj katalogów zawierających nazwę moda
                        var modDirectories = Directory.GetDirectories(basePath)
                            .Where(dir => Path.GetFileName(dir).Contains(modName, StringComparison.OrdinalIgnoreCase));
                        
                        foreach (var modDir in modDirectories)
                        {
                            System.Diagnostics.Debug.WriteLine($"🔍 Znaleziono potencjalny katalog moda: {modDir}");
                            return modDir; // Zwróć pierwszy pasujący
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Błąd podczas szukania ścieżki: {ex.Message}");
            }

            return null; // Nie znaleziono ścieżki
        }

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
