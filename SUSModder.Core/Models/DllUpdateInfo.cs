using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Informacja o dostępnej aktualizacji moda DLL
    /// </summary>
    public class DllUpdateInfo : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        /// <summary>
        /// Mod DLL do zaktualizowania (z najnowszą wersją z API)
        /// </summary>
        public ModConfiguration DllMod { get; set; } = new();

        /// <summary>
        /// Obecna wersja zainstalowana lokalnie
        /// </summary>
        public string CurrentVersion { get; set; } = string.Empty;

        /// <summary>
        /// Nowa dostępna wersja
        /// </summary>
        public string NewVersion { get; set; } = string.Empty;

        /// <summary>
        /// Lista modów FULL gdzie ten DLL jest zainstalowany
        /// </summary>
        public List<ModConfiguration> InstallLocations { get; set; } = new();

        /// <summary>
        /// Wybrane lokalizacje do zaktualizowania (domyślnie wszystkie)
        /// </summary>
        public List<ModConfiguration> SelectedLocations { get; set; } = new();

        /// <summary>
        /// Czy ta aktualizacja jest zaznaczona (do pokazania w UI)
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        /// <summary>
        /// Opis zmiany wersji dla UI
        /// </summary>
        public string VersionChangeText =>
            $"{DllMod.ModName}: {CurrentVersion} → {NewVersion}";

        /// <summary>
        /// Liczba wybranych lokalizacji
        /// </summary>
        public int SelectedCount => SelectedLocations?.Count ?? 0;

        /// <summary>
        /// Całkowita liczba lokalizacji
        /// </summary>
        public int TotalLocations => InstallLocations?.Count ?? 0;

        /// <summary>
        /// Tekst dla UI z liczbą lokalizacji
        /// </summary>
        public string LocationsText =>
            $"Zainstalowany w {TotalLocations} {GetLocationWord(TotalLocations)}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string GetLocationWord(int count)
        {
            if (count == 1) return "lokalizacji";
            if (count >= 2 && count <= 4) return "lokalizacjach";
            return "lokalizacjach";
        }
    }
}
