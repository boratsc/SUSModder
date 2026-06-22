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
                var requestedVersion = !string.IsNullOrWhiteSpace(mod.ModVersion)
                    && !string.Equals(mod.ModVersion, "latest", StringComparison.OrdinalIgnoreCase)
                        ? mod.ModVersion
                        : null;

                var variant = SelectVariant(
                    detail.Data.Variants,
                    normalizedPlatform,
                    requestedVersion,
                    IsDllMod(mod));
                if (variant is not null)
                {
                    var variantVersion = ResolveVariantVersion(variant, detail.Data, mod.Id);
                    if (!string.IsNullOrWhiteSpace(variantVersion))
                    {
                        return new ModDownloadResolution
                        {
                            Url = client.BuildModDownloadUrl(
                                mod.Id,
                                variantVersion,
                                NormalizeVariantPlatform(variant.Platform, normalizedPlatform),
                                variant.Architecture),
                            ExpectedSha256 = NormalizeSha256(variant.Sha256)
                        };
                    }
                }

                // Prefer the version pinned on the mod configuration (e.g. from a modpack
                // snapshot) over the catalog's current version. If neither is available,
                // fall back to the direct Build URL which uses mod.ModVersion.
                var catalogVersion = !string.IsNullOrWhiteSpace(mod.ModVersion)
                    ? mod.ModVersion
                    : detail.Data.CurrentVersion;

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

        private static bool IsDllMod(ModConfiguration mod)
        {
            return string.Equals(mod.ModType, "dll", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeVariantPlatform(string? variantPlatform, string fallbackPlatform)
        {
            return string.Equals(variantPlatform, "epic", StringComparison.OrdinalIgnoreCase)
                ? "epic"
                : string.Equals(variantPlatform, "steam", StringComparison.OrdinalIgnoreCase)
                    ? "steam"
                    : fallbackPlatform;
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

            string platform,

            string? requestedVersion = null,

            bool allowCrossPlatformFallback = false)

        {

            if (variants.Count == 0)

                return null;

            IEnumerable<CatalogModVariantDto> candidates = variants;

            // When a concrete version is requested (e.g. from a modpack snapshot),
            // never silently fall back to a different version. This keeps shared
            // modpacks platform-independent: the pack stores mod+version identity,
            // and the client picks the variant for that exact version on the
            // installing user's platform.
            if (!string.IsNullOrWhiteSpace(requestedVersion))
            {
                var versionMatches = variants
                    .Where(v => string.Equals(v.Version, requestedVersion, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (versionMatches.Count > 0)
                    candidates = versionMatches;
                else
                    return null;
            }

            // Prefer same-platform variants first (x64 then x86), because Among Us
            // (Steam & Epic) is a 64-bit application and Epic specifically requires
            // an x64 build for BepInEx to load.
            var samePlatform = candidates

                .Where(v => v.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase))

                .ToList();

            if (samePlatform.Count > 0)
            {
                return samePlatform.FirstOrDefault(v => v.Architecture.Equals("x64", StringComparison.OrdinalIgnoreCase))
                       ?? samePlatform.FirstOrDefault(v => v.Architecture.Equals("x86", StringComparison.OrdinalIgnoreCase))
                       ?? samePlatform[0];
            }

            if (!allowCrossPlatformFallback)
                return null;

            // Cross-platform fallback is allowed only for DLL mods. Many DLL mods
            // published before April 2025 (when Epic support was added) ship a single
            // Steam/x86 build. Their payload is platform-agnostic at runtime, so Epic
            // users can download the Steam variant instead of hitting an Epic 404.
            return candidates.FirstOrDefault(v => v.Architecture.Equals("x86", StringComparison.OrdinalIgnoreCase))
                   ?? candidates.FirstOrDefault(v => v.Architecture.Equals("x64", StringComparison.OrdinalIgnoreCase))
                   ?? candidates.FirstOrDefault();
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


