using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Wydanie moda z katalogu (3.1-11): stabilne albo beta.
    /// <see cref="ReleaseId"/> jest nieprzezroczystym identyfikatorem nadanym przez backend;
    /// ten sam numer wersji może wystąpić w różnych kanałach.
    /// </summary>
    public sealed class ModRelease
    {
        [JsonPropertyName("releaseId")]
        public string ReleaseId { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("isBeta")]
        public bool IsBeta { get; set; }

        [JsonPropertyName("amongVersion")]
        public string AmongVersion { get; set; } = string.Empty;

        [JsonPropertyName("isCurrent")]
        public bool IsCurrent { get; set; }

        [JsonPropertyName("variants")]
        public List<ModReleaseVariant> Variants { get; set; } = new();
    }

    /// <summary>
    /// Wariant pobierania konkretnego wydania (platforma/architektura).
    /// Pusta lista wariantów oznacza brak danych o platformach, nie brak wydania.
    /// </summary>
    public sealed class ModReleaseVariant
    {
        [JsonPropertyName("platform")]
        public string Platform { get; set; } = string.Empty;

        [JsonPropertyName("architecture")]
        public string Architecture { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("fileSizeBytes")]
        public long? FileSizeBytes { get; set; }
    }
}
