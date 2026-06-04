using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Domyślna implementacja instalacji instancji oparta o istniejący ModManager.
    /// </summary>
    public sealed class ModManagerFullModInstanceInstaller : IFullModInstanceInstaller
    {
        private readonly IConfiguration _configuration;

        public ModManagerFullModInstanceInstaller(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public Task InstallAsync(
            ModConfiguration modConfig,
            string targetInstallPath,
            string platform,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            ModManagerUserCallbacks userCallbacks,
            Action<string>? onSpeedUpdate = null)
        {
            var modManager = new ModManager(_configuration);
            return modManager.InstallFullModToPathAsync(
                modConfig,
                targetInstallPath,
                progress,
                log,
                userCallbacks,
                platform,
                onSpeedUpdate);
        }
    }
}
