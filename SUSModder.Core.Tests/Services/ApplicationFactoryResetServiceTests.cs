using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

/// <summary>
/// Factory reset czyści dane aplikacji, ale ZACHOWUJE magazyn tokenów właściciela paczek
/// (pack-owner-tokens to credentials, nie ustawienia — decyzja użytkownika 2026-11-24).
/// Test pracuje na izolowanym katalogu temp — nigdy na realnym %APPDATA%\SUSModder.
/// </summary>
public class ApplicationFactoryResetServiceTests
{
    [Fact]
    public void ClearDirectoryContents_RemovesSettingsAndFiles_KeepsOwnerTokenStoreWithContent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "user-settings.json"), "{\"modsInstallPath\":\"X\"}");
            File.WriteAllText(Path.Combine(dir, "susmodder.db"), "db-content");
            File.WriteAllText(Path.Combine(dir, ApplicationFactoryResetService.PendingResetFlagFileName), "flag");
            var logs = Directory.CreateDirectory(Path.Combine(dir, "Logs"));
            File.WriteAllText(Path.Combine(logs.FullName, "app.log"), "log");

            var storePath = Path.Combine(dir, PackOwnerTokenStore.FileName);
            const string storeContent = "{\"live-pack\":\"DPAPI-blob\"}";
            File.WriteAllText(storePath, storeContent);

            ApplicationFactoryResetService.ClearDirectoryContents(dir);

            // Magazyn tokenów nietknięty, z zawartością.
            Assert.True(File.Exists(storePath));
            Assert.Equal(storeContent, File.ReadAllText(storePath));

            // Reszta ustawień/plików/katalogów skasowana.
            Assert.False(File.Exists(Path.Combine(dir, "user-settings.json")));
            Assert.False(File.Exists(Path.Combine(dir, "susmodder.db")));
            Assert.False(File.Exists(Path.Combine(dir, ApplicationFactoryResetService.PendingResetFlagFileName)));
            Assert.False(Directory.Exists(logs.FullName));
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
