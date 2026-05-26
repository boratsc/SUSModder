using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Dane changeloga / "Co nowego" – wczytywane z whatsnew.json
    /// </summary>
    public class ChangelogData
    {
        /// <summary>
        /// Wersja aplikacji, której dotyczy changelog
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Data wydania w formacie YYYY-MM-DD
        /// </summary>
        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        /// <summary>
        /// Sekcje changeloga (np. Nowe funkcje, Poprawki)
        /// </summary>
        [JsonPropertyName("sections")]
        public List<ChangelogSection> Sections { get; set; } = new();

        /// <summary>
        /// URL do pełnego changeloga na GitHub
        /// </summary>
        [JsonPropertyName("githubUrl")]
        public string GithubUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Pojedyncza sekcja changeloga z ikoną, tytułem i listą itemów
    /// </summary>
    public class ChangelogSection
    {
        /// <summary>
        /// Emoji/ikona sekcji (np. "✨", "🔧")
        /// </summary>
        [JsonPropertyName("icon")]
        public string Icon { get; set; } = string.Empty;

        /// <summary>
        /// Tytuł sekcji (np. "Nowe funkcje", "Poprawki").
        /// Używany bezpośrednio gdy nie ma titleKey.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Klucz lokalizacyjny dla tytułu sekcji (np. "Changelog.SectionFeatures").
        /// Jeśli ustawiony, tytuł zostanie przetłumaczony przez ILocalizationService.
        /// </summary>
        [JsonPropertyName("titleKey")]
        public string? TitleKey { get; set; }

        /// <summary>
        /// Lista punktów w sekcji
        /// </summary>
        [JsonPropertyName("items")]
        public List<string> Items { get; set; } = new();
    }
}
