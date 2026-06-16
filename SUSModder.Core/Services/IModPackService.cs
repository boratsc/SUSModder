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
        Task<ModPackCustomArtifact?> UploadCustomDllAsync(string packCode, string filePath, CancellationToken ct = default);
        Task<ModPackArtifactStatusResult> GetExternalDllStatusAsync(string packCode, string sha256, CancellationToken ct = default);
        Task<ModPackCustomArtifact?> DeclareGitHubCustomModAsync(string packCode, ModPackCustomGithubModRequest request, CancellationToken ct = default);
        Task<ModPackArtifactStatusResult> GetCustomArtifactStatusAsync(string packCode, string artifactId, CancellationToken ct = default);
        Task<ModPackFinalizeResult> FinalizePackAsync(string packCode, CancellationToken ct = default);
        ModPackValidationResult ValidatePack(ModPack pack, bool externalDllConsentGiven);
        string CreatorHash { get; }
    }
}
