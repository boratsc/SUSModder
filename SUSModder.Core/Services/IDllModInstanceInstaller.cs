using System.Threading.Tasks;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Adapter instalacji moda DLL do konkretnej lokalnej instancji.
    /// </summary>
    public interface IDllModInstanceInstaller
    {
        Task<string?> InstallAsync(ModConfiguration dllMod, ModConfiguration targetMod, string platform);
    }
}
