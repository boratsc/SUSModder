using SUSModder.Core.Data;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public class ModInstanceTouConfigServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ModInstanceTouConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void SaveSnapshot_AndApply_RoundTripsJson()
    {
        using var db = CreateDb();
        var repo = new ModInstanceRepository(db);
        repo.AddInstance(new ModInstance
        {
            InstanceId = "tou-test",
            DisplayName = "ToU test",
            BaseModId = 10,
            BaseModName = "Town of Us",
            InstallPath = Path.Combine(_tempDir, "tou"),
            Platform = "steam"
        });

        const string json = "{\"test\":true}";
        ModInstanceTouConfigService.SaveSnapshot(repo, "tou-test", json);

        var applied = ModInstanceTouConfigService.TryApplyInstanceConfigToGlobal(repo, "tou-test");
        Assert.True(applied);
        Assert.True(ModInstanceTouConfigService.TryReadGlobalFile(out var read));
        Assert.Equal(json, read);
    }

    private DatabaseService CreateDb()
    {
        var db = new DatabaseService(Path.Combine(_tempDir, Guid.NewGuid().ToString("N"), "susmodder.db"));
        db.InitializeAsync().GetAwaiter().GetResult();
        return db;
    }

    public void Dispose()
    {
        try
        {
            var global = ModInstanceTouConfigService.GetGlobalTouSettingsPath();
            if (File.Exists(global))
                File.Delete(global);
        }
        catch
        {
            // best-effort
        }
    }
}
