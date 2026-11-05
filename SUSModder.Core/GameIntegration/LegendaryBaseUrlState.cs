using System;
using System.IO;
using System.Text.Json;

namespace SUSModder.Core.GameIntegration
{
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
