using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Metadane instalacji moda
    /// </summary>
    public class InstallationMetadata
    {
        /// <summary>
        /// Notatki o instalacji
        /// </summary>
        [JsonPropertyName("notes")]
        public string Notes { get; set; } = string.Empty;

        /// <summary>
        /// Niestandardowe tagi
        /// </summary>
        [JsonPropertyName("customTags")]
        public List<string> CustomTags { get; set; } = new();
    }
}
