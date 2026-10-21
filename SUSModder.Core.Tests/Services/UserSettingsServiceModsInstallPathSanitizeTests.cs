using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

public class UserSettingsServiceModsInstallPathSanitizeTests
{
    [Fact]
    public void LoadUserSettings_InvalidModsInstallPath_IsCorrectedInRepository()
    {
        var repository = new FakeUserSettingsRepository
        {
            Settings = new UserSettings
            {
                SettingsVersion = 1,
                ModsInstallPath = @"C:\Program Files (x86)\Steam\steamapps\common\Among Us\Among Us_Data"
            }
        };

        var service = new UserSettingsService(repository);
        var loaded = service.LoadUserSettings();

        Assert.Equal(Path.GetFullPath(PathSettings.DefaultModsPath), Path.GetFullPath(loaded.ModsInstallPath));
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(loaded.ModsInstallPath, repository.Settings.ModsInstallPath);
    }

    [Fact]
    public void LoadUserSettings_ValidModsInstallPath_IsNotChanged()
    {
        var validPath = Path.Combine(Path.GetTempPath(), "susmodder-valid-mods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(validPath);

        try
        {
            var repository = new FakeUserSettingsRepository
            {
                Settings = new UserSettings
                {
                    SettingsVersion = 1,
                    ModsInstallPath = validPath
                }
            };

            var service = new UserSettingsService(repository);
            var loaded = service.LoadUserSettings();

            Assert.Equal(Path.GetFullPath(validPath), Path.GetFullPath(loaded.ModsInstallPath));
            Assert.Equal(0, repository.SaveCount);
        }
        finally
        {
            Directory.Delete(validPath, true);
        }
    }

    private sealed class FakeUserSettingsRepository : IUserSettingsRepository
    {
        public UserSettings Settings { get; set; } = new();
        public int SaveCount { get; private set; }

        public UserSettings LoadSettings() => Settings;

        public void SaveSettings(UserSettings settings)
        {
            Settings = settings;
            SaveCount++;
        }

        public void UpdateSetting(string columnName, object value) =>
            throw new NotSupportedException();

        public void UpdateSingleField(string column, object? value) =>
            throw new NotSupportedException();

        public void ClearCache()
        {
        }
    }
}
