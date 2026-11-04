using System;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Przechowuje lokalny znacznik wersji zasad czatu, którą użytkownik już zobaczył.
    /// To znacznik zapoznania się, nie formalna zgoda prawna.
    /// </summary>
    public interface IChatRulesStateStore
    {
        /// <summary>Ostatnio potwierdzona wersja zasad (0 = nigdy).</summary>
        int GetAcknowledgedVersion();

        /// <summary>Zapisuje wersję zasad potwierdzoną przez użytkownika.</summary>
        void SetAcknowledgedVersion(int version);
    }

    /// <summary>
    /// Implementacja oparta o ustawienia użytkownika (SQLite, fallback JSON).
    /// Błędy zapisu/odczytu nie mogą przerwać działania czatu — znacznik jest wyłącznie wygodą.
    /// </summary>
    public sealed class UserSettingsChatRulesStateStore : IChatRulesStateStore
    {
        private readonly UserSettingsService _settings;

        public UserSettingsChatRulesStateStore(UserSettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <inheritdoc/>
        public int GetAcknowledgedVersion()
        {
            try
            {
                return _settings.LoadUserSettings().ChatRulesAcknowledgedVersion;
            }
            catch
            {
                return 0;
            }
        }

        /// <inheritdoc/>
        public void SetAcknowledgedVersion(int version)
        {
            try
            {
                var settings = _settings.LoadUserSettings();
                if (settings.ChatRulesAcknowledgedVersion == version)
                    return;

                settings.ChatRulesAcknowledgedVersion = version;
                _settings.SaveUserSettings(settings);
            }
            catch
            {
                // Znacznik jest opcjonalny; brak zapisu oznacza ponowne pokazanie zasad.
            }
        }
    }
}
