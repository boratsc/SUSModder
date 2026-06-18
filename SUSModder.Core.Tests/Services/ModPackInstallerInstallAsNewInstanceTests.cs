using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

public class ModPackInstallerInstallAsNewInstanceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _previousModsInstallPath;

    public ModPackInstallerInstallAsNewInstanceTests()
    {
        _previousModsInstallPath = PathSettings.ModsInstallPath;
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        PathSettings.SetCustomPath(_tempDir);
    }

    [Fact]
    public async Task InstallPack_AsNewInstance_CreatesInstanceDllRowsAndTouSnapshot()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var testConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://api.susmodder-cdn.ovh/v2"
            })
            .Build();
        var apiClient = new SUSModderApiClient(testConfig, new TestDiagnosticsOutput());
        var modRepo = new ModRepository(db, apiClient);
        ConfigManager.SetRepository(modRepo);
        modRepo.SaveAllMods(new List<ModConfiguration>
        {
            ModInstanceInstallerTestsHelpers.CreateFullMod(),
            ModInstanceInstallerTestsHelpers.CreateDllMod()
        });

        var instanceRepo = new ModInstanceRepository(db);
        var fakeFull = new ModInstanceInstallerTestsHelpers.FakeFullModInstaller();
        var fakeDll = new ModInstanceInstallerTestsHelpers.FakeDllInstaller();
        var instanceInstaller = new ModInstanceInstaller(instanceRepo, fakeFull, fakeDll);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://api.susmodder-cdn.ovh/v2",
                ["Configuration:BaseUrl"] = "https://susmodder.app/",
                ["Configuration:ModPacksEndpoint"] = "/api/mod-packs"
            })
            .Build();

        var configService = new ConfigService();
        var log = new TestDiagnosticsOutput();
        var dllService = new DllModificationService(configService, log);
        var installer = new ModPackInstaller(
            configuration,
            configService,
            dllService,
            log,
            instanceInstaller,
            instanceRepo);

        var pack = new ModPack
        {
            PackCode = "TEST-CODE-1234",
            ModName = "ToU - pack test",
            FullMod = new ModPackFullMod { Id = 10, Version = "5.5.0" },
            DllMods = new[]
            {
                new ModPackDllMod { DllModId = 20, DllModVersion = "2.0" }
            },
            TouConfig = JsonDocument.Parse("{\"roles\":1}").RootElement
        };

        var result = await installer.InstallPackAsync(pack, "steam", displayName: "ToU - shared");

        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.InstanceId));

        var stored = instanceRepo.GetInstance(result.InstanceId!);
        Assert.NotNull(stored);
        Assert.Equal("ToU - shared", stored.DisplayName);
        Assert.Equal("shared_pack", stored.Origin);
        Assert.Equal("TEST-CODE-1234", stored.SourcePackCode);
        Assert.Contains("Town of Us", result.InstalledMods);
        Assert.Contains("AleLuduMod", result.InstalledMods);
        Assert.Contains("ToU config", result.InstalledMods);
        Assert.Single(instanceRepo.GetDlls(result.InstanceId!));
        Assert.Contains(
            instanceRepo.GetConfigs(result.InstanceId!),
            c => string.Equals(c.ConfigType, "tou", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(fakeFull.LastTargetPath);
        Assert.Equal(stored.InstallPath, fakeFull.LastTargetPath);
    }

    [Fact]
    public async Task InstallPack_AsNewInstance_InstallsCleanCustomDllArtifactWithSha256Verification()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var testConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://api.susmodder-cdn.ovh/v2"
            })
            .Build();
        var apiClient = new SUSModderApiClient(testConfig, new TestDiagnosticsOutput());
        var modRepo = new ModRepository(db, apiClient);
        ConfigManager.SetRepository(modRepo);
        modRepo.SaveAllMods(new List<ModConfiguration>
        {
            ModInstanceInstallerTestsHelpers.CreateFullMod()
        });

        var bytes = Encoding.UTF8.GetBytes("clean custom dll bytes");
        using var server = new SingleResponseHttpServer(bytes);
        var instanceRepo = new ModInstanceRepository(db);
        var fakeFull = new ModInstanceInstallerTestsHelpers.FakeFullModInstaller();
        var instanceInstaller = new ModInstanceInstaller(
            instanceRepo,
            fakeFull,
            new ModInstanceInstallerTestsHelpers.FakeDllInstaller());
        var configuration = new ConfigurationBuilder().Build();
        var configService = new ConfigService();
        var log = new TestDiagnosticsOutput();
        var installer = new ModPackInstaller(
            configuration,
            configService,
            new DllModificationService(configService, log),
            log,
            instanceInstaller,
            instanceRepo);
        var pack = new ModPack
        {
            PackCode = "TEST-CODE-1234",
            ModName = "Custom DLL pack",
            FullMod = new ModPackFullMod { Id = 10, Version = "5.5.0" },
            Status = "ready",
            Installable = true,
            CustomArtifacts = new[]
            {
                new ModPackCustomArtifact
                {
                    ArtifactId = "artifact-1",
                    SourceKind = "uploaded_dll",
                    ModType = "dll",
                    FileName = "custom.dll",
                    Sha256 = Sha256Verifier.ComputeHex(bytes),
                    FileSize = bytes.Length,
                    Status = "clean",
                    DownloadUrl = server.Url,
                    DllInstallPath = Path.Combine("BepInEx", "plugins", "Custom")
                }
            }
        };

        var result = await installer.InstallPackAsync(pack, "steam", displayName: "custom artifact");

        Assert.True(result.Success);
        Assert.Contains("custom.dll", result.InstalledMods);
        Assert.NotNull(fakeFull.LastTargetPath);
        var installedPath = Path.Combine(fakeFull.LastTargetPath!, "BepInEx", "plugins", "Custom", "custom.dll");
        Assert.True(File.Exists(installedPath));
        Assert.Equal(bytes, File.ReadAllBytes(installedPath));
    }

    public void Dispose()
    {
        PathSettings.SetCustomPath(_previousModsInstallPath);
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private async Task<DatabaseService> CreateInitializedDatabaseAsync()
    {
        var db = new DatabaseService(Path.Combine(_tempDir, Guid.NewGuid().ToString("N"), "susmodder.db"));
        await db.InitializeAsync();
        return db;
    }
}

internal sealed class SingleResponseHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serverTask;
    private readonly byte[] _body;

    public SingleResponseHttpServer(byte[] body)
    {
        _body = body;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Url = $"http://127.0.0.1:{port}/artifact.dll";
        _serverTask = Task.Run(ServeOnceAsync);
    }

    public string Url { get; }

    private async Task ServeOnceAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            await using var stream = client.GetStream();
            var buffer = new byte[1024];
            _ = await stream.ReadAsync(buffer, _cts.Token);
            var headers = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/octet-stream\r\n" +
                $"Content-Length: {_body.Length}\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(headers, _cts.Token);
            await stream.WriteAsync(_body, _cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        try { _serverTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _cts.Dispose();
    }
}

/// <summary>
/// Współdzielone buildery i fake installery z ModInstanceInstallerTests.
/// </summary>
internal static class ModInstanceInstallerTestsHelpers
{
    public static ModConfiguration CreateFullMod() =>
        new()
        {
            Id = 10,
            ModName = "Town of Us",
            ModType = "full",
            ModVersion = "5.5.0",
            AmongVersion = "2024.6",
            GitHubRepoOrLink = "https://example.test/tou.zip"
        };

    public static ModConfiguration CreateDllMod() =>
        new()
        {
            Id = 20,
            ModName = "AleLuduMod",
            ModType = "dll",
            ModVersion = "2.0",
            DllInstallPath = Path.Combine("BepInEx", "plugins"),
            GitHubRepoOrLink = "https://example.test/aleludu.dll"
        };

    public sealed class FakeFullModInstaller : IFullModInstanceInstaller
    {
        public string? LastTargetPath { get; private set; }

        public Task<ModInstallResult> InstallAsync(
            ModConfiguration modConfig,
            string targetInstallPath,
            string platform,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            ModManagerUserCallbacks userCallbacks,
            Action<string>? onSpeedUpdate = null)
        {
            LastTargetPath = targetInstallPath;
            Directory.CreateDirectory(targetInstallPath);
            File.WriteAllText(Path.Combine(targetInstallPath, "Among Us.exe"), string.Empty);
            return Task.FromResult(ModInstallResult.Succeeded());
        }
    }

    public sealed class FakeDllInstaller : IDllModInstanceInstaller
    {
        public ModConfiguration? LastTargetMod { get; private set; }
        public int InstallCallCount { get; private set; }

        public Task<string?> InstallAsync(ModConfiguration dllMod, ModConfiguration targetMod, string platform)
        {
            InstallCallCount++;
            LastTargetMod = targetMod;
            var targetDir = Path.Combine(targetMod.InstallPath!, dllMod.DllInstallPath ?? string.Empty);
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, $"{dllMod.ModName}.dll");
            File.WriteAllText(targetPath, string.Empty);
            return Task.FromResult<string?>(targetPath);
        }
    }
}
