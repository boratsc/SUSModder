using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;
using Xunit;

namespace SUSModder.Core.Tests.Services;

public class PlatformFullModInstanceInstallerTests
{
    [Fact]
    public async Task InstallAsync_UnknownPlatform_ReturnsNotSupportedError()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://api.example/v2"
            })
            .Build();

        var installer = new PlatformFullModInstanceInstaller(config);
        var mod = new ModConfiguration { Id = 1, ModName = "Test", ModVersion = "1.0" };
        var progress = new NoOpProgressReporter();
        var log = new TestDiagnosticsOutput();

        var result = await installer.InstallAsync(mod, "C:\\Target", "xbox", progress, log, new ModManagerUserCallbacks());

        Assert.False(result.Success);
        Assert.Contains("mod_instance_platform_not_supported", result.ErrorMessage);
    }

    [Fact]
    public async Task InstallAsync_SteamPlatform_DelegatesToSteamInstaller()
    {
        var steam = new FakeInstaller("steam");
        var epic = new FakeInstaller("epic");
        var installer = new PlatformFullModInstanceInstaller(steam, epic);

        var mod = new ModConfiguration { Id = 1, ModName = "Test", ModVersion = "1.0" };
        var result = await installer.InstallAsync(mod, "C:\\Target", "steam", new NoOpProgressReporter(), new TestDiagnosticsOutput(), new ModManagerUserCallbacks());

        Assert.True(result.Success);
        Assert.Equal("steam", steam.LastPlatform);
        Assert.Null(epic.LastPlatform);
    }

    [Fact]
    public async Task InstallAsync_EpicPlatform_DelegatesToEpicInstaller()
    {
        var steam = new FakeInstaller("steam");
        var epic = new FakeInstaller("epic");
        var installer = new PlatformFullModInstanceInstaller(steam, epic);

        var mod = new ModConfiguration { Id = 1, ModName = "Test", ModVersion = "1.0" };
        var result = await installer.InstallAsync(mod, "C:\\Target", "epic", new NoOpProgressReporter(), new TestDiagnosticsOutput(), new ModManagerUserCallbacks());

        Assert.True(result.Success);
        Assert.Equal("epic", epic.LastPlatform);
        Assert.Null(steam.LastPlatform);
    }

    private sealed class FakeInstaller : IFullModInstanceInstaller
    {
        private readonly string _expectedPlatform;
        public string? LastPlatform { get; private set; }

        public FakeInstaller(string expectedPlatform)
        {
            _expectedPlatform = expectedPlatform;
        }

        public Task<ModInstallResult> InstallAsync(
            ModConfiguration modConfig,
            string targetInstallPath,
            string platform,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            ModManagerUserCallbacks userCallbacks,
            Action<string>? onSpeedUpdate = null)
        {
            LastPlatform = platform;
            Assert.Equal(_expectedPlatform, platform);
            return Task.FromResult(ModInstallResult.Succeeded());
        }
    }

    private sealed class NoOpProgressReporter : IProgressReporter
    {
        public void Report(int percent, string? message = null)
        {
        }
    }
}
