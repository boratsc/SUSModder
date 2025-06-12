using System.Collections.Generic;
using System.Threading.Tasks;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    public class RoleService
    {
        /// <summary>
        /// Pobiera wszystkie role i modyfikatory z serwera (lub cache).
        /// </summary>
        public async Task<List<ModifierInfo>> GetAllRolesAsync()
        {
            return await RoleModifierService.GetAllRolesAsync();
        }

        /// <summary>
        /// Filtrowanie ról/modyfikatorów według kryteriów.
        /// </summary>
        public IEnumerable<ModifierInfo> FilterRoles(
            IEnumerable<ModifierInfo> allRoles,
            string? search = null,
            string? category = null,
            string? type = null,
            string? modName = null)
        {
            return RoleModifierService.FilterRoles(allRoles, search, category, type, modName);
        }
    }
}
