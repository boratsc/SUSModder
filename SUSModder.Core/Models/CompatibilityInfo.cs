using System;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Informacja o kompatybilności między modem FULL a modem DLL
    /// </summary>
    public class CompatibilityInfo
    {
        /// <summary>
        /// ID wpisu kompatybilności
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Kod statusu z API (F/W/NT/NW)
        /// </summary>
        [JsonPropertyName("status")]
        public string StatusCode { get; set; } = "NT";

        /// <summary>
        /// Data ostatniego testu
        /// </summary>
        [JsonPropertyName("testedDate")]
        public DateTime? TestedDate { get; set; }

        /// <summary>
        /// Kto testował
        /// </summary>
        [JsonPropertyName("testedBy")]
        public string? TestedBy { get; set; }

        /// <summary>
        /// Wersja Among Us użyta w testach
        /// </summary>
        [JsonPropertyName("amongUsVersion")]
        public string? AmongUsVersion { get; set; }

        /// <summary>
        /// Notatki o kompatybilności
        /// </summary>
        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        /// <summary>
        /// Link do zgłoszenia problemów
        /// </summary>
        [JsonPropertyName("issuesUrl")]
        public string? IssuesUrl { get; set; }

        /// <summary>
        /// Czy test był na aktualnej wersji modów
        /// </summary>
        [JsonPropertyName("isCurrentVersion")]
        public bool IsCurrentVersion { get; set; }

        /// <summary>
        /// Ostrzeżenie jeśli test nie był na aktualnej wersji
        /// </summary>
        [JsonPropertyName("warning")]
        public string? Warning { get; set; }

        /// <summary>
        /// Pomocnicza właściwość - status jako enum
        /// </summary>
        [JsonIgnore]
        public CompatibilityStatus Status =>
            CompatibilityStatusExtensions.FromApiCode(StatusCode);

        /// <summary>
        /// Pomocnicza właściwość - opis dla UI
        /// </summary>
        [JsonIgnore]
        public string Description => Status.GetDescription();

        /// <summary>
        /// Pomocnicza właściwość - emoji dla UI
        /// </summary>
        [JsonIgnore]
        public string Emoji => Status.GetEmoji();

        /// <summary>
        /// Pomocnicza właściwość - formatowana data testu
        /// </summary>
        [JsonIgnore]
        public string FormattedTestedDate =>
            TestedDate?.ToString("yyyy-MM-dd") ?? "Brak danych";
    }
}
