using System.Text.Json.Serialization;

namespace SUSModder.Models
{
    /// <summary>
    /// Odpowiedź z API /api/online-users
    /// </summary>
    public class OnlineUsersResponse
    {
        /// <summary>
        /// Liczba użytkowników online
        /// </summary>
        [JsonPropertyName("online")]
        public int Online { get; set; }

        /// <summary>
        /// Znacznik czasu odpowiedzi
        /// </summary>
        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }
    }
}
