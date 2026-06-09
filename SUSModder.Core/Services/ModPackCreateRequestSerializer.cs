using System.Text.Json;
using System.Text.Json.Serialization;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services;

/// <summary>
/// Serializuje żądanie utworzenia modpacka zgodnie z API v2 (bez externalDlls w body).
/// </summary>
internal static class ModPackCreateRequestSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static string ToJson(ModPackCreateRequest request)
    {
        var payload = new ModPackCreateApiPayload
        {
            CreatorHash = request.CreatorHash,
            CreatorName = request.CreatorName,
            FullModId = request.FullModId,
            FullModVersion = request.FullModVersion,
            ModName = request.ModName,
            DiscordInvite = request.DiscordInvite,
            IncludeIntegrationDll = request.IncludeIntegrationDll,
            TtlDays = request.TtlDays,
            DllMods = request.DllMods,
            TouConfig = request.TouConfig
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed class ModPackCreateApiPayload
    {
        public string CreatorHash { get; set; } = string.Empty;
        public string? CreatorName { get; set; }
        public int FullModId { get; set; }
        public string FullModVersion { get; set; } = "latest";
        public string? ModName { get; set; }
        public string? DiscordInvite { get; set; }
        public bool IncludeIntegrationDll { get; set; }
        public int TtlDays { get; set; } = 30;
        public IReadOnlyList<ModPackDllModRequest> DllMods { get; set; } = Array.Empty<ModPackDllModRequest>();
        public JsonElement? TouConfig { get; set; }
    }
}
