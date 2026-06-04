using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Serwis do wczytywania changeloga – z GitHub API (preferred) lub z lokalnego whatsnew.json (fallback).
    /// </summary>
    public class ChangelogService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public ChangelogService()
        {
            _httpClient = new HttpClient();
            var appVersion = new UserSettingsService().LoadAppVersion().CurrentVersion;
            if (string.IsNullOrWhiteSpace(appVersion))
                appVersion = "0.0.0";
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"SUSModder/{appVersion}");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        /// <summary>
        /// Pobiera najnowszy release z GitHub API i parsuje go do ChangelogData.
        /// </summary>
        /// <param name="owner">Właściciel repozytorium (np. "boratsc")</param>
        /// <param name="repo">Nazwa repozytorium (np. "SUSModder")</param>
        /// <returns>ChangelogData lub null jeśli API nie odpowiada</returns>
        public async Task<ChangelogData?> FetchFromGitHubAsync(string owner, string repo)
        {
            try
            {
                var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
                System.Diagnostics.Debug.WriteLine($"[ChangelogService] Pobieranie release z GitHub: {url}");

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChangelogService] GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<GitHubReleaseResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (release == null || string.IsNullOrWhiteSpace(release.Body))
                {
                    System.Diagnostics.Debug.WriteLine("[ChangelogService] GitHub release body is empty");
                    return null;
                }

                var version = release.TagName?.TrimStart('v', 'V') ?? "0.0.0";
                var date = release.PublishedAt ?? release.CreatedAt ?? string.Empty;
                var githubUrl = release.HtmlUrl ?? $"https://github.com/{owner}/{repo}/releases";

                // Wyciągnij datę w formacie YYYY-MM-DD z ISO timestamp
                if (date.Length >= 10)
                    date = date[..10];

                var sections = ParseMarkdownToSections(release.Body);

                return new ChangelogData
                {
                    Version = version,
                    Date = date,
                    Sections = sections,
                    GithubUrl = githubUrl
                };
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChangelogService] GitHub API błąd sieciowy: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChangelogService] GitHub API timeout: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChangelogService] GitHub API nieoczekiwany błąd: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parsuje markdown release body (z GitHub) na listę sekcji changeloga.
        /// Obsługuje format:
        ///   ## ✨ Nowe funkcje
        ///   - Item 1
        ///   - Item 2
        ///   
        ///   ## 🔧 Poprawki
        ///   * Item 1
        /// </summary>
        public static List<ChangelogSection> ParseMarkdownToSections(string markdown)
        {
            var sections = new List<ChangelogSection>();

            if (string.IsNullOrWhiteSpace(markdown))
                return sections;

            var lines = markdown.Split('\n', StringSplitOptions.None);
            ChangelogSection? currentSection = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                // Sekcja: "## tytuł"
                if (line.StartsWith("## "))
                {
                    currentSection = new ChangelogSection();
                    var title = line[3..].Trim();

                    // Wyciągnij ikonę (pierwszy emoji po sekcji)
                    // Ikona może być pierwszym znakiem w tytule
                    var icon = ExtractFirstEmoji(title);
                    if (!string.IsNullOrWhiteSpace(icon))
                    {
                        currentSection.Icon = icon;
                        title = title[icon.Length..].Trim();
                    }

                    currentSection.Title = title;
                    sections.Add(currentSection);
                    continue;
                }

                // Item: "- tekst" lub "* tekst"
                if (currentSection != null && (line.StartsWith("- ") || line.StartsWith("* ")))
                {
                    var item = line[2..].Trim();
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        currentSection.Items.Add(item);
                    }
                    continue;
                }

                // Kontynuacja poprzedniego itemu (wcięty tekst)
                if (currentSection != null && currentSection.Items.Count > 0
                    && !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                {
                    // Jeśli linia nie jest pusta i nie zaczyna się od '#', to kontynuacja
                    // Po prostu pomijamy dla MVP - treść już jest w poprzednim itemie
                }
            }

            return sections;
        }

        /// <summary>
        /// Próbuje wyciągnąć pierwszy emoji z tekstu.
        /// Sprawdza znaki w zakresach Unicode emoji, ignoruje polskie znaki (Latin Extended).
        /// </summary>
        private static string ExtractFirstEmoji(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];

                // Pomijamy spacje
                if (c == ' ')
                    continue;

                // Sprawdź czy to znak w zakresie emoji (a nie Latin Extended/Supplement)
                if (IsEmojiChar(c))
                {
                    // Znajdź koniec emoji (kolejna spacja, ASCII, lub znak spoza zakresu emoji)
                    int j = i + 1;
                    while (j < text.Length)
                    {
                        var next = text[j];
                        if (next == 0x200D || next == 0xFE0F || char.IsSurrogate(next))
                        {
                            j++;
                            continue;
                        }
                        if (!IsEmojiChar(next) || next == ' ' || next <= 0x7F)
                            break;
                        j++;
                    }

                    return text[i..j];
                }

                // Jeśli to znak ASCII lub Latin Extended (polski), nie szukaj dalej
                if (c <= 0x7F || (c >= 0x0080 && c <= 0x024F))
                    return string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Sprawdza czy znak należy do zakresów Unicode emoji.
        /// Nie obejmuje Latin Extended (polskie znaki) ani innych bloków tekstowych.
        /// </summary>
        private static bool IsEmojiChar(char c)
        {
            return
                (c >= 0x2600 && c <= 0x27BF) ||   // Miscellaneous Symbols, Dingbats
                (c >= 0x2300 && c <= 0x23FF) ||   // Miscellaneous Technical
                (c >= 0x2B50 && c <= 0x2B55) ||   // Stars, warning signs
                (c >= 0x2934 && c <= 0x2935) ||   // Arrows (part of)
                (c >= 0x25AA && c <= 0x25FE) ||   // Geometric shapes
                (c >= 0x2B05 && c <= 0x2B55) ||   // More arrows and symbols
                c == 0x2728 ||                     // ✨ Sparkles
                c == 0x274C ||                     // ❌ Cross mark
                c == 0x274E ||                     // ❎ Negative squared cross
                c == 0x2757 ||                     // ❗ Exclamation
                c == 0x2764 ||                     // ❤ Heart
                c == 0x2795 || c == 0x2796 ||      // ➕➖
                c == 0x27A1 ||                     // ➡ Arrow
                c == 0x27B0 ||                     // ➰ Curly loop
                c == 0x27BF ||                     // ➿
                c == 0x3030 || c == 0x303D ||      // Wavy dash
                c == 0x3297 || c == 0x3299 ||      // Japanese symbols
                (c >= 0x1F000 && c <= 0x1FFFF);    // Supplemental Multilingual Plane (most emoji)
        }

        /// <summary>
        /// Sprawdza czy podana wersja jest nowsza niż ostatnia widziana.
        /// Obsługuje semver z prerelease sufiksami (np. "2.5.0-beta").
        /// Zasady: 2.5.0 > 2.5.0-beta (stable > beta tej samej wersji),
        ///         2.5.0-beta > 2.4.0 (wyższy numer wersji).
        /// </summary>
        public bool IsNewerVersion(string currentVersion, string lastSeenVersion)
        {
            if (string.IsNullOrWhiteSpace(lastSeenVersion) || string.IsNullOrWhiteSpace(currentVersion))
                return false;

            if (string.Equals(
                    currentVersion.Trim(),
                    lastSeenVersion.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var current = ParseVersion(currentVersion);
                var lastSeen = ParseVersion(lastSeenVersion);

                if (current == null || lastSeen == null)
                    return string.Compare(currentVersion, lastSeenVersion, StringComparison.Ordinal) > 0;

                // Porównaj major
                if (current.Value.Major != lastSeen.Value.Major)
                    return current.Value.Major > lastSeen.Value.Major;

                // Porównaj minor
                if (current.Value.Minor != lastSeen.Value.Minor)
                    return current.Value.Minor > lastSeen.Value.Minor;

                // Porównaj patch
                if (current.Value.Patch != lastSeen.Value.Patch)
                    return current.Value.Patch > lastSeen.Value.Patch;

                // Ta sama wersja numeryczna - porównaj prerelease:
                // stable > prerelease (2.5.0 > 2.5.0-beta)
                if (current.Value.IsPrerelease != lastSeen.Value.IsPrerelease)
                    return !current.Value.IsPrerelease; // current stable > current prerelease

                // Obie są prerelease lub obie stable - uznaj za tę samą wersję
                return false;
            }
            catch
            {
                // Fallback: porównanie stringowe
                return string.Compare(currentVersion, lastSeenVersion, StringComparison.Ordinal) > 0;
            }
        }

        /// <summary>
        /// Parsuje wersję semver (major.minor.patch).
        /// Obsługuje prefiks 'v' oraz sufiksy prerelease (np. "-beta", "-rc.1").
        /// </summary>
        private static (int Major, int Minor, int Patch, bool IsPrerelease)? ParseVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var clean = version.TrimStart('v', 'V');

            // Rozdziel sufiks prerelease (np. "2.5.0-beta" -> "2.5.0" + "-beta")
            var dashIndex = clean.IndexOf('-');
            if (dashIndex >= 0)
            {
                clean = clean[..dashIndex];
            }

            var parts = clean.Split('.');
            if (parts.Length < 2)
                return null;

            if (!int.TryParse(parts[0], out var major))
                return null;

            if (!int.TryParse(parts[1], out var minor))
                return null;

            var patch = 0;
            if (parts.Length >= 3)
                int.TryParse(parts[2], out patch);

            var isPrerelease = dashIndex >= 0;

            return (major, minor, patch, isPrerelease);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Model odpowiedzi z GitHub API dla pojedynczego release.
    /// GitHub API używa snake_case, więc mapujemy jawnie przez JsonPropertyName.
    /// </summary>
    public class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }
    }
}
