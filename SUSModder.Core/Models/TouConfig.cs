namespace SUSModder.Core.Models
{
    /// <summary>
    /// Pojedynczy wpis w historii zapisanych konfiguracji ToU (Town of Us).
    /// Zastępuje dynamic z Newtonsoft.Json w touConfigsBase.json.
    /// </summary>
    public class TouConfig
    {
        /// <summary>
        /// ID wpisu (auto-inkrementowane w SQLite)
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Hash konfiguracji
        /// </summary>
        public string Hash { get; set; } = string.Empty;

        /// <summary>
        /// Data utworzenia (ISO 8601)
        /// </summary>
        public string CreatedAt { get; set; } = string.Empty;
    }
}
