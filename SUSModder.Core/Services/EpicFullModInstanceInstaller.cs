using System;
using System.Net.Http;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Instalator full moda dla Epic do konkretnej ścieżki lokalnej instancji.
    /// </summary>
    public sealed class EpicFullModInstanceInstaller : IFullModInstanceInstaller
    {
        private readonly EpicModInstaller _installer;

        public EpicFullModInstanceInstaller(HttpClient? httpClient = null)
        {
            _installer = new EpicModInstaller(httpClient);
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
            if (!string.Equals(platform, "epic", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(ModInstallResult.Failed(
                    $"Platform '{platform}' is not supported by EpicFullModInstanceInstaller."));
            }

            return _installer.InstallAsync(modConfig, targetInstallPath, progress, log);
        }
    }
}
