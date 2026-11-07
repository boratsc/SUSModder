using SUSModder.Core.Data;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Data;

public class UserSettingsConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleInstance_DoesNotReenableTelemetry(bool saveSnapshot)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sus-settings-{Guid.NewGuid():N}.db");
        try
        {
            await using var firstDb = new DatabaseService(path);
            await firstDb.InitializeAsync();
            await using var secondDb = new DatabaseService(path);
            await secondDb.InitializeAsync();
            var first = new UserSettingsService(new UserSettingsRepository(firstDb));
            var second = new UserSettingsService(new UserSettingsRepository(secondDb));
            var stale = first.LoadUserSettings();

            second.UpdateUserSetting(s => s.TelemetryEnabled = false);
            if (saveSnapshot)
            {
                stale.Theme = "light";
                first.SaveUserSettings(stale);
            }
            else
                first.UpdateUserSetting(s => s.Theme = "light");

            var actual = new UserSettingsRepository(secondDb).LoadSettings();
            Assert.False(actual.TelemetryEnabled);
            Assert.Equal("light", actual.Theme);
            Assert.False(first.LoadUserSettings().TelemetryEnabled);
            first.UpdateUserSetting(s => s.TelemetryEnabled = true);
            Assert.True(new UserSettingsRepository(secondDb).LoadSettings().TelemetryEnabled);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task StaleSnapshot_DoesNotOverwriteLegendarySelection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sus-settings-{Guid.NewGuid():N}.db");
        try
        {
            await using var firstDb = new DatabaseService(path);
            await firstDb.InitializeAsync();
            await using var secondDb = new DatabaseService(path);
            await secondDb.InitializeAsync();
            var first = new UserSettingsService(new UserSettingsRepository(firstDb));
            var second = new UserSettingsService(new UserSettingsRepository(secondDb));
            var stale = first.LoadUserSettings();

            second.UpdateUserSetting(s =>
            {
                s.LegendaryUsePinned = false;
                s.LegendaryReleaseSource = "Heroic";
                s.LegendaryReleaseTag = "0.20.43";
                s.LegendaryReleaseAsset = "asset.exe";
                s.LegendaryReleaseSha256 = "hash";
            });
            stale.Theme = "light";
            first.SaveUserSettings(stale);

            var actual = new UserSettingsRepository(secondDb).LoadSettings();
            Assert.Equal("light", actual.Theme);
            Assert.False(actual.LegendaryUsePinned);
            Assert.Equal("Heroic", actual.LegendaryReleaseSource);
            Assert.Equal("0.20.43", actual.LegendaryReleaseTag);
            Assert.Equal("asset.exe", actual.LegendaryReleaseAsset);
            Assert.Equal("hash", actual.LegendaryReleaseSha256);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task FailedSave_DoesNotMutateServiceOrRepositoryCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sus-settings-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new DatabaseService(path);
            await db.InitializeAsync();
            var repository = new UserSettingsRepository(db);
            var service = new UserSettingsService(repository);
            service.LoadUserSettings();
            using var command = db.GetConnection().CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_settings BEFORE UPDATE ON user_settings BEGIN SELECT RAISE(FAIL, 'failed'); END;";
            command.ExecuteNonQuery();

            Assert.ThrowsAny<Exception>(() => service.UpdateUserSetting(s => s.LegendaryReleaseTag = "uncommitted"));
            Assert.Equal(string.Empty, service.LoadUserSettings().LegendaryReleaseTag);
            Assert.Equal(string.Empty, repository.LoadSettings().LegendaryReleaseTag);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
