using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Query info z response
    /// </summary>
    public class CompatibilityQuery
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty; // "dll" lub "full"

        [JsonPropertyName("modId")]
        public int ModId { get; set; }

        [JsonPropertyName("modName")]
        public string ModName { get; set; } = string.Empty;

        [JsonPropertyName("modVersion")]
        public string? ModVersion { get; set; }
    }

    /// <summary>
    /// Informacja o drugim modzie w parze
    /// </summary>
    public class CompatibilityModInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("currentVersion")]
        public string CurrentVersion { get; set; } = string.Empty;
    }

    /// <summary>
    /// Pojedynczy wpis kompatybilności z response
    /// </summary>
    public class CompatibilityEntry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = "NT";

        [JsonPropertyName("testedDate")]
        public string? TestedDate { get; set; }

        [JsonPropertyName("testedBy")]
        public string? TestedBy { get; set; }

        [JsonPropertyName("amongUsVersion")]
        public string? AmongUsVersion { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("issuesUrl")]
        public string? IssuesUrl { get; set; }

        [JsonPropertyName("isCurrentVersion")]
        public bool IsCurrentVersion { get; set; }

        [JsonPropertyName("warning")]
        public string? Warning { get; set; }

        // Jeden z tych będzie wypełniony (w zależności od query type)
        [JsonPropertyName("fullMod")]
        public CompatibilityModInfo? FullMod { get; set; }

        [JsonPropertyName("dllMod")]
        public CompatibilityModInfo? DllMod { get; set; }
    }

    /// <summary>
    /// Response z endpointu GET /api/compatibility
    /// </summary>
    public class CompatibilityResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("query")]
        public CompatibilityQuery? Query { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("compatibilities")]
        public List<CompatibilityEntry>? Compatibilities { get; set; }

        /// <summary>
        /// Pomocnicza właściwość - czy są jakieś wyniki
        /// </summary>
        [JsonIgnore]
        public bool HasCompatibilities =>
            Compatibilities?.Count > 0;

        /// <summary>
        /// Pomocnicza właściwość - pierwszy wynik (najczęściej szukamy tylko jednej pary)
        /// </summary>
        [JsonIgnore]
        public CompatibilityEntry? FirstCompatibility =>
            Compatibilities?.FirstOrDefault();
    }
}
