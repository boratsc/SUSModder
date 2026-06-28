using SUSModder.Core.Data;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public class InstanceToModPackMapperTests
{
    private readonly string _tempDir;

    public InstanceToModPackMapperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task Map_BuildsCreateRequestFromInstanceAndDllRows()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new ModInstanceRepository(db);
        var instanceId = Guid.NewGuid().ToString("D");
        repo.AddInstance(new ModInstance
        {
            InstanceId = instanceId,
            DisplayName = "ToU - test",
            BaseModId = 10,
            BaseModName = "Town of Us",
            FullModVersion = "5.4.0",
            InstallPath = Path.Combine(_tempDir, "tou-test"),
            Origin = "manual"
        });
        repo.AddDll(new ModInstanceDll
        {
            InstanceId = instanceId,
            DllModId = 42,
            DllName = "AleLudu",
            DllVersion = "2.0",
            Source = "catalog"
        });

        var mapper = new InstanceToModPackMapper(repo);
        var request = mapper.Map(instanceId, "Shared lobby");

        Assert.Equal(10, request.FullModId);
        Assert.Equal("5.4.0", request.FullModVersion);
        Assert.Equal("Shared lobby", request.ModName);
        Assert.Single(request.DllMods);
        Assert.Equal(42, request.DllMods[0].DllModId);
        Assert.Equal("2.0", request.DllMods[0].DllModVersion);
    }

    private async Task<DatabaseService> CreateInitializedDatabaseAsync()
    {
        var db = new DatabaseService(Path.Combine(_tempDir, Guid.NewGuid().ToString("N"), "susmodder.db"));
        await db.InitializeAsync();
        return db;
    }

}
