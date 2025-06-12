using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SUSModder.Core.Utilities
{
    public class ModifierInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ModName { get; set; } = string.Empty;
        public List<string> Abilities { get; set; } = new List<string>();
    }

    public static class RoleModifierService
    {
        private static List<ModifierInfo>? cachedRoles = null;

        public static async Task<List<ModifierInfo>> GetAllRolesAsync()
        {
            if (cachedRoles != null)
                return cachedRoles;

            string url = "https://susfuckr.boracik.pl/susfuckr/roles/roles.json";
            using var client = new HttpClient();
            var response = await client.GetStringAsync(url);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            cachedRoles = JsonSerializer.Deserialize<List<ModifierInfo>>(response, options) ?? new List<ModifierInfo>();
            return cachedRoles;
        }

        public static IEnumerable<ModifierInfo> FilterRoles(
            IEnumerable<ModifierInfo> allRoles,
            string? search = null,
            string? category = null,
            string? type = null,
            string? modName = null)
        {
            var filtered = allRoles;

            if (!string.IsNullOrWhiteSpace(search))
                filtered = filtered.Where(r =>
                    r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    r.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(category) && category != "Wszystkie")
                filtered = filtered.Where(r => r.Category == category);

            if (!string.IsNullOrWhiteSpace(type) && type != "Wszystkie")
                filtered = filtered.Where(r => r.Type == type);

            if (!string.IsNullOrWhiteSpace(modName) && modName != "Wszystkie")
                filtered = filtered.Where(r => r.ModName == modName);

            return filtered;
        }
    }
}
