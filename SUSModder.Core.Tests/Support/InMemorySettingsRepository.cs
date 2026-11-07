using SUSModder.Core.Configuration;
using SUSModder.Core.Data;

namespace SUSModder.Core.Tests.Support;

/// <summary>
/// Repozytorium ustawień w pamięci — testy nie dotykają bazy użytkownika.
/// </summary>
public class InMemorySettingsRepository : IUserSettingsRepository
{
    private UserSettings _settings = new();

    public UserSettings LoadSettings() => _settings;

    public virtual void SaveSettings(UserSettings settings) => _settings = settings;

    public void UpdateSetting(string columnName, object value)
    {
        switch (columnName)
        {
            case "legendary_use_pinned":
                _settings.LegendaryUsePinned = Convert.ToBoolean(value);
                break;
            case "legendary_release_source":
                _settings.LegendaryReleaseSource = Convert.ToString(value) ?? string.Empty;
                break;
            case "legendary_release_tag":
                _settings.LegendaryReleaseTag = Convert.ToString(value) ?? string.Empty;
                break;
            case "legendary_release_asset":
                _settings.LegendaryReleaseAsset = Convert.ToString(value) ?? string.Empty;
                break;
            case "legendary_release_sha256":
                _settings.LegendaryReleaseSha256 = Convert.ToString(value) ?? string.Empty;
                break;
            default:
                throw new ArgumentException($"Nieprawidłowa nazwa kolumny: {columnName}", nameof(columnName));
        }
    }

    public void UpdateSingleField(string column, object? value) =>
        UpdateSetting(column, value ?? string.Empty);

    public void ClearCache()
    {
    }
}
