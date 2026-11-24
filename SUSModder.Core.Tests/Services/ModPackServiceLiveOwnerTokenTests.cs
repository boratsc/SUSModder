using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Moq;
using SUSModder.Core.Api;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

/// <summary>
/// Testy integracyjne LIVE klienta Windows (ModPackService + SUSModderApiClient + PackOwnerTokenStore)
/// na pełnym stacku LAN (API v2 + PostgreSQL + Redis). Checklista Outline 02.8 dla ownerToken.
///
/// Bramkowane zmienną środowiskową SUSMODDER_LIVE_API_BASE (adres bazowego API v2, np. http://host:port/api/v2).
/// Bez zmiennej wszystkie testy są pomijane (Assert.Skip) — zero wpływu na CI/zwykły przebieg.
/// Wymagają Windows (DPAPI CurrentUser). NIGDY nie używają realnego magazynu użytkownika ani produkcji:
/// każdy test ma izolowany magazyn w temp i losowy creatorHash (64 hex) na przebieg.
///
/// Uwaga dot. HTTP (checklista pkt 6): SUSModderApiClient nie blokuje plaintext HTTP (brak walidacji schematu),
/// ale od decyzji użytkownika 2026-11-24 NIE wysyła nagłówka Authorization po http:// (guard w SendAsync).
/// Testowy transport dodatkowo zdejmuje taki nagłówek, gdyby się pojawił (defense-in-depth), i liczy zdarzenia —
/// licznik musi pozostać 0. Token właściciela jedzie WYŁĄCZNIE w polu creatorHash body/formy;
/// nagłówek X-User-Hash niesie CreatorHash (tożsamość grupującą) i jest przez backend ignorowany przy autoryzacji.
/// </summary>
public class ModPackServiceLiveOwnerTokenTests
{
    private static string LiveBase()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("ownerToken wymaga Windows DPAPI.");
        var value = Environment.GetEnvironmentVariable("SUSMODDER_LIVE_API_BASE");
        if (string.IsNullOrWhiteSpace(value))
            Assert.Skip("Ustaw SUSMODDER_LIVE_API_BASE (bazowy adres API v2), aby uruchomić testy LIVE.");
        return value!.TrimEnd('/');
    }

    // --- Checklista 1: dwa tokeny, trwałość po "restarcie", właściwy token per kod ---
    [Fact]
    public async Task Live_TwoPacks_TokensPersistAcrossRestart_CorrectTokenPerCode()
    {
        var apiBase = LiveBase();
        using var fx = CreateFixture(apiBase);
        var ct = TestContext.Current.CancellationToken;
        var service = fx.CreateService();

        var a = await service.CreatePackAsync(NewRequest("live-A"), ct);
        var b = await service.CreatePackAsync(NewRequest("live-B"), ct);
        Assert.True(a.Success, a.ErrorMessage);
        Assert.True(b.Success, b.ErrorMessage);
        Assert.NotNull(a.OwnerToken);
        Assert.NotNull(b.OwnerToken);
        Assert.Equal(64, a.OwnerToken!.Length);
        Assert.Equal(64, b.OwnerToken!.Length);
        Assert.NotEqual(a.OwnerToken, b.OwnerToken);
        var codeA = a.PackCode!;
        var codeB = b.PackCode!;
        fx.Track(codeA, a.OwnerToken!);
        fx.Track(codeB, b.OwnerToken!);

        // Token zapisany w magazynie przed uploadem/finalizacją.
        Assert.True(PackOwnerTokenStore.TryGet(fx.StorePath, codeA, out var storedA));
        Assert.True(PackOwnerTokenStore.TryGet(fx.StorePath, codeB, out var storedB));
        Assert.Equal(a.OwnerToken, storedA);
        Assert.Equal(b.OwnerToken, storedB);

        // "Restart": NOWA instancja serwisu z tą samą ścieżką magazynu.
        var restarted = fx.CreateService();
        Assert.True(restarted.HasOwnerToken(codeA));
        Assert.True(restarted.HasOwnerToken(codeB));

        // Token paczki A nie usuwa paczki B (serwer egzekwuje per-pack) -> 403.
        var (crossStatus, _) = await fx.RawAsync(HttpMethod.Delete, "/modpacks/" + codeB, new { creatorHash = storedA }, ct);
        Assert.Equal(403, crossStatus);

        // Właściwy token wybrany po restarcie -> 200 (chat hash dałby 403).
        var delB = await restarted.DeletePackDetailedAsync(codeB, ct);
        Assert.True(delB.Success, delB.ErrorMessage);
        Assert.Equal(200, delB.StatusCode);
        var delA = await restarted.DeletePackDetailedAsync(codeA, ct);
        Assert.True(delA.Success, delA.ErrorMessage);

        await fx.CleanupAndAssertEmptyAsync(ct);
        fx.AssertNoTokenInLogs(a.OwnerToken!, b.OwnerToken!);
    }

    // --- Checklista 2: finalize z tokenem -> 200; z hashem czatu -> 403/PACK_NOT_MANAGEABLE ---
    [Fact]
    public async Task Live_Finalize_WithToken_200_WithChatHash_PackNotManageable()
    {
        var apiBase = LiveBase();
        using var fx = CreateFixture(apiBase);
        var ct = TestContext.Current.CancellationToken;
        var owner = fx.CreateService();

        var created = await owner.CreatePackAsync(NewRequest("live-finalize"), ct);
        Assert.True(created.Success, created.ErrorMessage);
        var code = created.PackCode!;
        fx.Track(code, created.OwnerToken!);

        var finToken = await owner.FinalizePackAsync(code, ct);
        Assert.True(finToken.Success, finToken.ErrorMessage);
        Assert.Equal("ready", finToken.Status);

        // Instancja bez tokenu w magazynu -> write path wysyła legacy CreatorHash -> 403 -> PACK_NOT_MANAGEABLE.
        var noToken = fx.CreateService(fx.EmptyStorePath());
        Assert.False(noToken.HasOwnerToken(code));
        var finHash = await noToken.FinalizePackAsync(code, ct);
        Assert.False(finHash.Success);
        Assert.Equal("PACK_NOT_MANAGEABLE", finHash.ErrorCode);

        var del = await owner.DeletePackDetailedAsync(code, ct);
        Assert.True(del.Success, del.ErrorMessage);
        await fx.CleanupAndAssertEmptyAsync(ct);
        fx.AssertNoTokenInLogs(created.OwnerToken!);
    }

    // --- Checklista 3: uszkodzony magazyn -> Unreadable (fail-closed, zero żądań); usunięty -> legacy 403 ---
    [Theory]
    [InlineData("corrupt")]
    [InlineData("delete")]
    public async Task Live_MissingOrUnreadableToken_WriteBehavior(string mode)
    {
        var apiBase = LiveBase();
        using var fx = CreateFixture(apiBase);
        var ct = TestContext.Current.CancellationToken;
        var owner = fx.CreateService();

        var created = await owner.CreatePackAsync(NewRequest("live-missing"), ct);
        Assert.True(created.Success, created.ErrorMessage);
        var code = created.PackCode!;
        var token = created.OwnerToken!;
        fx.Track(code, token);

        if (mode == "corrupt")
            File.WriteAllText(fx.StorePath, "{uszkodzony-json");
        else
            File.Delete(fx.StorePath);

        var amnesiac = fx.CreateService(); // ta sama ścieżka magazynu
        Assert.False(amnesiac.HasOwnerToken(code));

        if (mode == "corrupt")
        {
            // Unreadable: Core NIE wysyła hashu czatu w ciemno — OWNER_TOKEN_UNREADABLE, zero żądań.
            Assert.Equal(OwnerTokenState.Unreadable, amnesiac.GetOwnerTokenState(code));
            var delUnreadable = await amnesiac.DeletePackDetailedAsync(code, ct);
            Assert.False(delUnreadable.Success);
            Assert.Equal("OWNER_TOKEN_UNREADABLE", delUnreadable.ErrorCode);
            var finUnreadable = await amnesiac.FinalizePackAsync(code, ct);
            Assert.False(finUnreadable.Success);
            Assert.Equal("OWNER_TOKEN_UNREADABLE", finUnreadable.ErrorCode);

            // Paczka nietknięta — żądanie nie wyszło.
            var (stillStatus, _) = await fx.RawAsync(HttpMethod.Get, "/modpacks/" + code, null, ct);
            Assert.Equal(200, stillStatus);
        }
        else
        {
            // Missing (legacy): Core wysyła CreatorHash, serwer 403 -> PACK_NOT_MANAGEABLE.
            Assert.Equal(OwnerTokenState.Missing, amnesiac.GetOwnerTokenState(code));
            var del = await amnesiac.DeletePackDetailedAsync(code, ct);
            Assert.False(del.Success);
            Assert.Equal(403, del.StatusCode);
            Assert.Equal("PACK_NOT_MANAGEABLE", del.ErrorCode);

            var fin = await amnesiac.FinalizePackAsync(code, ct);
            Assert.False(fin.Success);
            Assert.Equal("PACK_NOT_MANAGEABLE", fin.ErrorCode);
        }

        // Odzyskanie: właściwy token (spoza magazynu) nadal usuwa paczkę.
        var (recoverStatus, _) = await fx.RawAsync(HttpMethod.Delete, "/modpacks/" + code, new { creatorHash = token }, ct);
        Assert.Equal(200, recoverStatus);
        await fx.CleanupAndAssertEmptyAsync(ct);
        fx.AssertNoTokenInLogs(token);
    }

    // --- Checklista 3b: nagłówek X-User-Hash ignorowany — autoryzuje wyłącznie pole creatorHash w body ---
    [Fact]
    public async Task Live_TokenInBody_ChatHashInHeader_200()
    {
        var apiBase = LiveBase();
        using var fx = CreateFixture(apiBase);
        var ct = TestContext.Current.CancellationToken;
        var service = fx.CreateService();

        var created = await service.CreatePackAsync(NewRequest("live-header"), ct);
        Assert.True(created.Success, created.ErrorMessage);
        var code = created.PackCode!;
        var token = created.OwnerToken!;
        fx.Track(code, token);

        // Token w body + hash czatu w nagłówku -> 200 (gdyby nagłówek autoryzował, byłoby 403).
        var (status, _) = await fx.RawAsync(HttpMethod.Delete, "/modpacks/" + code,
            new { creatorHash = token }, ct, userHash: fx.CreatorHash);
        Assert.Equal(200, status);

        await fx.CleanupAndAssertEmptyAsync(ct);
        fx.AssertNoTokenInLogs(token);
    }

    // --- Checklista 4: legacy pack (POST bez ownershipMode) -> delete z CreatorHash -> 200 ---
    [Fact]
    public async Task Live_LegacyPack_CreateWithoutOwnershipMode_DeleteWithChatHash_200()
    {
        var apiBase = LiveBase();
        using var fx = CreateFixture(apiBase);
        var ct = TestContext.Current.CancellationToken;

        // Windows serializer zawsze dodaje ownershipMode, więc legacy tworzymy bezpośrednim POST bez pola.
        var (status, body) = await fx.RawAsync(HttpMethod.Post, "/modpacks", new
        {
            creatorHash = fx.CreatorHash,
            fullModId = 1,
            fullModVersion = "latest",
            modName = "live-legacy",
            ttlDays = 7
        }, ct);
        Assert.Equal(201, status);
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        var code = data.GetProperty("packCode").GetString()!;
        Assert.False(data.TryGetProperty("ownerToken", out _));
        fx.Track(code, fx.CreatorHash);

        var service = fx.CreateService();
        Assert.False(service.HasOwnerToken(code));
        var pack = await service.GetPackAsync(code, ct);
        Assert.NotNull(pack);
        Assert.Equal("legacy", pack!.OwnershipMode);

        var del = await service.DeletePackDetailedAsync(code, ct);
        Assert.True(del.Success, del.ErrorMessage);
        Assert.Equal(200, del.StatusCode);
        await fx.CleanupAndAssertEmptyAsync(ct);
    }

    // --- Checklista 5: flaga ownershipMode w ListOwnPacksDetailedAsync / GetPackAsync na żywo ---
    [Fact]
    public async Task Live_OwnershipMode_Flags_InListAndGet()
    {
        var apiBase = LiveBase();
        using var fx = CreateFixture(apiBase);
        var ct = TestContext.Current.CancellationToken;
        var service = fx.CreateService();

        var (status, body) = await fx.RawAsync(HttpMethod.Post, "/modpacks", new
        {
            creatorHash = fx.CreatorHash,
            fullModId = 1,
            fullModVersion = "latest",
            modName = "live-flag-legacy",
            ttlDays = 7
        }, ct);
        Assert.Equal(201, status);
        using var doc = JsonDocument.Parse(body);
        var legacyCode = doc.RootElement.GetProperty("data").GetProperty("packCode").GetString()!;
        fx.Track(legacyCode, fx.CreatorHash);

        var owned = await service.CreatePackAsync(NewRequest("live-flag-owned"), ct);
        Assert.True(owned.Success, owned.ErrorMessage);
        var ownedCode = owned.PackCode!;
        fx.Track(ownedCode, owned.OwnerToken!);

        var list = await service.ListOwnPacksDetailedAsync(ct);
        Assert.True(list.Success, list.ErrorMessage);
        var legacyEntry = Assert.Single(list.Packs, p => p.PackCode == legacyCode);
        var ownedEntry = Assert.Single(list.Packs, p => p.PackCode == ownedCode);
        Assert.Equal("legacy", legacyEntry.OwnershipMode);
        Assert.Equal("ownerToken", ownedEntry.OwnershipMode);

        var getLegacy = await service.GetPackAsync(legacyCode, ct);
        var getOwned = await service.GetPackAsync(ownedCode, ct);
        Assert.Equal("legacy", getLegacy!.OwnershipMode);
        Assert.Equal("ownerToken", getOwned!.OwnershipMode);

        await fx.CleanupAndAssertEmptyAsync(ct);
        fx.AssertNoTokenInLogs(owned.OwnerToken!);
    }

    // --- Checklista 6: klient akceptuje http:// (brak blokady schematu); Authorization nie jedzie po HTTP ---
    [Fact]
    public async Task Live_HttpBaseUrl_Accepted_AndNoAuthorizationOverHttp()
    {
        var apiBase = LiveBase();
        using var fx = CreateFixture(apiBase);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(apiBase, fx.Api.BaseUrl);
        var meta = await fx.Api.GetCatalogMetaAsync(cancellationToken: ct);
        Assert.True(meta.IsSuccess, $"catalog-meta HTTP {meta.StatusCode}");

        var service = fx.CreateService();
        var created = await service.CreatePackAsync(NewRequest("live-http"), ct);
        Assert.True(created.Success, created.ErrorMessage);
        fx.Track(created.PackCode!, created.OwnerToken!);

        if (new Uri(apiBase).Scheme == Uri.UriSchemeHttp)
            Assert.Equal(0, fx.AuthOverHttpCount); // guard klienta: Authorization nigdy po plaintext HTTP

        await fx.CleanupAndAssertEmptyAsync(ct);
        fx.AssertNoTokenInLogs(created.OwnerToken!);
    }

    private static ModPackCreateRequest NewRequest(string name) => new()
    {
        FullModId = 1,
        FullModVersion = "latest",
        ModName = name,
        TtlDays = 7
    };

    private static LiveFixture CreateFixture(string apiBase)
    {
        var creatorHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var storeDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", "live", Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(storeDir, "pack-owner-tokens");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = apiBase
            })
            .Build();

        var handler = new StripAuthorizationOverHttpHandler();
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var log = new TestDiagnosticsOutput();
        var api = new SUSModderApiClient(config, log, http);
        return new LiveFixture(apiBase, creatorHash, storePath, config, http, api, log, handler);
    }

    private sealed class LiveFixture : IDisposable
    {
        private readonly List<(string Code, string Credential)> _tracked = new();
        private readonly StripAuthorizationOverHttpHandler _handler;
        private bool _cleaned;

        public string BaseUrl { get; }
        public string CreatorHash { get; }
        public string StorePath { get; }
        public IConfiguration Config { get; }
        public HttpClient Http { get; }
        public SUSModderApiClient Api { get; }
        public TestDiagnosticsOutput Log { get; }
        public int AuthOverHttpCount => _handler.AuthOverHttpCount;

        public LiveFixture(string baseUrl, string creatorHash, string storePath, IConfiguration config,
            HttpClient http, SUSModderApiClient api, TestDiagnosticsOutput log, StripAuthorizationOverHttpHandler handler)
        {
            BaseUrl = baseUrl;
            CreatorHash = creatorHash;
            StorePath = storePath;
            Config = config;
            Http = http;
            Api = api;
            Log = log;
            _handler = handler;
        }

        public ModPackService CreateService(string? storePath = null)
        {
            var hardware = new Mock<IHardwareIdProvider>();
            hardware.Setup(x => x.GetAnonymousUserHash()).Returns(CreatorHash);
            return new ModPackService(Config, Log, hardware.Object, Api, storePath ?? StorePath);
        }

        public string EmptyStorePath() =>
            Path.Combine(Path.GetDirectoryName(StorePath)!, "empty-" + Guid.NewGuid().ToString("N"));

        public void Track(string code, string credential) => _tracked.Add((code, credential));

        public async Task<(int Status, string Body)> RawAsync(HttpMethod method, string path, object? body, CancellationToken ct, string? userHash = null)
        {
            using var request = new HttpRequestMessage(method, BaseUrl + path);
            if (body != null)
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(userHash))
                request.Headers.TryAddWithoutValidation("X-User-Hash", userHash);
            using var response = await Http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            return ((int)response.StatusCode, text);
        }

        public async Task CleanupAndAssertEmptyAsync(CancellationToken ct)
        {
            foreach (var (code, credential) in _tracked)
                await RawAsync(HttpMethod.Delete, "/modpacks/" + code, new { creatorHash = credential }, ct);

            var (status, body) = await RawAsync(HttpMethod.Get, "/modpacks?creatorHash=" + CreatorHash, null, ct);
            Assert.Equal(200, status);
            using var doc = JsonDocument.Parse(body);
            var packs = doc.RootElement.GetProperty("data").GetProperty("packs");
            Assert.Equal(0, packs.GetArrayLength());
            _cleaned = true;
        }

        public void AssertNoTokenInLogs(params string[] tokens)
        {
            var all = string.Join("\n", Log.Messages);
            foreach (var token in tokens)
                if (!string.IsNullOrEmpty(token))
                    Assert.DoesNotContain(token, all);
        }

        public void Dispose()
        {
            if (!_cleaned)
            {
                foreach (var (code, credential) in _tracked)
                {
                    try
                    {
                        RawAsync(HttpMethod.Delete, "/modpacks/" + code, new { creatorHash = credential }, CancellationToken.None)
                            .GetAwaiter().GetResult();
                    }
                    catch { /* best-effort cleanup */ }
                }
            }

            try
            {
                var dir = Path.GetDirectoryName(StorePath);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch { /* best-effort */ }

            Http.Dispose();
        }
    }

    /// <summary>
    /// Testowy transport (defense-in-depth): guard klienta API już nie wysyła Authorization po http://.
    /// Gdyby nagłówek mimo to się pojawił, zdejmujemy go (app token nie trafia do plaintext) i liczymy zdarzenie —
    /// test oczekuje licznika 0. Dla https nic nie zmienia.
    /// </summary>
    private sealed class StripAuthorizationOverHttpHandler : DelegatingHandler
    {
        public int AuthOverHttpCount;

        public StripAuthorizationOverHttpHandler() : base(new HttpClientHandler()) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Scheme == Uri.UriSchemeHttp && request.Headers.Authorization != null)
            {
                request.Headers.Authorization = null;
                AuthOverHttpCount++;
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}