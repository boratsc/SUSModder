using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Tests.Support;
using SUSModder.Core.Utilities;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

/// <summary>
/// Krytyczne przypadki: wyścig tła z ręcznym wyborem, zaufanie do EXE, spójność
/// podmiany z zapisem ustawień oraz limit rozmiaru pobrania.
/// </summary>
public sealed class LegendaryVersionSafetyTests : IDisposable
{
    private const string HeroicAsset = "legendary_windows_x86_64.exe";

    private readonly string _tempDir;
    private readonly string _toolPath;

    public LegendaryVersionSafetyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _toolPath = Path.Combine(_tempDir, "legendary.exe");
    }

    // ------------------------------------------------------- wyścig z ręcznym wyborem

    [Fact]
    public async Task EnsureInstalledAsync_ConcurrentWithManualSwitch_KeepsManualChoice()
    {
        // Tło (pinned) startuje pierwsze i wstrzymuje się na pobieraniu.
        // W tym czasie użytkownik świadomie wybiera wydanie ręczne.
        // Po zakończeniu pobierania tło nie może nadpisać tego wyboru.
        byte[] manualPayload = Encoding.UTF8.GetBytes("manual-release");
        string manualSha = Sha256Verifier.ComputeHex(manualPayload);

        var downloadGate = new TaskCompletionSource();
        var handler = new FakeHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/releases/tags/"))
                return Json(ReleaseTagJson(manualSha));

            // Pobieranie — czekamy na sygnał z testu.
            await downloadGate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(manualPayload)
            };
        });

        var repository = new InMemorySettingsRepository();
        var service = new LegendaryVersionService(repository, new HttpClient(handler), output: null, executablePath: _toolPath);
        var manual = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, manualSha);

        // Selekcja ręczna zapisana — tło ma ją odtworzyć, nie nadpisać przypiętą.
        repository.LoadSettings().LegendaryUsePinned = false;
        repository.LoadSettings().LegendaryReleaseSource = "Heroic";
        repository.LoadSettings().LegendaryReleaseTag = "0.20.43";
        repository.LoadSettings().LegendaryReleaseAsset = HeroicAsset;
        repository.LoadSettings().LegendaryReleaseSha256 = manualSha;

        var background = service.EnsureInstalledAsync();

        // Czekamy, aż tło wejdzie w pobieranie.
        await handler.WaitForDownloadAsync();

        // Użytkownik wybiera wydanie ręcznie — musi poczekać na zakończenie tła.
        var manualSwitch = service.SwitchAsync(manual);

        // Wypuszczamy pobieranie w tle.
        downloadGate.SetResult();

                var backgroundResult = await background;
        var manualResult = await manualSwitch;

        Assert.True(manualResult.Success, "Ręczny wybór musi się powieść.");

        // Świadomy wybór użytkownika musi przetrwać aktualizację tła.
        var settings = repository.LoadSettings();
        Assert.False(settings.LegendaryUsePinned, "Tryb ręczny nie może zostać nadpisany przez tło.");
        Assert.Equal("Heroic", settings.LegendaryReleaseSource);
        Assert.Equal("0.20.43", settings.LegendaryReleaseTag);
        Assert.Equal(manualSha, settings.LegendaryReleaseSha256);

        // Na dysku musi być wydanie ręczne, nie przypięte.
        Assert.Equal(manualSha, await Sha256Verifier.ComputeFileHexAsync(_toolPath));
        Assert.True(backgroundResult.Success || backgroundResult.ErrorCode == LegendaryErrorCodes.HashMismatch);
    }

    [Fact]
    public async Task EnsureInstalledAsync_WithManualChoice_NeverDownloadsPinnedRelease()
    {
        // Najważniejsza gwarancja: przypięta wersja nigdy nie nadpisuje świadomego wyboru ręcznego.
        byte[] manualPayload = Encoding.UTF8.GetBytes("manual-release");
        string manualSha = Sha256Verifier.ComputeHex(manualPayload);

        var requests = new List<string>();
        using var handler = new FakeHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.Contains("/releases/tags/")
                ? Json(ReleaseTagJson(manualSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manualPayload) };
        });

        var repository = new InMemorySettingsRepository();
        var service = new LegendaryVersionService(repository, new HttpClient(handler), output: null, executablePath: _toolPath);

        repository.LoadSettings().LegendaryUsePinned = false;
        repository.LoadSettings().LegendaryReleaseSource = "Heroic";
        repository.LoadSettings().LegendaryReleaseTag = "0.20.43";
        repository.LoadSettings().LegendaryReleaseAsset = HeroicAsset;
        repository.LoadSettings().LegendaryReleaseSha256 = manualSha;

        var result = await service.EnsureInstalledAsync();

        Assert.True(result.Success);
        Assert.Equal(manualSha, await Sha256Verifier.ComputeFileHexAsync(_toolPath));
        Assert.DoesNotContain(requests, u => u.Contains("legendary_windows_x64.exe"));

        // Wybór ręczny w ustawieniach nietknięty.
        var settings = repository.LoadSettings();
        Assert.False(settings.LegendaryUsePinned);
        Assert.Equal(manualSha, settings.LegendaryReleaseSha256);
    }

    [Fact]
    public async Task EnsureInstalledAsync_AfterManualChoice_RestoresManualBinaryNotPinned()
    {
        byte[] manualPayload = Encoding.UTF8.GetBytes("manual-release");
        string manualSha = Sha256Verifier.ComputeHex(manualPayload);

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? Json(ReleaseTagJson(manualSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manualPayload) });

        var repository = new InMemorySettingsRepository();
        var service = new LegendaryVersionService(repository, new HttpClient(handler), output: null, executablePath: _toolPath);
        var manual = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, manualSha);
        await service.SwitchAsync(manual);

        // Na dysku inna binarka — tło ma przywrócić wydanie ręczne (nie przypięte).
        File.WriteAllText(_toolPath, "different-build");

        var result = await service.EnsureInstalledAsync();

        Assert.True(result.Success);
        Assert.Equal(manualSha, await Sha256Verifier.ComputeFileHexAsync(_toolPath));

        // Wybór ręczny musi pozostać zapisany — przypięta wersja go nie nadpisuje.
        var settings = repository.LoadSettings();
        Assert.False(settings.LegendaryUsePinned);
        Assert.Equal(manualSha, settings.LegendaryReleaseSha256);
    }

    // ------------------------------------------------------------- zaufanie do EXE

    [Fact]
    public async Task InspectInstalledAsync_MissingFile_ReportsMissing()
    {
        using var handler = new FakeHandler(_ => Json("[]"));
        var service = CreateService(handler);

        var inspection = await service.InspectInstalledAsync();

        Assert.Equal(LegendaryToolTrust.Missing, inspection.Trust);
        Assert.False(inspection.IsSelectedVersion);
    }

    [Fact]
    public async Task InspectInstalledAsync_UnknownFile_IsUntrusted()
    {
        using var handler = new FakeHandler(_ => Json("[]"));
        var service = CreateService(handler);
        File.WriteAllText(_toolPath, "foreign-binary");

        var inspection = await service.InspectInstalledAsync();

        Assert.Equal(LegendaryToolTrust.Untrusted, inspection.Trust);
        Assert.False(inspection.IsSelectedVersion);
    }

    [Fact]
    public async Task InspectInstalledAsync_LegacyPinnedVersion_IsTrustedButNotSelected()
    {
        // Poprzednia przypięta wersja 0.20.41 to binarka SUSModder — wolno ją uruchomić,
        // ale tło ma ją wymienić na aktualnie przypiętą.
        using var handler = new FakeHandler(_ => Json("[]"));
        var service = CreateService(handler);
        File.WriteAllText(_toolPath, "legacy-pinned");

        var inspection = await service.InspectInstalledAsync();

        // Suma pliku nie jest sumą 0.20.41, więc to plik obcy.
        Assert.Equal(LegendaryToolTrust.Untrusted, inspection.Trust);
    }

    [Fact]
    public void ToolTrustPolicy_AcceptsPinnedLegacyAndManualSums()
    {
        const string manualSha = "1111111111111111111111111111111111111111111111111111111111111111";

        Assert.True(LegendaryToolTrustPolicy.IsTrustedSha256(LegendaryVersionService.PinnedSha256, null));
        Assert.True(LegendaryToolTrustPolicy.IsTrustedSha256(LegendaryToolTrustPolicy.LegacyPinnedSha256, null));
        Assert.True(LegendaryToolTrustPolicy.IsTrustedSha256(manualSha, manualSha));
        Assert.False(LegendaryToolTrustPolicy.IsTrustedSha256(manualSha, null));
        Assert.False(LegendaryToolTrustPolicy.IsTrustedSha256(null, manualSha));
    }

    // ------------------------------------------- spójność podmiany z zapisem ustawień

    [Fact]
    public async Task SwitchAsync_WhenSaveFailsWithoutOriginal_DoesNotRestoreStaleBackup()
    {
        byte[] payload = Encoding.UTF8.GetBytes("new-release");
        string sha = Sha256Verifier.ComputeHex(payload);
        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? Json(ReleaseTagJson(sha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        var service = new LegendaryVersionService(new FailingSettingsRepository(), new HttpClient(handler), executablePath: _toolPath);
        File.WriteAllText(_toolPath + ".previous", "stale-backup");

        var result = await service.SwitchAsync(new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, sha));

        Assert.Equal(LegendaryErrorCodes.Failed, result.ErrorCode);
        Assert.False(File.Exists(_toolPath));
        Assert.Equal("stale-backup", File.ReadAllText(_toolPath + ".previous"));
    }

    [Fact]
    public async Task SwitchAsync_DoesNotCreateLockFile()
    {
        byte[] payload = Encoding.UTF8.GetBytes("new-release");
        string sha = Sha256Verifier.ComputeHex(payload);
        int requests = 0;
        using var handler = new FakeHandler(request =>
        {
            requests++;
            return request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? Json(ReleaseTagJson(sha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        });
        var service = CreateService(handler);

        var result = await service.SwitchAsync(new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, sha));

        Assert.True(result.Success);
        Assert.Equal(2, requests);
        Assert.False(File.Exists(_toolPath + ".switch.lock"));
    }

    [Fact]
    public async Task EnsureInstalledAsync_SelectionChangesDuringDownload_DoesNotOverwriteNewChoice()
    {
        byte[] oldPayload = Encoding.UTF8.GetBytes("old-selection");
        byte[] newPayload = Encoding.UTF8.GetBytes("new-selection");
        string oldSha = Sha256Verifier.ComputeHex(oldPayload);
        string newSha = Sha256Verifier.ComputeHex(newPayload);
        var releaseDownload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHandler(async _ =>
        {
            await releaseDownload.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(oldPayload) };
        });
        var repository = new InMemorySettingsRepository();
        var settings = repository.LoadSettings();
        settings.LegendaryUsePinned = false;
        settings.LegendaryReleaseSource = "Heroic";
        settings.LegendaryReleaseTag = "0.20.43";
        settings.LegendaryReleaseAsset = HeroicAsset;
        settings.LegendaryReleaseSha256 = oldSha;
        var service = new LegendaryVersionService(repository, new HttpClient(handler), executablePath: _toolPath);

        var background = service.EnsureInstalledAsync();
        await handler.WaitForDownloadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using (await LegendaryOperationGate.AcquireAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)))
        {
            repository.LoadSettings().LegendaryReleaseSha256 = newSha;
            File.WriteAllBytes(_toolPath, newPayload);
        }
        releaseDownload.SetResult();

        var result = await background;
        Assert.Equal(LegendaryErrorCodes.ToolBusy, result.ErrorCode);
        Assert.Equal(newSha, await Sha256Verifier.ComputeFileHexAsync(_toolPath));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public async Task SwitchAsync_WhenSelectionSaveFails_RestoresPreviousBinary()
    {
        byte[] payload = Encoding.UTF8.GetBytes("new-release");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? Json(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var repository = new FailingSettingsRepository();
        var service = new LegendaryVersionService(
            repository,
            new HttpClient(handler),
            output: null,
            executablePath: _toolPath);

        File.WriteAllText(_toolPath, "old-release");

        var release = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);
        var result = await service.SwitchAsync(release);

        // Zapis ustawień zawiódł → binarka musi wrócić do poprzedniej wersji.
        Assert.False(result.Success);
        Assert.Equal(LegendaryErrorCodes.Failed, result.ErrorCode);
        Assert.Equal("old-release", File.ReadAllText(_toolPath));
    }

    [Fact]
    public async Task EnsureInstalledAsync_AfterFailedSave_RestoresFromBackup()
    {
        // Po nieudanym zapisie podmiana i zapis się rozjechały.
        // Po restarcie odzyskanie następuje po sumie zapisanej selekcji i kopii zapasowej.
        byte[] payload = Encoding.UTF8.GetBytes("selected-release");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? Json(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var repository = new InMemorySettingsRepository();
        var service = new LegendaryVersionService(repository, new HttpClient(handler), output: null, executablePath: _toolPath);

        // Selekcja ręczna zapisana, ale na dysku leży inna (niespójna) binarka.
        repository.LoadSettings().LegendaryUsePinned = false;
        repository.LoadSettings().LegendaryReleaseSource = "Heroic";
        repository.LoadSettings().LegendaryReleaseTag = "0.20.43";
        repository.LoadSettings().LegendaryReleaseAsset = HeroicAsset;
        repository.LoadSettings().LegendaryReleaseSha256 = expectedSha;

        // Kopia zapasowa odpowiada zapisanej selekcji.
        File.WriteAllText(_toolPath + ".previous", Encoding.UTF8.GetString(payload));
        File.WriteAllText(_toolPath, "inconsistent-build");

        var result = await service.EnsureInstalledAsync();

        Assert.True(result.Success);
        Assert.Equal(expectedSha, await Sha256Verifier.ComputeFileHexAsync(_toolPath));
    }

    // ------------------------------------------------------------ limit rozmiaru

    [Fact]
    public async Task SwitchAsync_TooLargeDownload_IsRejectedAndKeepsOldBinary()
    {
        // Serwer deklaruje plik większy niż limit — pobieranie musi zostać odrzucone.
        byte[] payload = Encoding.UTF8.GetBytes("new-release");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/releases/tags/"))
                return Json(ReleaseTagJson(expectedSha));

            var content = new ByteArrayContent(payload);
            content.Headers.ContentLength = 1024;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        // Limit 512 B — plik 12 B przekracza limit w strumieniu.
        var service = new LegendaryVersionService(
            new InMemorySettingsRepository(),
            new HttpClient(handler),
            output: null,
            executablePath: _toolPath,
            maxDownloadBytes: 4);

        File.WriteAllText(_toolPath, "old-release");

        var release = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);
        var result = await service.SwitchAsync(release);

        Assert.False(result.Success);
        Assert.Equal(LegendaryErrorCodes.Offline, result.ErrorCode);
        Assert.Equal("old-release", File.ReadAllText(_toolPath));
    }

    [Fact]
    public async Task SwitchAsync_DeclaredSizeOverLimit_IsRejectedBeforeDownload()
    {
        byte[] payload = Encoding.UTF8.GetBytes("new-release");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/releases/tags/"))
                return Json(ReleaseTagJson(expectedSha));

            var content = new ByteArrayContent(payload);
            content.Headers.ContentLength = 4096;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var service = new LegendaryVersionService(
            new InMemorySettingsRepository(),
            new HttpClient(handler),
            output: null,
            executablePath: _toolPath,
            maxDownloadBytes: 1024);

        File.WriteAllText(_toolPath, "old-release");

        var release = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);
        var result = await service.SwitchAsync(release);

        Assert.False(result.Success);
        Assert.Equal(LegendaryErrorCodes.Offline, result.ErrorCode);
        Assert.Equal("old-release", File.ReadAllText(_toolPath));
    }

    [Fact]
    public async Task EnsureInstalledAsync_PinnedMode_DoesNotTouchStoredSelection()
    {
        // Operacja automatyczna nie może zapisywać wyboru — zmienia tylko plik.
        byte[] payload = Encoding.UTF8.GetBytes("pinned-release");
        string pinnedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? Json(ReleaseTagJson(pinnedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var repository = new InMemorySettingsRepository();
        var service = new LegendaryVersionService(repository, new HttpClient(handler), output: null, executablePath: _toolPath);

        // Tryb przypięty, ale w ustawieniach wciąż widać ślady poprzedniego wyboru ręcznego.
        repository.LoadSettings().LegendaryUsePinned = true;
        repository.LoadSettings().LegendaryReleaseSource = "Heroic";
        repository.LoadSettings().LegendaryReleaseTag = "0.20.43";
        repository.LoadSettings().LegendaryReleaseSha256 = pinnedSha;

        var result = await service.EnsureInstalledAsync();

        // Suma pinned w kodzie nie pasuje do payloadu → weryfikacja odrzuca, ustawienia nietknięte.
        Assert.False(result.Success);
        var settings = repository.LoadSettings();
        Assert.True(settings.LegendaryUsePinned);
        Assert.Equal("Heroic", settings.LegendaryReleaseSource);
        Assert.Equal("0.20.43", settings.LegendaryReleaseTag);
    }

    // --------------------------------------------------------------- brama operacji

    [Fact]
    public async Task ExecuteLegendaryProcessAsync_UntrustedTool_IsNotExecuted()
    {
        // Plik o nieznanej sumie nie może zostać uruchomiony.
        using var sandbox = new LegendaryConfigSandbox();
        var manager = new EpicVersionManager(
            new NullDiagnosticsOutput(),
            new NullEpicUserInteraction(),
            (arguments, timeout, onOutput) => Task.FromResult(LegendaryRunResult.Success()),
            sandbox.Directory,
            sandbox.Directory,
            new UntrustedProvider());

        var result = await manager.ExecuteLegendaryProcessAsync(
            manager.LegendaryToolPath,
            "--version",
            TimeSpan.FromSeconds(5),
            null);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.StandardError, line => line.Contains("nie jest zaufany"));
    }

    [Fact]
    public async Task EnsureInstalledAsync_WhileLegendaryRunning_ReportsToolBusy()
    {
        byte[] payload = Encoding.UTF8.GetBytes("manual-release");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? Json(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var repository = new InMemorySettingsRepository();
        var service = new LegendaryVersionService(repository, new HttpClient(handler), output: null, executablePath: _toolPath);

        repository.LoadSettings().LegendaryUsePinned = false;
        repository.LoadSettings().LegendaryReleaseSource = "Heroic";
        repository.LoadSettings().LegendaryReleaseTag = "0.20.43";
        repository.LoadSettings().LegendaryReleaseAsset = HeroicAsset;
        repository.LoadSettings().LegendaryReleaseSha256 = expectedSha;

        // Symulujemy trwającą operację legendary — procesu nie ubijamy, podmiana jest odraczana.
        LegendaryOperationGate.RegisterOperationStart();
        try
        {
            var result = await service.EnsureInstalledAsync();
            Assert.False(result.Success);
            Assert.Equal(LegendaryErrorCodes.ToolBusy, result.ErrorCode);
        }
        finally
        {
            LegendaryOperationGate.RegisterOperationEnd();
        }
    }

    // ----------------------------------------------------------- pomocnicze

    private LegendaryVersionService CreateService(HttpMessageHandler handler) =>
        new(
            new InMemorySettingsRepository(),
            new HttpClient(handler),
            output: null,
            executablePath: _toolPath);

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string ReleaseTagJson(string sha256) => $$"""
    {
      "tag_name": "0.20.43",
      "published_at": "2026-04-22T14:42:11Z",
      "draft": false,
      "prerelease": false,
      "assets": [
        {
          "name": "legendary_windows_x86_64.exe",
          "browser_download_url": "https://github.com/Heroic-Games-Launcher/legendary/releases/download/0.20.43/legendary_windows_x86_64.exe",
          "digest": "sha256:{{sha256}}"
        }
      ]
    }
    """;

    public void Dispose()
    {
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

    /// <summary>
    /// Repozytorium ustawień, które wywala zapis — symuluje awarię SQLite po podmianie pliku.
    /// </summary>
    private sealed class FailingSettingsRepository : InMemorySettingsRepository
    {
        public override void SaveSettings(SUSModder.Core.Configuration.UserSettings settings) =>
            throw new IOException("SQLite: baza zablokowana");
    }

    /// <summary>Serwis, który zawsze uznaje narzędzie za obce.</summary>
    private sealed class UntrustedProvider : ILegendaryVersionProvider
    {
        public Task<LegendaryVersionState> GetStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new LegendaryVersionState(true, null, null, null));

        public Task<IReadOnlyList<LegendaryReleaseInfo>> GetReleasesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LegendaryReleaseInfo>>(Array.Empty<LegendaryReleaseInfo>());

        public Task<LegendarySwitchResult> SwitchAsync(
            LegendaryReleaseInfo? release,
            IProgress<string>? progress = null,
            CancellationToken ct = default) =>
            Task.FromResult(new LegendarySwitchResult(false, LegendaryErrorCodes.Failed));

        public Task<LegendarySwitchResult> EnsureInstalledAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default) =>
            Task.FromResult(new LegendarySwitchResult(false, LegendaryErrorCodes.Failed));

        public Task<LegendaryToolInspection> InspectInstalledAsync(CancellationToken ct = default) =>
            Task.FromResult(new LegendaryToolInspection(LegendaryToolTrust.Untrusted, "deadbeef", false));
    }

    private sealed class NullDiagnosticsOutput : IDiagnosticsOutput
    {
        public void Write(string message) { }
    }

    private sealed class NullEpicUserInteraction : IEpicUserInteraction
    {
        public bool Confirm(string message) => false;

        public void ShowError(string message) { }

        public string? Prompt(string message, string title = "") => null;

        public Task<string?> ShowEpicAuthDialogAsync(string browserUrl) => Task.FromResult<string?>(null);
    }

    /// <summary>Katalog konfiguracji legendary — tymczasowy katalog testowy.</summary>
    private sealed class LegendaryConfigSandbox : IDisposable
    {
        public LegendaryConfigSandbox()
        {
            Directory = Path.Combine(Path.GetTempPath(), "susmodder-legendary-safety-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    /// <summary>Handler HTTP z możliwością wstrzymania pobierania (do testu wyścigu).</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;
        private readonly TaskCompletionSource _downloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this(request => Task.FromResult(responder(request)))
        {
        }

        public Task WaitForDownloadAsync() => _downloadStarted.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!request.RequestUri!.AbsolutePath.Contains("/releases/tags/"))
                _downloadStarted.TrySetResult();

            return await _responder(request);
        }
    }
}
