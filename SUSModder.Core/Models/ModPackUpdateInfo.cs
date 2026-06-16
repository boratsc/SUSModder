using System.Collections.Generic;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Wynik sprawdzenia aktualizacji paczki modów dla lokalnej instancji.
    /// Porównuje lokalną instancję z aktualną paczką z API.
    /// </summary>
    public sealed class ModPackUpdateInfo
    {
        /// <summary>
        /// Kod paczki (sourcePackCode).
        /// </summary>
        public string PackCode { get; set; } = string.Empty;

        /// <summary>
        /// ID lokalnej instancji.
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        /// <summary>
        /// Nazwa instancji (displayName).
        /// </summary>
        public string InstanceName { get; set; } = string.Empty;

        /// <summary>
        /// Czy paczka ma dostępną aktualizację.
        /// </summary>
        public bool HasUpdate { get; set; }

        /// <summary>
        /// Czy sprawdzenie się powiodło (false = błąd połączenia / 404).
        /// </summary>
        public bool CheckSucceeded { get; set; } = true;

        /// <summary>
        /// Komunikat błędu jeśli CheckSucceeded = false.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Lista zmian w modzie głównym (puste jeśli brak zmiany).
        /// </summary>
        public List<ModPackChangeItem> Changes { get; set; } = new();
    }

    /// <summary>
    /// Pojedyncza zmiana w paczce (różnica między lokalną instancją a aktualną paczką).
    /// </summary>
    public sealed class ModPackChangeItem
    {
        /// <summary>
        /// Typ zmiany: "fullMod", "dll", "externalDll", "config".
        /// </summary>
        public string ChangeType { get; set; } = string.Empty;

        /// <summary>
        /// Nazwa elementu (nazwa moda, nazwa DLL).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Stara wartość (wersja, hash, itp).
        /// </summary>
        public string? OldValue { get; set; }

        /// <summary>
        /// Nowa wartość.
        /// </summary>
        public string? NewValue { get; set; }

        /// <summary>
        /// Tekst opisujący zmianę (dla UI).
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }
}
