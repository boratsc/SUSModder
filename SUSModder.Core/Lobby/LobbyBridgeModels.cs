using System;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Lobby
{
    /// <summary>
    /// Model danych odczytywany z lobby-bridge.json (zapisywanego przez SUSModder.Integration.dll).
    /// Format zgodny z specyfikacją lobby-bridge-protocol.md v1.
    /// </summary>
    internal sealed class LobbyBridgeFileData
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("modId")]
        public int ModId { get; set; }

        [JsonPropertyName("region")]
        public string Region { get; set; } = "Modded EU";

        [JsonPropertyName("maxPlayers")]
        public int MaxPlayers { get; set; }

        [JsonPropertyName("isPublic")]
        public bool IsPublic { get; set; }

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyName("bridgeVersion")]
        public int BridgeVersion { get; set; } = 1;
    }

    /// <summary>
    /// EventArgs dla zdarzenia wykrycia kodu lobby przez LobbyBridgeFileReader.
    /// </summary>
    public class LobbyCodeDetectedEventArgs : EventArgs
    {
        /// <summary>Kod lobby (4-6 znaków A-Z0-9).</summary>
        public string Code { get; init; } = string.Empty;

        /// <summary>ID moda w katalogu SUSModder.</summary>
        public int ModId { get; init; }

        /// <summary>Region serwera (Modded EU / NA / Asia).</summary>
        public string Region { get; init; } = "Modded EU";

        /// <summary>Maksymalna liczba graczy w lobby.</summary>
        public int MaxPlayers { get; init; }

        /// <summary>Timestamp zapisu z pliku bridge.</summary>
        public DateTimeOffset Timestamp { get; init; }
    }
}
