using System;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Adapter używający istniejącego DllModificationService do instalacji DLL w instancji.
    /// </summary>
    public sealed class DllModificationServiceInstanceInstaller : IDllModInstanceInstaller
    {
        private readonly DllModificationService _dllService;

        public DllModificationServiceInstanceInstaller(DllModificationService dllService)
        {
            _dllService = dllService ?? throw new ArgumentNullException(nameof(dllService));
        }

        public Task<string?> InstallAsync(ModConfiguration dllMod, ModConfiguration targetMod, string platform)
        {
            return _dllService.InstallDllToModAsync(dllMod, targetMod, platform);
        }
    }
}
