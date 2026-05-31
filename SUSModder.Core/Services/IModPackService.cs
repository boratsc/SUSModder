using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    public interface IModPackService
    {
        Task<ModPackCreateResult> CreatePackAsync(ModPackCreateRequest request, CancellationToken ct = default);
        Task<ModPack?> GetPackAsync(string packCode, CancellationToken ct = default);
        Task<IReadOnlyList<ModPackListEntry>> ListOwnPacksAsync(CancellationToken ct = default);
        Task<bool> DeletePackAsync(string packCode, CancellationToken ct = default);
        Task<ModPackExternalDll?> UploadExternalDllAsync(string packCode, string filePath, CancellationToken ct = default);
        ModPackValidationResult ValidatePack(ModPack pack, bool externalDllConsentGiven);
        string CreatorHash { get; }
    }
}
