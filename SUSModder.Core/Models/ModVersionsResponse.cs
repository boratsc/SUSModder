using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Response z endpointu GET /susmodder-config-versions
    /// </summary>
    public class ModVersionsResponse
    {
        /// <summary>
        /// Czy request się powiódł
        /// </summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        /// <summary>
        /// ID moda (jeśli filtrowano po modId)
        /// null jeśli pobrano wszystkie mody
        /// </summary>
        [JsonPropertyName("modId")]
        public int? ModId { get; set; }

        /// <summary>
        /// Liczba zwróconych wersji
        /// </summary>
        [JsonPropertyName("count")]
        public int Count { get; set; }

        /// <summary>
        /// Lista wersji (sortowana od najnowszej do najstarszej)
        /// </summary>
        [JsonPropertyName("versions")]
        public List<ModVersionHistory> Versions { get; set; } = new();

        /// <summary>
        /// Pomocnicza właściwość - czy są jakieś wersje
        /// </summary>
        [JsonIgnore]
        public bool HasVersions => Versions?.Count > 0;

        /// <summary>
        /// Pomocnicza właściwość - najnowsza wersja
        /// </summary>
        [JsonIgnore]
        public ModVersionHistory? LatestVersion => Versions?.FirstOrDefault();
    }
}
