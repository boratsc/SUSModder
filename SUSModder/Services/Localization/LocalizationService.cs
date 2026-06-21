using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ReactiveUI;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Services.Localization;

/// <summary>
/// Implementacja serwisu lokalizacji z obsługą live switching.
/// </summary>
public class LocalizationService : ReactiveObject, ILocalizationService
{
    private string _currentCulture = "pl";
    private Dictionary<string, Dictionary<string, object>> _translations = new();
    private const string DefaultCulture = "pl";

    /// <summary>
    /// Aktualnie wybrany język.
    /// </summary>
    public string CurrentCulture
    {
        get => _currentCulture;
        private set => this.RaiseAndSetIfChanged(ref _currentCulture, value);
    }

    public LocalizationService()
    {
        // Ładujemy wszystkie dostępne języki z embedded resources
        LoadAllTranslations();

        // Ustawiamy domyślny język
        CurrentCulture = DefaultCulture;
    }

    /// <summary>
    /// Ładuje wszystkie pliki JSON z embedded resources.
    /// </summary>
    private void LoadAllTranslations()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(name => name.Contains("Localization") && name.EndsWith(".json"))
                .ToList();

            Console.WriteLine($"[LocalizationService] Znaleziono {resourceNames.Count} zasobów lokalizacji");

            foreach (var resourceName in resourceNames)
            {
                // Wydobądź kod języka z nazwy zasobu (np. "SUSModder.Localization.pl.json" -> "pl")
                var parts = resourceName.Split('.');
                var culture = parts.Length >= 2 ? parts[^2] : "pl"; // Przedostatni element to kod języka

                Console.WriteLine($"[LocalizationService] Ładowanie języka: {culture} z {resourceName}");
                LoadTranslationFromResource(culture, resourceName, assembly);
            }

            if (_translations.Count == 0)
            {
                Console.WriteLine($"[LocalizationService] OSTRZEŻENIE: Nie załadowano żadnych tłumaczeń!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalizationService] Błąd ładowania tłumaczeń: {ex.Message}");
        }
    }

    /// <summary>
    /// Ładuje pojedynczy plik JSON z embedded resource.
    /// </summary>
    private void LoadTranslationFromResource(string culture, string resourceName, Assembly assembly)
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Console.WriteLine($"[LocalizationService] Nie znaleziono zasobu: {resourceName}");
                return;
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var translations = JsonSerializer.Deserialize<Dictionary<string, object>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (translations != null)
            {
                _translations[culture] = translations;
                Console.WriteLine($"[LocalizationService] Załadowano {translations.Count} kluczy dla języka {culture}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalizationService] Błąd ładowania {culture}.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Pobiera przetłumaczony string dla podanego klucza.
    /// </summary>
    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "[EMPTY_KEY]";

        // Próbuj pobrać z aktualnego języka
        var value = GetFromCulture(CurrentCulture, key);
        if (value != null)
            return value;

        // Fallback do języka domyślnego (pl)
        if (CurrentCulture != DefaultCulture)
        {
            value = GetFromCulture(DefaultCulture, key);
            if (value != null)
            {
                // Opcjonalnie: log warning o brakującym tłumaczeniu
                return value;
            }
        }

        // Nie znaleziono w żadnym języku
        return $"[{key}]";
    }

    /// <summary>
    /// Pobiera wartość z konkretnego języka, delegując nawigację po
    /// drzewie tłumaczeń do <see cref="LocalizationKeyResolver"/>, który
    /// obsługuje zarówno zagnieżdżone obiekty JSON, jak i płaskie klucze
    /// zawierające kropki w nazwie (np. LaunchDiagnostics.Severity.Info).
    /// </summary>
    private string? GetFromCulture(string culture, string key)
    {
        if (!_translations.TryGetValue(culture, out var tree))
            return null;

        return LocalizationKeyResolver.Resolve(tree, key);
    }

    /// <summary>
    /// Pobiera przetłumaczony string z formatowaniem.
    /// </summary>
    public string GetFormatted(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            // Jeśli formatowanie się nie uda, zwróć szablon
            return template;
        }
    }

    /// <summary>
    /// Zmienia aktualny język i odświeża wszystkie bindingi.
    /// </summary>
    public void ChangeCulture(string culture)
    {
        if (_currentCulture == culture)
            return;

        if (!IsCultureAvailable(culture))
        {
            Console.WriteLine($"[LocalizationService] Język {culture} nie jest dostępny");
            return;
        }

        CurrentCulture = culture;

        // KLUCZOWE: Powiadom wszystkie bindingi o zmianie
        // RaisePropertyChanged z pustym stringiem oznacza "wszystkie property się zmieniły"
        this.RaisePropertyChanged(string.Empty);
    }

    /// <summary>
    /// Sprawdza czy język jest dostępny.
    /// </summary>
    public bool IsCultureAvailable(string culture)
    {
        return _translations.ContainsKey(culture);
    }

    /// <summary>
    /// Zwraca listę dostępnych języków.
    /// </summary>
    public IEnumerable<string> GetAvailableCultures()
    {
        return _translations.Keys.OrderBy(c => c);
    }

    /// <summary>
    /// Indexer dla łatwiejszego bindingu w AXAML.
    /// Umożliwia użycie: Binding Path="Item[UI.Buttons.Install]"
    /// </summary>
    public string this[string key] => Get(key);
}
