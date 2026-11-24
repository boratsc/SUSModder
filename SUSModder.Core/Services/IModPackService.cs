using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    public interface IModPackService
    {
        Task<ModPackCreateResult> CreatePackAsync(ModPackCreateRequest request, CancellationToken ct = default);
        Task<ModPack?> GetPackAsync(string packCode, CancellationToken ct = default);
        Task<IReadOnlyList<ModPackListEntry>> ListOwnPacksAsync(CancellationToken ct = default);
        Task<ModPackListResult> ListOwnPacksDetailedAsync(CancellationToken ct = default);
        Task<bool> DeletePackAsync(string packCode, CancellationToken ct = default);
        Task<ModPackDeleteResult> DeletePackDetailedAsync(string packCode, CancellationToken ct = default);
        Task<ModPackExternalDll?> UploadExternalDllAsync(string packCode, string filePath, CancellationToken ct = default);
        Task<ModPackCustomArtifact?> UploadCustomDllAsync(string packCode, string filePath, CancellationToken ct = default);
        Task<ModPackArtifactStatusResult> GetExternalDllStatusAsync(string packCode, string sha256, CancellationToken ct = default);
        Task<ModPackCustomArtifact?> DeclareGitHubCustomModAsync(string packCode, ModPackCustomGithubModRequest request, CancellationToken ct = default);
        Task<ModPackArtifactStatusResult> GetCustomArtifactStatusAsync(string packCode, string artifactId, CancellationToken ct = default);
        Task<ModPackFinalizeResult> FinalizePackAsync(string packCode, CancellationToken ct = default);
        ModPackValidationResult ValidatePack(ModPack pack, bool externalDllConsentGiven);
        string CreatorHash { get; }

        /// <summary>Czy lokalny magazyn ma token właściciela paczki (gating akcji zapisu). Błąd odczytu → false.</summary>
        bool HasOwnerToken(string packCode);

        /// <summary>
        /// Stan tokenu właściciela: Available / Missing (legacy, brak wpisu) / Unreadable (błąd IO/DPAPI/uszkodzony plik).
        /// Domyślna implementacja nie rozróżnia Unreadable — nadpisz w implementacji z magazynem.
        /// </summary>
        OwnerTokenState GetOwnerTokenState(string packCode) =>
            HasOwnerToken(packCode) ? OwnerTokenState.Available : OwnerTokenState.Missing;

        /// <summary>
        /// Migawka stanów tokenów dla wielu kodów po jednym odczycie magazynu (klucze: znormalizowane kody).
        /// Domyślna implementacja czyta per kod — nadpisz w implementacji z magazynem.
        /// </summary>
        IReadOnlyDictionary<string, OwnerTokenState> GetOwnerTokenStates(IEnumerable<string> packCodes)
        {
            var states = new Dictionary<string, OwnerTokenState>(System.StringComparer.Ordinal);
            foreach (var code in packCodes)
                states[ModPackCodeValidator.IsValid(code) ? ModPackCodeValidator.Normalize(code) : code] = GetOwnerTokenState(code);
            return states;
        }
    }
}
