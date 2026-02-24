using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Buduje URL-e do pobierania modów przez CDN serwera SUSModder.
    /// Endpoint: /api/mod-download/{id}/{version}?platform=steam|epic
    /// </summary>
    public static class ModDownloadUrlBuilder
    {
        private static readonly string _configFilePath = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath)!,
            "appsettings.json");

        private static string GetBaseUrl()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(_configFilePath, optional: true, reloadOnChange: false)
                .Build();

            return (config["Configuration:BaseUrl"] ?? "https://susmodder.app/").TrimEnd('/');
        }

        private static string GetModDownloadEndpoint()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(_configFilePath, optional: true, reloadOnChange: false)
                .Build();

            return (config["Configuration:ModDownloadEndpoint"] ?? "/api/mod-download").TrimEnd('/');
        }

        /// <summary>
        /// Buduje URL do pobrania moda przez CDN.
        /// Wynik: https://susmodder.app/api/mod-download/{id}/{version}?platform=steam|epic
        /// </summary>
        public static string Build(ModConfiguration mod, string platform)
        {
            string baseUrl = GetBaseUrl();
            string endpoint = GetModDownloadEndpoint();
            string normalizedPlatform = platform.Equals("epic", StringComparison.OrdinalIgnoreCase) ? "epic" : "steam";

            return $"{baseUrl}{endpoint}/{mod.Id}/{mod.ModVersion}?platform={normalizedPlatform}";
        }

        /// <summary>
        /// Wyciąga nazwę pliku DLL z oryginalnego linku (GitHubRepoOrLink lub EpicGitHubRepoOrLink).
        /// Używane do określenia nazwy pliku na dysku bez pobierania przez ten link.
        /// </summary>
        public static string GetDllFileName(ModConfiguration dllMod, string platform)
        {
            string sourceUrl = platform.Equals("epic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(dllMod.EpicGitHubRepoOrLink)
                ? dllMod.EpicGitHubRepoOrLink
                : dllMod.GitHubRepoOrLink ?? string.Empty;

            if (string.IsNullOrEmpty(sourceUrl))
                return string.Empty;

            try
            {
                return Path.GetFileName(new Uri(sourceUrl).LocalPath);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
