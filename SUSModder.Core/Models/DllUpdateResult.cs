using System.Collections.Generic;
using System.Linq;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Wynik aktualizacji moda DLL w wielu lokalizacjach
    /// </summary>
    public class DllUpdateResult
    {
        /// <summary>
        /// Nazwa zaktualizowanego DLL
        /// </summary>
        public string DllName { get; set; } = string.Empty;

        /// <summary>
        /// Całkowita liczba lokalizacji do zaktualizowania
        /// </summary>
        public int TotalLocations { get; set; }

        /// <summary>
        /// Liczba pomyślnych aktualizacji
        /// </summary>
        public int SuccessfulUpdates { get; set; }

        /// <summary>
        /// Liczba nieudanych aktualizacji
        /// </summary>
        public int FailedUpdates { get; set; }

        /// <summary>
        /// Lista nazw modów FULL gdzie aktualizacja się powiodła
        /// </summary>
        public List<string> UpdatedLocations { get; set; } = new();

        /// <summary>
        /// Lista nazw modów FULL gdzie aktualizacja się nie powiodła
        /// </summary>
        public List<string> FailedLocations { get; set; } = new();

        /// <summary>
        /// Czy wszystkie aktualizacje się powiodły
        /// </summary>
        public bool AllSuccessful => FailedUpdates == 0;

        /// <summary>
        /// Czy jakiekolwiek aktualizacje się powiodły
        /// </summary>
        public bool AnySuccessful => SuccessfulUpdates > 0;

        /// <summary>
        /// Procent sukcesu
        /// </summary>
        public int SuccessPercentage =>
            TotalLocations > 0 ? (SuccessfulUpdates * 100 / TotalLocations) : 0;

        /// <summary>
        /// Podsumowanie tekstowe dla UI
        /// </summary>
        public string Summary
        {
            get
            {
                if (AllSuccessful)
                {
                    return $"✅ Pomyślnie zaktualizowano {DllName} w {SuccessfulUpdates} lokalizacjach";
                }
                else if (AnySuccessful)
                {
                    return $"⚠️ Zaktualizowano {DllName} w {SuccessfulUpdates}/{TotalLocations} lokalizacjach";
                }
                else
                {
                    return $"❌ Nie udało się zaktualizować {DllName} w żadnej lokalizacji";
                }
            }
        }

        /// <summary>
        /// Szczegółowe podsumowanie dla UI
        /// </summary>
        public string DetailedSummary
        {
            get
            {
                var lines = new List<string> { Summary };

                if (UpdatedLocations.Any())
                {
                    lines.Add("");
                    lines.Add("Zaktualizowano w:");
                    lines.AddRange(UpdatedLocations.Select(loc => $"  ✓ {loc}"));
                }

                if (FailedLocations.Any())
                {
                    lines.Add("");
                    lines.Add("Nie udało się zaktualizować w:");
                    lines.AddRange(FailedLocations.Select(loc => $"  ✗ {loc}"));
                }

                return string.Join("\n", lines);
            }
        }
    }
}
