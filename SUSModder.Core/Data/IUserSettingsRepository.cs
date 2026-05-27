using SUSModder.Core.Configuration;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Interfejs repozytorium dla ustawień użytkownika (tabela user_settings w SQLite).
    /// </summary>
    public interface IUserSettingsRepository
    {
        /// <summary>
        /// Wczytuje ustawienia użytkownika z bazy.
        /// </summary>
        UserSettings LoadSettings();

        /// <summary>
        /// Zapisuje ustawienia użytkownika do bazy.
        /// </summary>
        void SaveSettings(UserSettings settings);

        /// <summary>
        /// Aktualizuje pojedyncze pole w ustawieniach (alias dla UpdateSetting z wsparciem nullable).
        /// </summary>
        void UpdateSetting(string columnName, object value);

        /// <summary>
        /// Aktualizuje pojedyncze pole w ustawieniach z obsługą wartości null.
        /// </summary>
        void UpdateSingleField(string column, object? value);

        /// <summary>
        /// Czyści cache w pamięci.
        /// </summary>
        void ClearCache();
    }
}
