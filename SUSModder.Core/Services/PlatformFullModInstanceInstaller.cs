using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Kompozytowy instalator full moda do lokalnej instancji.
    /// Dobiera implementację (Steam/Epic) na podstawie platformy użytkownika.
    /// </summary>
    public sealed class PlatformFullModInstanceInstaller : IFullModInstanceInstaller
    {
        private readonly IConfiguration? _configuration;
        private readonly HttpClient? _httpClient;
        private readonly IFullModInstanceInstaller? _steamInstaller;
        private readonly IFullModInstanceInstaller? _epicInstaller;

        public PlatformFullModInstanceInstaller(IConfiguration configuration, HttpClient? httpClient = null)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _httpClient = httpClient;
        }

        /// <summary>
        /// Test-only constructor that injects the platform-specific installers directly.
        /// </summary>
        public PlatformFullModInstanceInstaller(
            IFullModInstanceInstaller steamInstaller,
            IFullModInstanceInstaller epicInstaller)
        {
            _steamInstaller = steamInstaller;
            _epicInstaller = epicInstaller;
        }

        public Task<ModInstallResult> InstallAsync(
            ModConfiguration modConfig,
            string targetInstallPath,
            string platform,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            ModManagerUserCallbacks userCallbacks,
            Action<string>? onSpeedUpdate = null)
        {
            if (string.Equals(platform, "steam", StringComparison.OrdinalIgnoreCase))
            {
                var steamInstaller = _steamInstaller ?? new ModManagerFullModInstanceInstaller(_configuration!);
                return steamInstaller.InstallAsync(
                    modConfig,
                    targetInstallPath,
                    platform,
                    progress,
                    log,
                    userCallbacks,
                    onSpeedUpdate);
            }

            if (string.Equals(platform, "epic", StringComparison.OrdinalIgnoreCase))
            {
                var epicInstaller = _epicInstaller ?? new EpicFullModInstanceInstaller(_httpClient);
                return epicInstaller.InstallAsync(
                    modConfig,
                    targetInstallPath,
                    platform,
                    progress,
                    log,
                    userCallbacks,
                    onSpeedUpdate);
            }

            return Task.FromResult(ModInstallResult.Failed(
                $"mod_instance_platform_not_supported:{platform}"));
        }
    }
}
