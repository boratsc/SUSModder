using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SUSModder.Models
{
    public class RoleModRef
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class Role
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>Legacy v1 – płaskie powiązanie z modem.</summary>
        [JsonPropertyName("modId")]
        public int ModId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("modName")]
        public string ModName { get; set; } = string.Empty;

        [JsonPropertyName("mods")]
        public List<RoleModRef> Mods { get; set; } = new();

        [JsonPropertyName("abilities")]
        public List<Ability> Abilities { get; set; } = new();

        public IEnumerable<int> GetAssociatedModIds()
        {
            if (Mods.Count > 0)
                return Mods.Select(m => m.Id);

            if (ModId > 0)
                return new[] { ModId };

            return [];
        }

        public bool IsAssociatedWithMod(int modConfigId) =>
            GetAssociatedModIds().Contains(modConfigId);
    }

    public class Ability
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("icon")]
        public string Icon { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }
}
