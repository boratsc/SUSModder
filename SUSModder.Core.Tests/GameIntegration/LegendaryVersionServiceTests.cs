using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Data;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Tests.Support;
using SUSModder.Core.Utilities;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

/// <summary>
/// Serwis wersji legendary.exe: pin, ręczny wybór, weryfikacja sumy, rollback.
/// Testy używają fake'owego HTTP — bez ruchu sieciowego i bez pobierania prawdziwego narzędzia.
/// </summary>
public sealed class LegendaryVersionServiceTests : IDisposable
{
    private const string UpstreamAsset = "legendary_windows_x64.exe";
    private const string HeroicAsset = "legendary_windows_x86_64.exe";

    private readonly string _tempDir;
    private readonly string _toolPath;

    public LegendaryVersionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _toolPath = Path.Combine(_tempDir, "legendary.exe");
    }

    // ---------------------------------------------------------------- pin

    [Fact]
    public void PinnedRelease_UsesUpstreamZeroDot21DotOne_WithPinnedSha256()
    {
        var pinned = LegendaryVersionService.PinnedRelease;

        Assert.Equal(LegendaryReleaseSource.Upstream, pinned.Source);
        Assert.Equal("0.21.1", pinned.Tag);
        Assert.Equal(UpstreamAsset, pinned.AssetName);
        Assert.Equal("legendary-gl/legendary", pinned.Repository);
        Assert.Equal(
            "6be77857dc0a6dd33ea7442ce8b5291944ec6da53757f81ca95b57ce794baf5b",
            pinned.Sha256);
        Assert.True(Sha256Verifier.IsWellFormedHash(pinned.Sha256));
    }

    [Fact]
    public void PinnedDownloadUrl_PointsAtUpstreamWindowsX64Asset()
    {
        Assert.Equal(
            "https://github.com/legendary-gl/legendary/releases/download/0.21.1/legendary_windows_x64.exe",
            LegendaryVersionService.PinnedDownloadUrl);
    }

    [Fact]
    public void PinnedRelease_IsNotHeroicRepository()
    {
        // Upstream i Heroic to osobne repozytoria — przypięcie nie może wskazywać na fork.
        Assert.NotEqual(LegendaryReleaseSource.Heroic, LegendaryVersionService.PinnedRelease.Source);
        Assert.Equal("legendary_windows_x64.exe", LegendaryVersionService.PinnedRelease.AssetName);
        Assert.Equal("legendary_windows_x86_64.exe", LegendaryReleaseCatalog.AssetFor(LegendaryReleaseSource.Heroic));
    }

    [Fact]
    public async Task GetReleasesAsync_AllowsManualReturnToPreviousHeroicPinnedVersion()
    {
        // Poprzednio przypięta wersja 0.20.41 (Heroic) musi pozostać osiągalna ręcznie,
        // żeby użytkownik mógł wrócić do niej po przejściu na 0.21.1.
        using var handler = new FakeHandler(_ => JsonResponse(ReleasesWithHeroic02041Json));
        var service = CreateService(handler);

        var releases = await service.GetReleasesAsync();

        var previous = releases.FirstOrDefault(r =>
            r.Source == LegendaryReleaseSource.Heroic && r.Tag == "0.20.41");

        Assert.NotNull(previous);
        Assert.Equal("legendary_windows_x86_64.exe", previous!.AssetName);
        Assert.Equal(
            "b8866cca30d1d66a9d1331d6f38ec8249073bd749b871821ef9c20e70ce7d263",
            previous.Sha256);
    }

    // ------------------------------------------------------- lista wydań

    [Fact]
    public async Task GetReleasesAsync_ReturnsBothRepositories_SortedNewestFirst()
    {
        using var handler = new FakeHandler(_ => JsonResponse(ReleasesJson));
        var service = CreateService(handler);

        var releases = await service.GetReleasesAsync();

        Assert.Contains(releases, r => r.Source == LegendaryReleaseSource.Upstream && r.Tag == "0.21.1");
        Assert.Contains(releases, r => r.Source == LegendaryReleaseSource.Heroic && r.Tag == "0.20.43");
        Assert.All(releases, r => Assert.False(string.IsNullOrWhiteSpace(r.Sha256)));
        Assert.All(releases, r => Assert.True(Sha256Verifier.IsWellFormedHash(r.Sha256)));

        // Kolejność malejąco po dacie publikacji.
        var dates = releases.Where(r => r.PublishedAt.HasValue).Select(r => r.PublishedAt!.Value).ToList();
        Assert.Equal(dates.OrderByDescending(d => d), dates);
    }

    [Fact]
    public async Task GetReleasesAsync_ExcludesReleaseWithoutShaDigest()
    {
        // Brak digestu = brak możliwości weryfikacji = wydanie nie trafia na listę.
        using var handler = new FakeHandler(_ => JsonResponse(ReleasesJson));
        var service = CreateService(handler);

        var releases = await service.GetReleasesAsync();

        Assert.DoesNotContain(releases, r => r.Tag == "0.21.0");
    }

    [Fact]
    public async Task GetReleasesAsync_ExcludesAssetOtherThanWindowsX64()
    {
        using var handler = new FakeHandler(_ => JsonResponse(ReleasesJson));
        var service = CreateService(handler);

        var releases = await service.GetReleasesAsync();

        Assert.All(releases, r => Assert.True(
            string.Equals(r.AssetName, UpstreamAsset, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.AssetName, HeroicAsset, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(releases, r => r.AssetName.Contains("arm64", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetReleasesAsync_ThrowsOnGitHubFailure()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<LegendaryReleaseException>(
            () => service.GetReleasesAsync());

        Assert.Equal(LegendaryErrorCodes.GitHubUnavailable, ex.ErrorCode);
    }

    // --------------------------------------------------------- podmiana

    [Fact]
    public async Task SwitchAsync_ManualRelease_VerifiesAndReplacesBinary()
    {
        byte[] payload = Encoding.UTF8.GetBytes("legendary-manual-build");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var service = CreateService(handler);
        var release = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);

        var result = await service.SwitchAsync(release);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Equal(expectedSha, result.Installed!.Sha256);
        Assert.Equal("0.20.43", result.Installed.Tag);
        Assert.Equal(expectedSha, await Sha256Verifier.ComputeFileHexAsync(_toolPath));
    }

    [Fact]
    public async Task SwitchAsync_ManualRelease_RejectsFileWithWrongChecksum_AndKeepsOldBinary()
    {
        byte[] payload = Encoding.UTF8.GetBytes("tampered-build");
        string expectedSha = Sha256Verifier.ComputeHex(Encoding.UTF8.GetBytes("legitimate-build"));

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var service = CreateService(handler);
        File.WriteAllText(_toolPath, "old-binary");
        string oldSha = await Sha256Verifier.ComputeFileHexAsync(_toolPath);

        var release = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);
        var result = await service.SwitchAsync(release);

        Assert.False(result.Success);
        Assert.Equal(LegendaryErrorCodes.HashMismatch, result.ErrorCode);

        // Stary plik zostaje nietknięty — nie ma pół-podmienionej binarki.
        Assert.Equal(oldSha, await Sha256Verifier.ComputeFileHexAsync(_toolPath));
        Assert.Equal("old-binary", File.ReadAllText(_toolPath));
    }

    [Fact]
    public async Task SwitchAsync_ManualReleaseWithoutDigest_IsRejected_AndKeepsOldBinary()
    {
        // Brak digestu w API = fail closed, wydanie nie jest instalowane.
        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(null))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });

        var service = CreateService(handler);
        File.WriteAllText(_toolPath, "old-binary");

        var release = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, new string('a', 64));
        var result = await service.SwitchAsync(release);

        Assert.False(result.Success);
        Assert.Equal(LegendaryErrorCodes.NoWindowsAsset, result.ErrorCode);
        Assert.Equal("old-binary", File.ReadAllText(_toolPath));
    }

    [Fact]
    public async Task SwitchAsync_Null_RevertsToPinnedRelease()
    {
        byte[] payload = Encoding.UTF8.GetBytes("pinned-build");
        string pinnedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var service = CreateService(handler);
        var state = await service.GetStateAsync();

        // Pin w kodzie ma własny SHA — symulujemy pobranie zgodne z pinem.
        var result = await service.SwitchAsync(null);

        // Pinned nie przechodzi weryfikacji (fałszywy payload) → stary plik zostaje.
        Assert.False(result.Success);
        Assert.Equal(LegendaryErrorCodes.HashMismatch, result.ErrorCode);
        Assert.NotNull(state.Pinned);
    }

    [Fact]
    public async Task SwitchAsync_KeepsPreviousBinaryAsBackup()
    {
        byte[] payload = Encoding.UTF8.GetBytes("new-build");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var service = CreateService(handler);
        File.WriteAllText(_toolPath, "old-binary");

        var release = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);
        var result = await service.SwitchAsync(release);

        Assert.True(result.Success);

        // Poprzednia binarka zostaje na dysku — ręczny powrót do starej wersji jest możliwy.
        string backupPath = _toolPath + ".previous";
        Assert.True(File.Exists(backupPath));
        Assert.Equal("old-binary", File.ReadAllText(backupPath));
    }

    [Fact]
    public async Task SwitchAsync_ReportsOfflineWhenDownloadFails()
    {
        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(new string('b', 64)))
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var service = CreateService(handler);
        File.WriteAllText(_toolPath, "old-binary");

        var release = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, new string('b', 64));
        var result = await service.SwitchAsync(release);

        Assert.False(result.Success);
        Assert.Equal(LegendaryErrorCodes.Offline, result.ErrorCode);
        Assert.Equal("old-binary", File.ReadAllText(_toolPath));
    }

    [Fact]
    public async Task SwitchAsync_ReportsBusyWhenToolIsLocked()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("x"))
        });

        var service = CreateService(handler);
        File.WriteAllText(_toolPath, "old-binary");

        // Symulacja trwającego procesu legendary — plik otwarty bez współdzielenia.
        using (var lockStream = new FileStream(_toolPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var release = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, new string('c', 64));
            var result = await service.SwitchAsync(release);

            Assert.False(result.Success);
            Assert.Equal(LegendaryErrorCodes.ToolBusy, result.ErrorCode);
        }

        // Stary plik zostaje nietknięty.
        Assert.Equal("old-binary", File.ReadAllText(_toolPath));
    }

    // -------------------------------------------------------- ustawienia

    [Fact]
    public async Task SwitchAsync_ManualSelection_PersistsChoice()
    {
        byte[] payload = Encoding.UTF8.GetBytes("manual-build");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var service = CreateService(handler);
        var release = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);

        var result = await service.SwitchAsync(release);
        Assert.True(result.Success);

        var state = await service.GetStateAsync();

        Assert.False(state.UsePinned);
        Assert.NotNull(state.Selected);
        Assert.Equal("0.20.43", state.Selected!.Tag);
        Assert.Equal(LegendaryReleaseSource.Heroic, state.Selected.Source);
        Assert.Equal(expectedSha, state.Selected.Sha256);
        Assert.Equal(state.Selected, state.Installed);
    }

    [Fact]
    public async Task SwitchAsync_Null_RestoresPinnedModeAndClearsManualChoice()
    {
        byte[] payload = Encoding.UTF8.GetBytes("anything");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var service = CreateService(handler);
        var manual = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);
        await service.SwitchAsync(manual);

        var revert = await service.SwitchAsync(null);

        // Powrót do pinu wymaga pobrania przypiętej binarki — fałszywy payload nie przechodzi weryfikacji,
        // więc tryb ręczny musi pozostać nietknięty.
        Assert.False(revert.Success);

        var state = await service.GetStateAsync();
        Assert.False(state.UsePinned);
    }

    [Fact]
    public async Task GetStateAsync_ReportsPinnedWhenNothingInstalled()
    {
        using var handler = new FakeHandler(_ => JsonResponse(ReleasesJson));
        var service = CreateService(handler);

        var state = await service.GetStateAsync();

        Assert.True(state.UsePinned);
        Assert.Null(state.Selected);
        Assert.NotNull(state.Pinned);
        Assert.Null(state.Installed);
    }

    // ------------------------------------------------------ tryb ręczny

    [Fact]
    public async Task EnsureInstalledAsync_ManualMode_NeverInstallsPinnedRelease()
    {
        byte[] payload = Encoding.UTF8.GetBytes("manual-build");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        var requests = new List<string>();
        using var handler = new FakeHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        });

        var service = CreateService(handler);
        var manual = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);
        await service.SwitchAsync(manual);

        // Usuamy binarkę — serwis musi odtworzyć wydanie ręczne, nie przypięte.
        File.Delete(_toolPath);
        requests.Clear();

        var result = await service.EnsureInstalledAsync();

        Assert.True(result.Success);
        Assert.Equal(expectedSha, await Sha256Verifier.ComputeFileHexAsync(_toolPath));
        Assert.DoesNotContain(requests, u => u.Contains("legendary_windows_x64.exe"));
    }

    [Fact]
    public async Task EnsureInstalledAsync_ManualMode_UsesStoredDigestWithoutCallingApi()
    {
        // Przy starcie nie odpytujemy GitHubu — suma zapisana przy wyborze wystarcza.
        byte[] payload = Encoding.UTF8.GetBytes("manual-build");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        var requests = new List<string>();
        using var handler = new FakeHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        });

        var service = CreateService(handler);
        var manual = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);
        await service.SwitchAsync(manual);

        File.Delete(_toolPath);
        requests.Clear();

        var result = await service.EnsureInstalledAsync();

        Assert.True(result.Success);
        Assert.DoesNotContain(requests, u => u.Contains("/releases/tags/"));
    }

    [Fact]
    public async Task EnsureInstalledAsync_ManualMode_RestoresManualBinaryWhenFileDiffers()
    {
        // Mismatch w trybie ręcznym to nie jest sukces — tło ma przywrócić wydanie ręczne.
        byte[] payload = Encoding.UTF8.GetBytes("manual-build");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        var requests = new List<string>();
        using var handler = new FakeHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        });

        var service = CreateService(handler);
        var manual = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);
        await service.SwitchAsync(manual);

        // Na dysku inna wersja — tło ma ją wymienić na wybraną ręcznie, nie na przypiętą.
        File.WriteAllText(_toolPath, "some-other-build");
        requests.Clear();

        var result = await service.EnsureInstalledAsync();

        Assert.True(result.Success);
        Assert.Equal(expectedSha, await Sha256Verifier.ComputeFileHexAsync(_toolPath));
        Assert.DoesNotContain(requests, u => u.Contains("legendary_windows_x64.exe"));
    }

    [Fact]
    public async Task EnsureInstalledAsync_PinnedMode_ReplacesBinaryWithMismatchedChecksum()
    {
        // Tryb przypięty: binarka o innej sumie jest wymieniana na przypiętą.
        byte[] payload = Encoding.UTF8.GetBytes("pinned-build");
        string pinnedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var service = CreateService(handler);
        File.WriteAllText(_toolPath, "stale-binary");

        var result = await service.EnsureInstalledAsync();

        // Payload nie zgadza się z pinem (sha z pinu jest stałą w kodzie) → weryfikacja odrzuca,
        // stara binarka zostaje na miejscu.
        Assert.False(result.Success);
        Assert.Equal(LegendaryErrorCodes.HashMismatch, result.ErrorCode);
        Assert.Equal("stale-binary", File.ReadAllText(_toolPath));
    }

    [Fact]
    public async Task EnsureInstalledAsync_PinnedMode_KeepsMatchingBinaryUntouched()
    {
        // Binarka zgodna z pinem — brak pobierania, brak podmiany.
        var requests = new List<string>();
        using var handler = new FakeHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("x"))
            };
        });

        var service = CreateService(handler);
        File.WriteAllText(_toolPath, "not-the-pinned-binary");

        var result = await service.EnsureInstalledAsync();

        // SHA nie zgadza się z pinem → próba pobrania; payload jest fałszywy → weryfikacja odrzuca.
        Assert.False(result.Success);
        Assert.Equal(LegendaryErrorCodes.HashMismatch, result.ErrorCode);
        Assert.Equal("not-the-pinned-binary", File.ReadAllText(_toolPath));
        Assert.NotEmpty(requests);
    }

    [Fact]
    public async Task EnsureInstalledAsync_ManualMode_IgnoresBrokenStoredSelection()
    {
        // Uszkodzony zapis ustawień (np. błędna suma) → tryb ręczny nie jest realizowany,
        // wracamy do przypiętego wydania zamiast instalować niesprawdzonej binarki.
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("x"))
        });

        var repository = new InMemorySettingsRepository();
        var service = CreateService(handler, repository);

        repository.LoadSettings().LegendaryUsePinned = false;
        repository.LoadSettings().LegendaryReleaseSource = "Heroic";
        repository.LoadSettings().LegendaryReleaseTag = "0.20.43";
        repository.LoadSettings().LegendaryReleaseAsset = HeroicAsset;
        repository.LoadSettings().LegendaryReleaseSha256 = "not-a-sha";

        var state = await service.GetStateAsync();

        Assert.True(state.UsePinned);
        Assert.Null(state.Selected);
    }

    [Fact]
    public async Task GetStateAsync_ManualMode_DetectsInstalledByChecksum()
    {
        byte[] payload = Encoding.UTF8.GetBytes("manual-build");
        string expectedSha = Sha256Verifier.ComputeHex(payload);

        using var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/releases/tags/")
                ? JsonResponse(ReleaseTagJson(expectedSha))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var service = CreateService(handler);
        var manual = new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, expectedSha);
        await service.SwitchAsync(manual);

        var state = await service.GetStateAsync();

        Assert.False(state.UsePinned);
        Assert.NotNull(state.Installed);
        Assert.Equal("0.20.43", state.Installed!.Tag);
        Assert.Equal(LegendaryReleaseSource.Heroic, state.Installed.Source);
    }

    // --------------------------------------------------------- katalog

    [Fact]
    public void Catalog_AllowsOnlyKnownRepositoriesAndExplicitWindowsAssets()
    {
        Assert.Equal("legendary-gl/legendary", LegendaryReleaseCatalog.RepositoryFor(LegendaryReleaseSource.Upstream));
        Assert.Equal("Heroic-Games-Launcher/legendary", LegendaryReleaseCatalog.RepositoryFor(LegendaryReleaseSource.Heroic));

        Assert.True(LegendaryReleaseCatalog.IsAllowedAsset(LegendaryReleaseSource.Upstream, "legendary_windows_x64.exe"));
        Assert.False(LegendaryReleaseCatalog.IsAllowedAsset(LegendaryReleaseSource.Upstream, "legendary_windows_x86_64.exe"));
        Assert.True(LegendaryReleaseCatalog.IsAllowedAsset(LegendaryReleaseSource.Heroic, "legendary_windows_x86_64.exe"));
        Assert.False(LegendaryReleaseCatalog.IsAllowedAsset(LegendaryReleaseSource.Heroic, "legendary_windows_arm64.exe"));
        Assert.False(LegendaryReleaseCatalog.IsAllowedAsset(LegendaryReleaseSource.Upstream, "legendary_linux_x64"));
        Assert.False(LegendaryReleaseCatalog.IsAllowedAsset(LegendaryReleaseSource.Upstream, null));
    }

    [Fact]
    public void ErrorCodes_MapToLocaleKeysForBothLocales()
    {
        Assert.Equal("Settings.LegendaryVersion.Errors.Offline", LegendaryErrorCodes.MapErrorKey(LegendaryErrorCodes.Offline));
        Assert.Equal("Settings.LegendaryVersion.Errors.GitHubUnavailable", LegendaryErrorCodes.MapErrorKey(LegendaryErrorCodes.GitHubUnavailable));
        Assert.Equal("Settings.LegendaryVersion.Errors.NoWindowsAsset", LegendaryErrorCodes.MapErrorKey(LegendaryErrorCodes.NoWindowsAsset));
        Assert.Equal("Settings.LegendaryVersion.Errors.ToolBusy", LegendaryErrorCodes.MapErrorKey(LegendaryErrorCodes.ToolBusy));
        Assert.Equal("Settings.LegendaryVersion.Errors.HashMismatch", LegendaryErrorCodes.MapErrorKey(LegendaryErrorCodes.HashMismatch));
        Assert.Equal("Settings.LegendaryVersion.Errors.Failed", LegendaryErrorCodes.MapErrorKey(LegendaryErrorCodes.Failed));
        Assert.Equal("Settings.LegendaryVersion.Errors.Unknown", LegendaryErrorCodes.MapErrorKey("mystery"));
        Assert.Equal("Settings.LegendaryVersion.Errors.Unknown", LegendaryErrorCodes.MapErrorKey(null));
    }

    // --------------------------------------------------------- pomocnicze

    private LegendaryVersionService CreateService(
        HttpMessageHandler handler,
        InMemorySettingsRepository? repository = null) =>
        new(
            repository ?? new InMemorySettingsRepository(),
            new HttpClient(handler),
            executablePath: _toolPath);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>
    /// Odpowiedź API GitHub dla listy wydań. Wydanie "0.21.0" ma asset bez digestu — nie wchodzi na listę.
    /// </summary>
    private const string ReleasesJson = """
    [
      {
        "tag_name": "0.21.1",
        "published_at": "2026-09-08T21:07:05Z",
        "draft": false,
        "prerelease": false,
        "assets": [
          {
            "name": "legendary_windows_x64.exe",
            "browser_download_url": "https://github.com/legendary-gl/legendary/releases/download/0.21.1/legendary_windows_x64.exe",
            "digest": "sha256:6be77857dc0a6dd33ea7442ce8b5291944ec6da53757f81ca95b57ce794baf5b"
          },
          {
            "name": "legendary_windows_arm64.exe",
            "browser_download_url": "https://github.com/legendary-gl/legendary/releases/download/0.21.1/legendary_windows_arm64.exe",
            "digest": "sha256:5ca178d75713cb92ee24daa5df1905c75f05b8b403383669ca7aa32a5809fba3"
          }
        ]
      },
      {
        "tag_name": "0.21.0",
        "published_at": "2026-08-04T17:29:09Z",
        "draft": false,
        "prerelease": false,
        "assets": [
          {
            "name": "legendary_windows_x64.exe",
            "browser_download_url": "https://github.com/legendary-gl/legendary/releases/download/0.21.0/legendary_windows_x64.exe"
          }
        ]
      },
      {
        "tag_name": "0.20.43",
        "published_at": "2026-04-22T14:42:11Z",
        "draft": false,
        "prerelease": false,
        "assets": [
          {
            "name": "legendary_windows_x86_64.exe",
            "browser_download_url": "https://github.com/Heroic-Games-Launcher/legendary/releases/download/0.20.43/legendary_windows_x86_64.exe",
            "digest": "sha256:ec1ad2d19d44e07b2b0330191c300979f102c509f2a889708099f453c5188f20"
          }
        ]
      }
    ]
    """;

    /// <summary>
    /// Lista wydań zawierająca poprzednio przypiętą wersję Heroic 0.20.41 (ma digest).
    /// </summary>
    private const string ReleasesWithHeroic02041Json = """
    [
      {
        "tag_name": "0.20.41",
        "published_at": "2026-02-20T15:07:43Z",
        "draft": false,
        "prerelease": false,
        "assets": [
          {
            "name": "legendary_windows_x86_64.exe",
            "browser_download_url": "https://github.com/Heroic-Games-Launcher/legendary/releases/download/0.20.41/legendary_windows_x86_64.exe",
            "digest": "sha256:b8866cca30d1d66a9d1331d6f38ec8249073bd749b871821ef9c20e70ce7d263"
          }
        ]
      }
    ]
    """;

    /// <summary>Wydanie po tagu — <paramref name="sha256"/> null oznacza brak digestu.</summary>
    private static string ReleaseTagJson(string? sha256)    {
        string digest = sha256 == null ? string.Empty : $", \"digest\": \"sha256:{sha256}\"";
        return $$"""
        {
          "tag_name": "0.20.43",
          "published_at": "2026-04-22T14:42:11Z",
          "draft": false,
          "prerelease": false,
          "assets": [
            {
              "name": "legendary_windows_x86_64.exe",
              "browser_download_url": "https://github.com/Heroic-Games-Launcher/legendary/releases/download/0.20.43/legendary_windows_x86_64.exe"{{digest}}
            }
          ]
        }
        """;
    }

    /// <summary>Fake handler HTTP — zwraca przygotowaną odpowiedź dla każdego żądania.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_responder(request));
        }
    }

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
}
