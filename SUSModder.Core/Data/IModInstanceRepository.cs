using System.Collections.Generic;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Repozytorium lokalnych instancji modpacków (Moje zestawy).
    /// </summary>
    public interface IModInstanceRepository
    {
        List<ModInstance> GetAllInstances();

        /// <summary>
        /// Instancje utworzone jako zestawy (manual / import kodu / klon) — bez legacy z katalogu.
        /// </summary>
        List<ModInstance> GetPackInstances();

        ModInstance? GetInstance(string instanceId);
        List<ModInstance> GetInstancesForBaseMod(int baseModId);
        void AddInstance(ModInstance instance);
        void UpdateInstance(ModInstance instance);
        void DeleteInstance(string instanceId);
        void RenameInstance(string instanceId, string displayName);
        void UpdateLastLaunched(string instanceId);

        List<ModInstanceDll> GetDlls(string instanceId);
        long AddDll(ModInstanceDll dll);
        void RemoveDll(long id);

        List<ModInstanceConfig> GetConfigs(string instanceId);
        long AddConfig(ModInstanceConfig config);
        void UpdateConfig(ModInstanceConfig config);
        void DeleteConfig(long id);
    }
}
