using System;

using System.IO;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;

using SUSModder.Core.Api;

using SUSModder.Core.Api.Models;

using SUSModder.Core.Configuration;



namespace SUSModder.Core.Utilities

{

    /// <summary>

    /// Buduje URL-e do pobierania modów przez API v2.

    /// Endpoint: /downloads/mod/{id}/{version}?platform=steam|epic

    /// </summary>

    public static class ModDownloadUrlBuilder

    {

        private static readonly string _configFilePath = Path.Combine(

            Path.GetDirectoryName(Environment.ProcessPath)!,

            "appsettings.json");



        public static string Build(ModConfiguration mod, string platform)

        {

            var directUrl = TryGetDirectDownloadUrl(mod, platform);

            if (!string.IsNullOrWhiteSpace(directUrl))

                return directUrl;

            var client = SUSModderApiClientProvider.TryGetDefault();

            if (client is not null)

                return client.BuildModDownloadUrl(mod.Id, mod.ModVersion, platform);



            var baseUrl = GetApiV2BaseUrl().TrimEnd('/');

            var normalizedPlatform = platform.Equals("epic", StringComparison.OrdinalIgnoreCase) ? "epic" : "steam";

            return $"{baseUrl}/downloads/mod/{mod.Id}/{Uri.EscapeDataString(mod.ModVersion)}?platform={normalizedPlatform}&arch=x86";

        }



        public static async Task<string> ResolveAsync(
            ModConfiguration mod,
            string platform,
            CancellationToken cancellationToken = default)
        {
            var resolution = await ResolveWithHashAsync(mod, platform, cancellationToken);
            return resolution.Url;
        }

        public static async Task<ModDownloadResolution> ResolveWithHashAsync(
            ModConfiguration mod,
            string platform,
            CancellationToken cancellationToken = default)
        {
            var directUrl = TryGetDirectDownloadUrl(mod, platform);
            if (!string.IsNullOrWhiteSpace(directUrl))
                return new ModDownloadResolution { Url = directUrl };

            var client = SUSModderApiClientProvider.TryGetDefault();
            if (client is null)
            {
                return new ModDownloadResolution
                {
                    Url = Build(mod, platform)
                };
            }

            var normalizedPlatform = platform.Equals("epic", StringComparison.OrdinalIgnoreCase) ? "epic" : "steam";

            SusModderApiResult<CatalogModDetailDto> detail;
            try
            {
                detail = await client.GetCatalogModDetailAsync(mod.Id, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ModDownloadUrlBuilder] catalog/{mod.Id} failed: {ex.Message}");
                return new ModDownloadResolution { Url = Build(mod, platform) };
            }

            if (detail.IsSuccess && detail.Data is not null)
            {
                var variant = SelectVariant(detail.Data.Variants, normalizedPlatform);
                if (variant is not null)
                {
                    var variantVersion = ResolveVariantVersion(variant, detail.Data, mod.Id);
                    if (!string.IsNullOrWhiteSpace(variantVersion))
                    {
                        return new ModDownloadResolution
                        {
                            Url = client.BuildModDownloadUrl(mod.Id, variantVersion, platform),
                            ExpectedSha256 = NormalizeSha256(variant.Sha256)
                        };
                    }
                }

                var catalogVersion = !string.IsNullOrWhiteSpace(detail.Data.CurrentVersion)
                    ? detail.Data.CurrentVersion
                    : mod.ModVersion;

                if (!string.IsNullOrWhiteSpace(catalogVersion))
                {
                    return new ModDownloadResolution
                    {
                        Url = client.BuildModDownloadUrl(mod.Id, catalogVersion, platform)
                    };
                }
            }

            return new ModDownloadResolution { Url = Build(mod, platform) };
        }

        private static string? TryGetDirectDownloadUrl(ModConfiguration mod, string platform)
        {
            if (mod.Id > 0)
                return null;

            var sourceUrl = platform.Equals("epic", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(mod.EpicGitHubRepoOrLink)
                ? mod.EpicGitHubRepoOrLink
                : mod.GitHubRepoOrLink;

            if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                return uri.ToString();
            }

            return null;
        }

        private static string? NormalizeSha256(string? sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256))
                return null;

            return sha256.Trim().ToLowerInvariant();
        }



        public static string GetDllFileName(ModConfiguration dllMod, string platform)

        {

            string sourceUrl = platform.Equals("epic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(dllMod.EpicGitHubRepoOrLink)

                ? dllMod.EpicGitHubRepoOrLink

                : dllMod.GitHubRepoOrLink ?? string.Empty;



            if (string.IsNullOrEmpty(sourceUrl))

                return BuildFallbackDllFileName(dllMod.ModName);



            try

            {

                var fileName = Path.GetFileName(new Uri(sourceUrl).LocalPath);
                if (IsSafeDllFileName(fileName))
                    return fileName;

            }

            catch

            {

                // Fall back to a deterministic catalog name below.

            }

            return BuildFallbackDllFileName(dllMod.ModName);

        }



        private static string BuildFallbackDllFileName(string? modName)

        {

            var baseName = string.IsNullOrWhiteSpace(modName) ? "mod" : modName.Trim();

            foreach (var invalid in Path.GetInvalidFileNameChars())

                baseName = baseName.Replace(invalid, '_');

            baseName = baseName.Replace(Path.DirectorySeparatorChar, '_')

                .Replace(Path.AltDirectorySeparatorChar, '_');

            return string.IsNullOrWhiteSpace(baseName) ? "mod.dll" : $"{baseName}.dll";

        }



        private static bool IsSafeDllFileName(string? fileName)

        {

            if (string.IsNullOrWhiteSpace(fileName))

                return false;

            if (!string.Equals(Path.GetExtension(fileName), ".dll", StringComparison.OrdinalIgnoreCase))

                return false;

            return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&

                   !fileName.Contains(Path.DirectorySeparatorChar) &&

                   !fileName.Contains(Path.AltDirectorySeparatorChar);

        }



        private static CatalogModVariantDto? SelectVariant(

            IReadOnlyList<CatalogModVariantDto> variants,

            string platform)

        {

            if (variants.Count == 0)

                return null;



            return variants.FirstOrDefault(v =>

                       v.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase) &&

                       v.Architecture.Equals("x86", StringComparison.OrdinalIgnoreCase))

                   ?? variants.FirstOrDefault(v =>

                       v.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase) &&

                       v.Architecture.Equals("x64", StringComparison.OrdinalIgnoreCase))

                   ?? variants.FirstOrDefault(v =>

                       v.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase));

        }



        private static string? ResolveVariantVersion(

            CatalogModVariantDto variant,

            CatalogModDetailDto detail,

            int modId)

        {

            if (!string.IsNullOrWhiteSpace(variant.Version))

                return variant.Version;



            if (!string.IsNullOrWhiteSpace(detail.CurrentVersion))

                return detail.CurrentVersion;



            if (string.IsNullOrWhiteSpace(variant.DownloadUrl))

                return null;



            try

            {

                var segments = new Uri(variant.DownloadUrl).AbsolutePath

                    .Split('/', StringSplitOptions.RemoveEmptyEntries);

                var modIndex = Array.FindIndex(segments, s => s.Equals(modId.ToString(), StringComparison.Ordinal));

                if (modIndex >= 0 && modIndex + 1 < segments.Length)

                    return Uri.UnescapeDataString(segments[modIndex + 1]);

            }

            catch

            {

                // ignored

            }



            return null;

        }



        private static string GetApiV2BaseUrl()

        {

            var config = new ConfigurationBuilder()

                .AddJsonFile(_configFilePath, optional: true, reloadOnChange: false)

                .Build();



            return config["Configuration:ApiV2BaseUrl"] ?? "https://api.susmodder-cdn.ovh/v2";

        }

    }

}


