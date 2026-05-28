using System;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Typ wpisu na lobby board.
    /// </summary>
    public enum LobbyEntryType
    {
        Code,
        Message,
        All
    }

    /// <summary>
    /// Pojedynczy wpis z lobby board (kod lub wiadomość).
    /// </summary>
    public sealed record LobbyBoardEntry(
        string Id,
        [property: JsonPropertyName("type")]
        LobbyEntryType Type,
        [property: JsonPropertyName("modId")]
        int ModId,
        [property: JsonPropertyName("modName")]
        string ModName,
        [property: JsonPropertyName("publishedAt")]
        DateTimeOffset PublishedAt,
        [property: JsonPropertyName("expiresAt")]
        DateTimeOffset ExpiresAt,
        [property: JsonPropertyName("ageSeconds")]
        int AgeSeconds,
        // Code-specific (null dla Message)
        [property: JsonPropertyName("code")]
        string? Code,
        [property: JsonPropertyName("region")]
        string? Region,
        [property: JsonPropertyName("maxPlayers")]
        int? MaxPlayers,
        [property: JsonPropertyName("currentPlayers")]
        int? CurrentPlayers,
        // Message-specific (null dla Code)
        [property: JsonPropertyName("content")]
        string? Content
    );

    /// <summary>
    /// Rezultat publikacji wpisu.
    /// </summary>
    public sealed record PostEntryResult(
        [property: JsonPropertyName("success")]
        bool Success,
        [property: JsonPropertyName("id")]
        string? EntryId,
        [property: JsonPropertyName("expiresAt")]
        DateTimeOffset? ExpiresAt,
        [property: JsonPropertyName("errorCode")]
        string? ErrorCode,
        [property: JsonPropertyName("moderationWarning")]
        bool ModerationWarning
    );

    /// <summary>
    /// Dane autoryzacyjne do Among Us / Innersloth potrzebne do lookupu lobby.
    /// </summary>
    public sealed record AmongUsAuth(
        string IdToken,
        string Puid,
        string Username,
        int ClientVersion
    );

    /// <summary>
    /// Wynik live lookupu stanu lobby z serwera regionu.
    /// </summary>
    public sealed record LobbyLookupResult(
        int PlayerCount,
        int MaxPlayers,
        string? Map,
        DateTimeOffset QueriedAt
    );

    /// <summary>
    /// Wrapper dla response GET /api/lobby-board.
    /// </summary>
    internal sealed class LobbyBoardResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("entries")]
        public List<LobbyBoardEntry> Entries { get; set; } = new();

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    /// <summary>
    /// Wrapper dla response POST /api/lobby-board.
    /// </summary>
    internal sealed class PostLobbyEntryResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTimeOffset? ExpiresAt { get; set; }

        [JsonPropertyName("moderationWarning")]
        public bool ModerationWarning { get; set; }

        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }
    }

    /// <summary>
    /// Wrapper dla response DELETE / PATCH / REPORT.
    /// </summary>
    internal sealed class SimpleResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }
}
