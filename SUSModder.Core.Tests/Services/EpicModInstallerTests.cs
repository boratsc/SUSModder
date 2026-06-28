using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Moq;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Utilities;
using Xunit;

namespace SUSModder.Core.Tests.Services;

public class EpicModInstallerTests : IDisposable
{
    private readonly string _previousModsInstallPath;
    private readonly string _tempDir;

    public EpicModInstallerTests()
    {
        _previousModsInstallPath = PathSettings.ModsInstallPath;
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        PathSettings.SetCustomPath(_tempDir);
    }

    [Fact]
    public async Task InstallAsync_DownloadsAndExtractsEpicPayloadToAmongUsSubdirectory()
    {
        var targetPath = Path.Combine(_tempDir, "MyEpicPack");
        var zipBytes = CreateZipWithFile("Among Us.exe", "epic-game");
        using var server = new SingleResponseHttpServer(zipBytes);

        var mockApi = CreateMockApi(server.Url, zipBytes);
        var previousDefault = SUSModderApiClientProvider.TryGetDefault();
        SUSModderApiClientProvider.SetDefault(mockApi.Object);
        try
        {
            var installer = new EpicModInstaller();
            var progress = new TestProgressReporter();
            var log = new TestDiagnosticsOutput();
            var mod = new ModConfiguration
            {
                Id = 10,
                ModName = "Town of Us",
                ModVersion = "5.5.0"
            };

            var result = await installer.InstallAsync(mod, targetPath, progress, log);

            Assert.True(result.Success);
            var gamePath = Path.Combine(targetPath, "AmongUs");
            Assert.True(File.Exists(Path.Combine(gamePath, "Among Us.exe")));
            Assert.Equal("epic-game", await File.ReadAllTextAsync(Path.Combine(gamePath, "Among Us.exe")));
            Assert.Equal(gamePath, PathSettings.GetActualModPath(targetPath));
            Assert.True(progress.LastPercent >= 0 && progress.LastPercent <= 100);
        }
        finally
        {
            if (previousDefault is null)
                SUSModderApiClientProvider.ResetForTests();
            else
                SUSModderApiClientProvider.SetDefault(previousDefault);
        }
    }

    [Fact]
    public async Task InstallAsync_InvalidZip_ReturnsFailure()
    {
        var targetPath = Path.Combine(_tempDir, "MyEpicPack");
        var invalidBytes = Encoding.UTF8.GetBytes("not-a-zip");
        using var server = new SingleResponseHttpServer(invalidBytes);

        var mockApi = CreateMockApi(server.Url, invalidBytes);
        var previousDefault = SUSModderApiClientProvider.TryGetDefault();
        SUSModderApiClientProvider.SetDefault(mockApi.Object);
        try
        {
            var installer = new EpicModInstaller();
            var progress = new TestProgressReporter();
            var log = new TestDiagnosticsOutput();
            var mod = new ModConfiguration { Id = 10, ModName = "Town of Us", ModVersion = "5.5.0" };

            var result = await installer.InstallAsync(mod, targetPath, progress, log);

            Assert.False(result.Success);
            Assert.False(Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any());
        }
        finally
        {
            if (previousDefault is null)
                SUSModderApiClientProvider.ResetForTests();
            else
                SUSModderApiClientProvider.SetDefault(previousDefault);
        }
    }

    [Fact]
    public async Task InstallAsync_TargetAlreadyAmongUs_DoesNotCreateNestedAmongUsDirectory()
    {
        var targetPath = Path.Combine(_tempDir, "MyEpicPack", "AmongUs");
        var zipBytes = CreateZipWithFile("BepInEx/plugins/mod.dll", "dll");
        using var server = new SingleResponseHttpServer(zipBytes);

        var mockApi = CreateMockApi(server.Url, zipBytes);
        var previousDefault = SUSModderApiClientProvider.TryGetDefault();
        SUSModderApiClientProvider.SetDefault(mockApi.Object);
        try
        {
            var installer = new EpicModInstaller();
            var progress = new TestProgressReporter();
            var log = new TestDiagnosticsOutput();
            var mod = new ModConfiguration { Id = 10, ModName = "Town of Us", ModVersion = "5.5.0" };

            var result = await installer.InstallAsync(mod, targetPath, progress, log);

            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(targetPath, "BepInEx", "plugins", "mod.dll")));
            Assert.False(Directory.Exists(Path.Combine(targetPath, "AmongUs")));
        }
        finally
        {
            if (previousDefault is null)
                SUSModderApiClientProvider.ResetForTests();
            else
                SUSModderApiClientProvider.SetDefault(previousDefault);
        }
    }

    [Fact]
    public async Task InstallAsync_Sha256Mismatch_ReturnsFailure()
    {
        var targetPath = Path.Combine(_tempDir, "MyEpicPack");
        var zipBytes = CreateZipWithFile("Among Us.exe", "epic-game");
        using var server = new SingleResponseHttpServer(zipBytes);

        var mockApi = CreateMockApi(server.Url, zipBytes, expectedSha256: "0000000000000000000000000000000000000000000000000000000000000000");
        var previousDefault = SUSModderApiClientProvider.TryGetDefault();
        SUSModderApiClientProvider.SetDefault(mockApi.Object);
        try
        {
            var installer = new EpicModInstaller();
            var progress = new TestProgressReporter();
            var log = new TestDiagnosticsOutput();
            var mod = new ModConfiguration { Id = 10, ModName = "Town of Us", ModVersion = "5.5.0" };

            var result = await installer.InstallAsync(mod, targetPath, progress, log);

            Assert.False(result.Success);
        }
        finally
        {
            if (previousDefault is null)
                SUSModderApiClientProvider.ResetForTests();
            else
                SUSModderApiClientProvider.SetDefault(previousDefault);
        }
    }

    private static Mock<ISUSModderApiClient> CreateMockApi(string downloadUrl, byte[] body, string? expectedSha256 = null)
    {
        var mockApi = new Mock<ISUSModderApiClient>();
        mockApi.SetupGet(x => x.BaseUrl).Returns("https://api.example/v2");
        mockApi
            .Setup(x => x.GetCatalogModDetailAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SusModderApiResult<CatalogModDetailDto>
            {
                StatusCode = 200,
                Data = new CatalogModDetailDto
                {
                    Id = 10,
                    CurrentVersion = "5.5.0",
                    Variants = new List<CatalogModVariantDto>
                    {
                        new()
                        {
                            Platform = "epic",
                            Architecture = "x64",
                            Version = "5.5.0",
                            Sha256 = expectedSha256 ?? ComputeSha256(body)
                        }
                    }
                }
            });
        mockApi
            .Setup(x => x.BuildModDownloadUrl(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(downloadUrl);
        return mockApi;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] CreateZipWithFile(string fileName, string content)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry(fileName);
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write(content);
        }
        return ms.ToArray();
    }

    public void Dispose()
    {
        PathSettings.SetCustomPath(_previousModsInstallPath);
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private sealed class TestProgressReporter : IProgressReporter
    {
        public int LastPercent { get; private set; }

        public void Report(int percent, string? message = null)
        {
            LastPercent = percent;
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
            Url = $"http://127.0.0.1:{port}/mod.zip";
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
}
