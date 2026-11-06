using System;
using System.IO;
using System.Text.Json;

namespace SUSModder.Core.GameIntegration
{
    /// <summary>
    /// Stan wpisu aplikacji w <c>installed.json</c> legendary.
    /// </summary>
    public enum LegendaryInstallPathState
    {
        /// <summary>Wpis istnieje i zawiera ścieżkę instalacji.</summary>
        Found,

        /// <summary>
        /// Brak wpisu dla aplikacji (pliku nie ma albo nie zawiera tej aplikacji).
        /// </summary>
        Missing,

        /// <summary>
        /// Stan nieznany: pliku nie da się odczytać, JSON jest uszkodzony albo wpis nie ma
        /// poprawnego pola <c>install_path</c>. Stan traktujemy jako niepewny (fail-closed).
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Odczyt adresów CDN (<c>base_urls</c>) z konfiguracji legendary.
    /// legendary czyści to pole przy każdym odświeżeniu metadanych Epic (nowa wersja gry, np. po aktualizacji
    /// Among Us), a instalacja z lokalnym manifestem nie odpytuje API CDN - kończy się wtedy błędem
    /// "No base URLs found, please try again.".
    /// </summary>
    public static class LegendaryBaseUrlState
    {
        private const string LegendaryConfigPathVariable = "LEGENDARY_CONFIG_PATH";
        private const string XdgConfigHomeVariable = "XDG_CONFIG_HOME";

        /// <summary>
        /// Katalog konfiguracji legendary - taka sama kolejność jak w legendary (lfs/lgndry.py).
        /// </summary>
        public static string ResolveConfigDirectory()
            => ResolveConfigDirectory(
                Environment.GetEnvironmentVariable(LegendaryConfigPathVariable),
                Environment.GetEnvironmentVariable(XdgConfigHomeVariable),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        public static string ResolveConfigDirectory(string? legendaryConfigPath, string? xdgConfigHome, string? userProfile)
        {
            if (!string.IsNullOrWhiteSpace(legendaryConfigPath))
                return legendaryConfigPath!;

            if (!string.IsNullOrWhiteSpace(xdgConfigHome))
                return Path.Combine(xdgConfigHome!, "legendary");

            return Path.Combine(userProfile ?? string.Empty, ".config", "legendary");
        }

        /// <summary>
        /// Sprawdza, czy legendary ma zapisane adresy CDN dla aplikacji - w metadanych gry
        /// (<c>metadata/&lt;appId&gt;.json</c>) lub w <c>installed.json</c>.
        /// </summary>
        public static bool HasBaseUrls(string configDirectory, string appId)
        {
            if (string.IsNullOrWhiteSpace(configDirectory) || string.IsNullOrWhiteSpace(appId))
                return false;

            try
            {
                string metadataFile = Path.Combine(configDirectory, "metadata", $"{appId}.json");
                if (File.Exists(metadataFile) && HasBaseUrlsInJson(File.ReadAllText(metadataFile), appId: null))
                    return true;

                string installedFile = Path.Combine(configDirectory, "installed.json");
                if (File.Exists(installedFile) && HasBaseUrlsInJson(File.ReadAllText(installedFile), appId))
                    return true;
            }
            catch (IOException)
            {
                // brak dostępu do plików konfiguracji - traktujemy jak brak informacji
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (JsonException)
            {
            }

            return false;
        }

        /// <summary>
        /// Sprawdza, czy adresy CDN dla aplikacji istnieją wyłącznie w <c>installed.json</c>
        /// (w metadanych gry ich nie ma). Uninstall usuwa wpis z <c>installed.json</c> wraz z zapisanymi
        /// tam adresami - bez kopii w metadanych zniknęłyby bezpowrotnie.
        /// </summary>
        /// <remarks>
        /// Fail-closed: gdy nie da się odczytać pliku (brak dostępu, uszkodzony JSON), zwracamy
        /// <c>true</c> - stan jest wtedy niepewny i nie wolno usuwać jedynej możliwej kopii adresów.
        /// </remarks>
        public static bool HasBaseUrlsOnlyInInstalledJson(string configDirectory, string appId)
        {
            if (string.IsNullOrWhiteSpace(configDirectory) || string.IsNullOrWhiteSpace(appId))
                return true;

            string metadataFile;
            string installedFile;
            try
            {
                metadataFile = Path.Combine(configDirectory, "metadata", $"{appId}.json");
                installedFile = Path.Combine(configDirectory, "installed.json");
            }
            catch (Exception)
            {
                return true;
            }

            try
            {
                // Metadane zawierają kopię adresów - uninstall niczego jedynego nie zabierze.
                string? metadataJson = TryReadConfigFile(metadataFile);
                if (metadataJson != null && HasBaseUrlsInJson(metadataJson, appId: null))
                    return false;
            }
            catch (Exception)
            {
                // stan nieznany - blokujemy przed uninstall
                return true;
            }

            try
            {
                string? installedJson = TryReadConfigFile(installedFile);
                return installedJson != null && HasBaseUrlsInJson(installedJson, appId);
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// Czy adresy CDN aplikacji są zapisane w metadanych gry (<c>metadata/&lt;appId&gt;.json</c>).
        /// Tylko tam przetrwają destrukcyjny uninstall - uninstall usuwa wpis z <c>installed.json</c>
        /// wraz z zapisanymi tam adresami.
        /// </summary>
        /// <remarks>
        /// Fail-closed: nieczytelny plik, uszkodzony JSON albo brak adresów zwracają <c>false</c> -
        /// bez potwierdzonych adresów w metadanych nie wolno kasować instalacji.
        /// </remarks>
        public static bool HasBaseUrlsInMetadata(string configDirectory, string appId)
        {
            if (string.IsNullOrWhiteSpace(configDirectory) || string.IsNullOrWhiteSpace(appId))
                return false;

            string metadataFile;
            try
            {
                metadataFile = Path.Combine(configDirectory, "metadata", $"{appId}.json");
            }
            catch (Exception)
            {
                return false;
            }

            try
            {
                string? content = TryReadConfigFile(metadataFile);
                if (content == null)
                    return false;

                return HasBaseUrlsInJson(content, appId: null);
            }
            catch (Exception)
            {
                // Nieczytelny plik - adresów nie da się potwierdzić (fail-closed).
                return false;
            }
        }

        /// <summary>
        /// Czyta plik konfiguracji legendary. Zwraca <c>null</c>, gdy pliku nie ma.
        /// Brak dostępu lub uszkodzony JSON zgłaszamy wyjątkiem - stan jest wtedy niepewny.
        /// </summary>
        private static string? TryReadConfigFile(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Sprawdza, czy legendary ma zarejestrowaną instalację aplikacji (<c>installed.json</c>).
        /// <c>legendary install</c> przy istniejącym wpisie przejmuje katalog instalacji z tego pliku,
        /// a po analizie może usunąć pliki gry (<c>uninstall_tag</c>) i przepisać stan instalacji -
        /// dlatego takich wpisów nie wolno dotykać operacją odświeżającą metadane.
        /// </summary>
        /// <remarks>
        /// Fail-closed: gdy nie da się ustalić stanu (brak dostępu do pliku, uszkodzony JSON),
        /// zwracamy <c>true</c> - operacja odświeżająca nie może zakładać, że gry nie ma w legendary.
        /// </remarks>
        public static bool IsRegistered(string configDirectory, string appId)
        {
            if (string.IsNullOrWhiteSpace(configDirectory) || string.IsNullOrWhiteSpace(appId))
                return true;

            string installedFile;
            try
            {
                installedFile = Path.Combine(configDirectory, "installed.json");
            }
            catch (Exception)
            {
                return true;
            }

            string content;
            try
            {
                // File.ReadAllText odróżnia "pliku nie ma" od "nie mam dostępu" - File.Exists tego nie robi.
                content = File.ReadAllText(installedFile);
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (IOException)
            {
                return true;
            }
            catch (Exception)
            {
                return true;
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                var element = document.RootElement;

                if (element.ValueKind != JsonValueKind.Object)
                    return true;

                // Samo wystąpienie klucza oznacza wpis - jego typ nie ma znaczenia (nieznany = fail-closed).
                return element.TryGetProperty(appId, out _);
            }
            catch (JsonException)
            {
                return true;
            }
        }

        /// <summary>
        /// Odczytuje zarejestrowaną ścieżkę instalacji aplikacji z <c>installed.json</c>.
        /// Odczyt jest wyłącznie do odczytu - stan rejestracji w legendary pozostaje nietknięty.
        /// </summary>
        /// <remarks>
        /// Fail-closed: nieczytelny plik, uszkodzony JSON albo wpis bez poprawnego
        /// <c>install_path</c> zwracają <see cref="LegendaryInstallPathState.Unknown</see> -
        /// destrukcyjny uninstall nie może wtedy kasować plików.
        /// </remarks>
        public static LegendaryInstallPathState GetRegisteredInstallPath(
            string configDirectory,
            string appId,
            out string? installPath)
        {
            installPath = null;

            if (string.IsNullOrWhiteSpace(configDirectory) || string.IsNullOrWhiteSpace(appId))
                return LegendaryInstallPathState.Unknown;

            string installedFile;
            try
            {
                installedFile = Path.Combine(configDirectory, "installed.json");
            }
            catch (Exception)
            {
                return LegendaryInstallPathState.Unknown;
            }

            string content;
            try
            {
                content = File.ReadAllText(installedFile);
            }
            catch (FileNotFoundException)
            {
                return LegendaryInstallPathState.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return LegendaryInstallPathState.Missing;
            }
            catch (Exception)
            {
                // Brak dostępu do pliku - stan niepewny (fail-closed).
                return LegendaryInstallPathState.Unknown;
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                var element = document.RootElement;

                if (element.ValueKind != JsonValueKind.Object)
                    return LegendaryInstallPathState.Unknown;

                if (!element.TryGetProperty(appId, out var appEntry))
                    return LegendaryInstallPathState.Missing;

                if (appEntry.ValueKind != JsonValueKind.Object)
                    return LegendaryInstallPathState.Unknown;

                if (!appEntry.TryGetProperty("install_path", out var pathElement))
                    return LegendaryInstallPathState.Unknown;

                if (pathElement.ValueKind != JsonValueKind.String)
                    return LegendaryInstallPathState.Unknown;

                string? path = pathElement.GetString();
                if (string.IsNullOrWhiteSpace(path))
                    return LegendaryInstallPathState.Unknown;

                installPath = path;
                return LegendaryInstallPathState.Found;
            }
            catch (JsonException)
            {
                return LegendaryInstallPathState.Unknown;
            }
        }

        private static bool HasBaseUrlsInJson(string json, string? appId)
        {
            using var document = JsonDocument.Parse(json);
            var element = document.RootElement;

            if (element.ValueKind != JsonValueKind.Object)
                return false;

            if (appId != null)
            {
                if (!element.TryGetProperty(appId, out var appEntry) || appEntry.ValueKind != JsonValueKind.Object)
                    return false;

                element = appEntry;
            }

            return element.TryGetProperty("base_urls", out var baseUrls)
                && baseUrls.ValueKind == JsonValueKind.Array
                && baseUrls.GetArrayLength() > 0;
        }
    }
}
