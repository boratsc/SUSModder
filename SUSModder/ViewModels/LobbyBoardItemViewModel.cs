using System;
using ReactiveUI;
using SUSModder.Core.Models;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Pojedynczy element na liście kodów lub wiadomości w lobby board.
    /// </summary>
    public class LobbyBoardItemViewModel : ReactiveObject
    {
        private readonly ILocalizationService _loc;

        public string Id { get; }
        public LobbyEntryType Type { get; }

        // Code-specific
        public string? Code { get; }
        public string? Region { get; }
        public int? MaxPlayers { get; }
        public int? CurrentPlayers { get; }
        public string PlayerCountDisplay => CurrentPlayers.HasValue
            ? $"{CurrentPlayers}/{MaxPlayers ?? 15}"
            : $"?/{MaxPlayers ?? 15}";

        // Message-specific
        public string? Content { get; }

        // Wspólne
        public string TimeAgoDisplay { get; }
        public bool IsOwnEntry { get; }

        /// <summary>
        /// Kod do skopiowania — używany przez view code-behind.
        /// </summary>
        public string? CopyTarget => Code;

        public LobbyBoardItemViewModel(LobbyBoardEntry entry, string? userHash, ILocalizationService loc)
        {
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            Id = entry.Id;
            Type = entry.Type;
            Code = entry.Code;
            Region = entry.Region;
            MaxPlayers = entry.MaxPlayers;
            CurrentPlayers = entry.CurrentPlayers;
            Content = entry.Content;
            TimeAgoDisplay = FormatTimeAgo(entry.AgeSeconds);
            IsOwnEntry = false; // TODO: porównanie z userHash autora (backend nie zwraca userHash w GET)

            // Jeśli backend obsługuje X-User-Hash w GET, można porównać
            // IsOwnEntry = entry.UserHash == userHash;
        }

        private string FormatTimeAgo(int ageSeconds)
        {
            if (ageSeconds < 60)
                return _loc.Get("UI.LobbyBoard.Time.JustNow");
            int minutes = ageSeconds / 60;
            if (minutes < 60)
                return _loc.GetFormatted("UI.LobbyBoard.Time.MinutesAgo", minutes);
            int hours = minutes / 60;
            return _loc.GetFormatted("UI.LobbyBoard.Time.HoursAgo", hours);
        }
    }
}
