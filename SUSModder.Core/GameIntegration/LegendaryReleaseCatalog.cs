using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.GameIntegration
{
    /// <summary>
    /// Katalog dozwolonych wydań legendary.exe.
    /// </summary>
    /// <remarks>
    /// Dozwolone są wyłącznie dwa repozytoria i wyłącznie ich jawne assety Windows x64.
    /// Asset pobieramy tylko, gdy API GitHub zwróciło dla niego digest sha256 —
    /// brak digestu oznacza odmowę (fail closed), bo wtedy nie mamy skąd wziąć sumy kontrolnej.
    /// </remarks>
    public static class LegendaryReleaseCatalog
    {
        /// <summary>Upstream legendary (oryginalne repozytorium projektu).</summary>
        public const string UpstreamRepository = "legendary-gl/legendary";

        /// <summary>Wydania utrzymywane przez Heroic.</summary>
        public const string HeroicRepository = "Heroic-Games-Launcher/legendary";

        /// <summary>Asset Windows x64 w repozytorium upstream (nazwa inna niż w Heroic!).</summary>
        public const string UpstreamWindowsAsset = "legendary_windows_x64.exe";

        /// <summary>Asset Windows x64 w repozytorium Heroic.</summary>
        public const string HeroicWindowsAsset = "legendary_windows_x86_64.exe";

        /// <summary>Repozytorium dla danego źródła.</summary>
        public static string RepositoryFor(LegendaryReleaseSource source) =>
            source == LegendaryReleaseSource.Upstream ? UpstreamRepository : HeroicRepository;

        /// <summary>Dozwolony asset Windows x64 dla danego źródła.</summary>
        public static string AssetFor(LegendaryReleaseSource source) =>
            source == LegendaryReleaseSource.Upstream ? UpstreamWindowsAsset : HeroicWindowsAsset;

        /// <summary>Czy asset jest dozwolonym assetem Windows x64 danego źródła.</summary>
        public static bool IsAllowedAsset(LegendaryReleaseSource source, string? assetName) =>
            !string.IsNullOrWhiteSpace(assetName) &&
            string.Equals(assetName, AssetFor(source), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Wydanie wraz z assetem Windows x64 (dane wewnętrzne serwisu — UI dostaje <see cref="LegendaryReleaseInfo"/>).
    /// </summary>
    internal sealed record LegendaryReleaseAsset(
        LegendaryReleaseSource Source,
        string Tag,
        string AssetName,
        string Sha256,
        string DownloadUrl,
        DateTimeOffset? PublishedAt);

    /// <summary>
    /// Klient API GitHub dla wydań legendary.
    /// </summary>
    /// <remarks>
    /// Odpowiedzialny wyłącznie za odczyt metadanych (tag, digest, URL assetu).
    /// Nie pobiera ani nie uruchamia narzędzia.
    /// </remarks>
    internal sealed class LegendaryReleaseClient
    {
        private const string ApiBase = "https://api.github.com/repos/";

        private readonly System.Net.Http.HttpClient _http;
        private readonly IDiagnosticsOutput? _output;

        public LegendaryReleaseClient(System.Net.Http.HttpClient http, IDiagnosticsOutput? output = null)
        {
            _http = http;
            _output = output;
        }

        /// <summary>
        /// Wydania z obu dozwolonych repozytoriów, scalone i posortowane od najnowszych.
        /// </summary>
        public async Task<IReadOnlyList<LegendaryReleaseAsset>> ListAsync(CancellationToken ct)
        {
            var all = new List<LegendaryReleaseAsset>();

            foreach (var source in new[] { LegendaryReleaseSource.Upstream, LegendaryReleaseSource.Heroic })
            {
                string url = $"{ApiBase}{LegendaryReleaseCatalog.RepositoryFor(source)}/releases?per_page=30";
                string json = await GetStringAsync(url, ct);

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        var asset = ParseRelease(element, source);
                        if (asset != null)
                            all.Add(asset);
                    }
                }
                catch (JsonException)
                {
                    // Niepoprawna odpowiedź API = brak wiarygodnych metadanych = brak wydań z tego repo.
                    _output?.Write($"[Legendary] Niepoprawna odpowiedź API GitHub dla {source}.");
                }
            }

            return all
                .OrderByDescending(a => a.PublishedAt ?? DateTimeOffset.MinValue)
                .ThenBy(a => a.Tag, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Pojedyncze wydanie (po tagu) z assetem Windows x64 i digestem sha256.
        /// Zwraca null, gdy wydania nie ma, nie ma dozwolonego assetu albo brak digestu.
        /// </summary>
        public async Task<LegendaryReleaseAsset?> GetByTagAsync(
            LegendaryReleaseSource source,
            string tag,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            string url = $"{ApiBase}{LegendaryReleaseCatalog.RepositoryFor(source)}/releases/tags/{Uri.EscapeDataString(tag)}";
            string json = await GetStringAsync(url, ct);

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                return ParseRelease(doc.RootElement, source);
            }
            catch (JsonException)
            {
                // Niepoprawna odpowiedź API = brak wiarygodnych metadanych = odmowa (fail closed).
                _output?.Write("[Legendary] Niepoprawna odpowiedź API GitHub dla wydania.");
                return null;
            }
        }

        private async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _output?.Write($"[Legendary] API GitHub zwróciło {(int)response.StatusCode} dla {url}");
                throw new LegendaryReleaseException(
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden
                        ? LegendaryErrorCodes.GitHubUnavailable
                        : LegendaryErrorCodes.GitHubUnavailable,
                    $"GitHub API: {(int)response.StatusCode}");
            }

            return await response.Content.ReadAsStringAsync(ct);
        }

        /// <summary>
        /// Parsuje obiekt wydania. Odrzuca drafty, prerelease i wydania bez digestu.
        /// </summary>
        private static LegendaryReleaseAsset? ParseRelease(JsonElement element, LegendaryReleaseSource source)
        {
            if (element.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                return null;

            if (element.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True)
                return null;

            string? tag = element.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(tag))
                return null;

            DateTimeOffset? publishedAt = null;
            if (element.TryGetProperty("published_at", out var published) &&
                published.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(published.GetString(), out var parsed))
            {
                publishedAt = parsed;
            }

            if (!element.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            string expectedAsset = LegendaryReleaseCatalog.AssetFor(source);
            string repository = LegendaryReleaseCatalog.RepositoryFor(source);

            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;

                if (!string.Equals(name, expectedAsset, StringComparison.OrdinalIgnoreCase))
                    continue;

                string? digest = asset.TryGetProperty("digest", out var digestElement)
                    ? digestElement.GetString()
                    : null;

                if (!TryParseSha256Digest(digest, out var sha256))
                    return null; // brak wiarygodnej sumy kontrolnej -> fail closed

                string? downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(downloadUrl) ||
                    !downloadUrl.StartsWith($"https://github.com/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return new LegendaryReleaseAsset(
                    source,
                    tag!,
                    expectedAsset,
                    sha256!,
                    downloadUrl!,
                    publishedAt);
            }

            return null;
        }

        private static bool TryParseSha256Digest(string? digest, out string sha256)
        {
            sha256 = string.Empty;

            if (string.IsNullOrWhiteSpace(digest))
                return false;

            const string prefix = "sha256:";
            if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string value = digest[prefix.Length..].Trim();
            if (!Utilities.Sha256Verifier.IsWellFormedHash(value))
                return false;

            sha256 = value.ToLowerInvariant();
            return true;
        }
    }
}
