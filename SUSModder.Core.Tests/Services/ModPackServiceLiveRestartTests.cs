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
/// LIVE dowód PRAWDZIWEGO restartu procesu dla ownerToken (checklista Outline 02.8):
/// Phase1 tworzy dwie paczki i zapisuje tokeny w magazynie DPAPI pod ścieżką z SUSMODDER_LIVE_STORE_PATH;
/// Phase2 czyta ten sam plik w NOWYM procesie (runner uruchamia każdą fazę osobnym `dotnet test`).
/// Runner: SKRYPTY/Test/test-live-owner-token-restart.ps1 (ustawia env, temp store, sprzątanie).
///
/// Bramkowanie: Windows + SUSMODDER_LIVE_API_BASE + SUSMODDER_LIVE_STORE_PATH; brak któregokolwiek → skip.
/// Phase1 NIE sprząta paczek ani magazynu (Phase2 to robi); kody + creatorHash lądują w &lt;store&gt;.phase1.json
/// (BEZ tokenów — tokeny wyłącznie w magazynie DPAPI i nigdy w logach).
/// Magazyn jest izolowany (temp runnera) — realny magazyn użytkownika w %APPDATA% nietknięty.
/// </summary>
public class ModPackServiceLiveRestartTests
{
    private static (string ApiBase, string StorePath) LiveRestartEnv()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("ownerToken wymaga Windows DPAPI.");
        var apiBase = Environment.GetEnvironmentVariable("SUSMODDER_LIVE_API_BASE");
        if (string.IsNullOrWhiteSpace(apiBase))
            Assert.Skip("Ustaw SUSMODDER_LIVE_API_BASE (bazowy adres API v2), aby uruchomić testy LIVE.");
        var storePath = Environment.GetEnvironmentVariable("SUSMODDER_LIVE_STORE_PATH");
        if (string.IsNullOrWhiteSpace(storePath))
            Assert.Skip("Ustaw SUSMODDER_LIVE_STORE_PATH (izolowany magazyn temp) — użyj runnera test-live-owner-token-restart.ps1.");
        return (apiBase!.TrimEnd('/'), storePath!);
    }

    [Fact]
    public async Task Phase1_CreateTwoPacks()
    {
        var (apiBase, storePath) = LiveRestartEnv();
        using var fx = CreateFixture(apiBase, storePath);
        var ct = TestContext.Current.CancellationToken;
        var service = fx.CreateService();

        var a = await service.CreatePackAsync(NewRequest("live-restart-A"), ct);
        var b = await service.CreatePackAsync(NewRequest("live-restart-B"), ct);
        Assert.True(a.Success, a.ErrorMessage);
        Assert.True(b.Success, b.ErrorMessage);
        Assert.NotNull(a.OwnerToken);
        Assert.NotNull(b.OwnerToken);
        Assert.Equal(64, a.OwnerToken!.Length);
        Assert.Equal(64, b.OwnerToken!.Length);
        Assert.NotEqual(a.OwnerToken, b.OwnerToken);

        // Tokeny trwałe w magazynie pod ścieżką runnera (plik DPAPI przeżyje ten proces).
        Assert.True(File.Exists(storePath));
        Assert.True(PackOwnerTokenStore.TryGet(storePath, a.PackCode!, out var storedA));
        Assert.True(PackOwnerTokenStore.TryGet(storePath, b.PackCode!, out var storedB));
        Assert.Equal(a.OwnerToken, storedA);
        Assert.Equal(b.OwnerToken, storedB);

        // Metadane dla Phase2 obok magazynu — BEZ tokenów.
        File.WriteAllText(storePath + ".phase1.json", JsonSerializer.Serialize(new
        {
            creatorHash = fx.CreatorHash,
            codeA = a.PackCode,
            codeB = b.PackCode
        }));

        if (new Uri(apiBase).Scheme == Uri.UriSchemeHttp)
            Assert.Equal(0, fx.AuthOverHttpCount);
        fx.AssertNoTokenInLogs(a.OwnerToken!, b.OwnerToken!);
        // Celowo BEZ sprzątania: paczki i magazyn zostają dla Phase2 (nowy proces).
    }

    [Fact]
    public async Task Phase2_AfterProcessRestart()
    {
        var (apiBase, storePath) = LiveRestartEnv();
        var metaPath = storePath + ".phase1.json";
        Assert.True(File.Exists(metaPath), "Brak <store>.phase1.json — uruchom najpierw Phase1_CreateTwoPacks (runner).");
        using var meta = JsonDocument.Parse(File.ReadAllText(metaPath));
        var creatorHash = meta.RootElement.GetProperty("creatorHash").GetString()!;
        var codeA = meta.RootElement.GetProperty("codeA").GetString()!;
        var codeB = meta.RootElement.GetProperty("codeB").GetString()!;

        using var fx = CreateFixture(apiBase, storePath, creatorHash);
        var ct = TestContext.Current.CancellationToken;

        // NOWY proces (osobne uruchomienie dotnet test przez runnera): odczyt pliku DPAPI z dysku.
        var restarted = fx.CreateService();
        Assert.Equal(OwnerTokenState.Available, restarted.GetOwnerTokenState(codeA));
        Assert.Equal(OwnerTokenState.Available, restarted.GetOwnerTokenState(codeB));
        Assert.True(PackOwnerTokenStore.TryGet(storePath, codeA, out var tokenA));
        Assert.True(PackOwnerTokenStore.TryGet(storePath, codeB, out var tokenB));
        Assert.NotEqual(tokenA, tokenB);
        fx.Track(codeA, tokenA);
        fx.Track(codeB, tokenB);

        // Właściwy token per paczka: token A nie usuwa paczki B -> 403.
        var (crossStatus, _) = await fx.RawAsync(HttpMethod.Delete, "/modpacks/" + codeB, new { creatorHash = tokenA }, ct);
        Assert.Equal(403, crossStatus);

        // Token A wybrany z magazynu po restarcie -> 200.
        var delA = await restarted.DeletePackDetailedAsync(codeA, ct);
        Assert.True(delA.Success, delA.ErrorMessage);
        Assert.Equal(200, delA.StatusCode);

        // (a) Unreadable: izolowana uszkodzona KOPIA magazynu -> fail-closed, ZERO żądań HTTP.
        var corruptCopy = storePath + ".unreadable-copy";
        File.WriteAllText(corruptCopy, "{uszkodzony-json");
        var unreadable = fx.CreateService(corruptCopy);
        Assert.Equal(OwnerTokenState.Unreadable, unreadable.GetOwnerTokenState(codeB));
        var requestsBefore = fx.RequestCount;
        var delU = await unreadable.DeletePackDetailedAsync(codeB, ct);
        Assert.False(delU.Success);
        Assert.Equal("OWNER_TOKEN_UNREADABLE", delU.ErrorCode);
        var finU = await unreadable.FinalizePackAsync(codeB, ct);
        Assert.False(finU.Success);
        Assert.Equal("OWNER_TOKEN_UNREADABLE", finU.ErrorCode);
        Assert.Equal(requestsBefore, fx.RequestCount); // delete/finalize nie wysłały niczego

        // (b) Missing: kod spoza magazynu (pusty magazyn) -> Core wysyła legacy CreatorHash -> 403.
        // Gating UI (blokada akcji bez tokenu) jest pokryty osobno testami UI — Core świadomie nie blokuje.
        var missing = fx.CreateService(storePath + ".empty-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(OwnerTokenState.Missing, missing.GetOwnerTokenState(codeB));
        var delM = await missing.DeletePackDetailedAsync(codeB, ct);
        Assert.False(delM.Success);
        Assert.Equal(403, delM.StatusCode);
        Assert.Equal("PACK_NOT_MANAGEABLE", delM.ErrorCode);

        // Sprzątanie: pozostała paczka B własnym tokenem, potem pusta lista creatorHash z Phase1.
        var delB = await restarted.DeletePackDetailedAsync(codeB, ct);
        Assert.True(delB.Success, delB.ErrorMessage);
        Assert.Equal(200, delB.StatusCode);
        await fx.AssertEmptyPackListAsync(ct);
        fx.AssertNoTokenInLogs(tokenA, tokenB);
    }

    private static ModPackCreateRequest NewRequest(string name) => new()
    {
        FullModId = 1,
        FullModVersion = "latest",
        ModName = name,
        TtlDays = 7
    };

    private static RestartFixture CreateFixture(string apiBase, string storePath, string? creatorHash = null)
    {
        creatorHash ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = apiBase
            })
            .Build();

        var handler = new CountingStripAuthorizationHandler();
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var log = new TestDiagnosticsOutput();
        var api = new SUSModderApiClient(config, log, http);
        return new RestartFixture(apiBase, creatorHash, storePath, config, http, api, log, handler);
    }

    /// <summary>
    /// Fixture bez automagicznego sprzątania w Dispose: magazyn i paczki Phase1 muszą przeżyć proces.
    /// Dispose robi tylko best-effort DELETE paczek już oznaczonych do sprzątania (Phase2 po odczycie tokenów).
    /// Katalog temp należy do runnera.
    /// </summary>
    private sealed class RestartFixture : IDisposable
    {
        private readonly List<(string Code, string Credential)> _tracked = new();
        private readonly CountingStripAuthorizationHandler _handler;

        public string BaseUrl { get; }
        public string CreatorHash { get; }
        public string StorePath { get; }
        public IConfiguration Config { get; }
        public HttpClient Http { get; }
        public SUSModderApiClient Api { get; }
        public TestDiagnosticsOutput Log { get; }
        public int RequestCount => _handler.RequestCount;
        public int AuthOverHttpCount => _handler.AuthOverHttpCount;

        public RestartFixture(string baseUrl, string creatorHash, string storePath, IConfiguration config,
            HttpClient http, SUSModderApiClient api, TestDiagnosticsOutput log, CountingStripAuthorizationHandler handler)
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

        public void Track(string code, string credential) => _tracked.Add((code, credential));

        public async Task<(int Status, string Body)> RawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, BaseUrl + path);
            if (body != null)
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            return ((int)response.StatusCode, text);
        }

        public async Task AssertEmptyPackListAsync(CancellationToken ct)
        {
            var (status, body) = await RawAsync(HttpMethod.Get, "/modpacks?creatorHash=" + CreatorHash, null, ct);
            Assert.Equal(200, status);
            using var doc = JsonDocument.Parse(body);
            var packs = doc.RootElement.GetProperty("data").GetProperty("packs");
            Assert.Equal(0, packs.GetArrayLength());
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
            foreach (var (code, credential) in _tracked)
            {
                try
                {
                    RawAsync(HttpMethod.Delete, "/modpacks/" + code, new { creatorHash = credential }, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                catch { /* best-effort cleanup */ }
            }
            Http.Dispose();
        }
    }

    /// <summary>
    /// Liczy KAŻDE żądanie transportu (dowód "zero żądań" przy Unreadable) i defense-in-depth
    /// zdejmuje Authorization po http:// (guard klienta już to robi; licznik musi pozostać 0).
    /// </summary>
    private sealed class CountingStripAuthorizationHandler : DelegatingHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);
        public int AuthOverHttpCount;

        public CountingStripAuthorizationHandler() : base(new HttpClientHandler()) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            if (request.RequestUri?.Scheme == Uri.UriSchemeHttp && request.Headers.Authorization != null)
            {
                request.Headers.Authorization = null;
                AuthOverHttpCount++;
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
