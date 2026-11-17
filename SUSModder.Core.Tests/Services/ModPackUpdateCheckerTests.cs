using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Moq;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public class ModPackUpdateCheckerTests
{
    [Theory]
    [InlineData("{\"a\":1,\"b\":2}", "{\"b\":2,\"a\":1}", false)]
    [InlineData("{\"a\":1}", "{\"a\":2}", true)]
    [InlineData("{\"a\":1}", null, true)]
    public async Task CheckInstance_TouChangeReflectsSnapshot(string remoteJson, string? snapshotJson, bool expectedChange)
    {
        var repository = new Mock<IModInstanceRepository>();
        repository.Setup(r => r.GetConfigs("instance")).Returns(snapshotJson == null
            ? new List<ModInstanceConfig>()
            : new List<ModInstanceConfig> { new() { ConfigType = "tou", ConfigJson = snapshotJson } });
        var checker = CreateChecker(repository, JsonDocument.Parse(remoteJson).RootElement);

        var result = await checker.CheckInstanceAsync(CreateInstance(), TestContext.Current.CancellationToken);

        Assert.Equal(expectedChange, result.Changes.Any(c => c.ChangeType == "config"));
    }

    [Fact]
    public async Task CheckInstance_RemoteWithoutTouConfig_HasNoConfigChange()
    {
        var checker = CreateChecker(new Mock<IModInstanceRepository>(), null);
        var result = await checker.CheckInstanceAsync(CreateInstance(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Changes, c => c.ChangeType == "config");
    }

    [Fact]
    public async Task CheckAllInstances_IncludesOnlyPackSourcedSharedAndCloneInstances()
    {
        var repository = new Mock<IModInstanceRepository>();
        repository.Setup(r => r.GetPackInstances()).Returns(new List<ModInstance>
        {
            CreateInstance(ModInstanceOrigins.Clone, "pack", "clone"),
            CreateInstance(ModInstanceOrigins.Clone, null, "manual-clone"),
            CreateInstance(ModInstanceOrigins.Manual, "pack", "manual"),
            CreateInstance(ModInstanceOrigins.SharedPack, "pack", "shared")
        });
        var packService = new Mock<IModPackService>();
        packService.Setup(s => s.GetPackAsync("pack", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModPack { PackCode = "pack" });
        var checker = new ModPackUpdateChecker(packService.Object, repository.Object, new TestDiagnosticsOutput(), new ConfigService());

        var results = await checker.CheckAllInstancesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "clone", "shared" }, results.Select(r => r.InstanceId));
        Assert.Equal(2, packService.Invocations.Count(i => i.Method.Name == nameof(IModPackService.GetPackAsync)));
    }

    private static ModPackUpdateChecker CreateChecker(Mock<IModInstanceRepository> repository, JsonElement? touConfig)
    {
        var packService = new Mock<IModPackService>();
        packService.Setup(s => s.GetPackAsync("pack", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModPack { PackCode = "pack", TouConfig = touConfig });
        return new ModPackUpdateChecker(packService.Object, repository.Object, new TestDiagnosticsOutput(), new ConfigService());
    }

    private static ModInstance CreateInstance() => CreateInstance(ModInstanceOrigins.SharedPack, "pack", "instance");

    private static ModInstance CreateInstance(string origin, string? packCode, string instanceId) =>
        new() { InstanceId = instanceId, SourcePackCode = packCode, Origin = origin, DisplayName = "test" };
}
