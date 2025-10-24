using System.Collections.Generic;
using System.ComponentModel;

namespace SUSModder.Core.Services.Localization;

/// <summary>
/// Serwis zarządzania wielojęzycznością aplikacji.
/// Implementacja powinna wspierać INotifyPropertyChanged dla live switching.
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>
    /// Aktualnie wybrany język (np. "pl", "en", "de").
    /// </summary>
    string CurrentCulture { get; }

    /// <summary>
    /// Pobiera przetłumaczony string dla podanego klucza.
    /// </summary>
    /// <param name="key">Klucz w formacie "Category.Subcategory.Key" (np. "UI.Buttons.Install")</param>
    /// <returns>Przetłumaczony string lub klucz w nawiasach jeśli nie znaleziono</returns>
    string Get(string key);

    /// <summary>
    /// Pobiera przetłumaczony string z formatowaniem (string.Format).
    /// </summary>
    /// <param name="key">Klucz tłumaczenia</param>
    /// <param name="args">Parametry do formatowania</param>
    /// <returns>Sformatowany przetłumaczony string</returns>
    string GetFormatted(string key, params object[] args);

    /// <summary>
    /// Zmienia aktualny język i odświeża wszystkie bindingi.
    /// </summary>
    /// <param name="culture">Kod języka (np. "pl", "en")</param>
    void ChangeCulture(string culture);

    /// <summary>
    /// Sprawdza czy dany język jest dostępny (czy istnieje plik JSON).
    /// </summary>
    /// <param name="culture">Kod języka do sprawdzenia</param>
    /// <returns>True jeśli język jest dostępny</returns>
    bool IsCultureAvailable(string culture);

    /// <summary>
    /// Zwraca listę wszystkich dostępnych języków.
    /// </summary>
    /// <returns>Kody języków (np. ["pl", "en", "de"])</returns>
    IEnumerable<string> GetAvailableCultures();
}
