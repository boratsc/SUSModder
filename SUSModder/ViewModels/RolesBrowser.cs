using System;
using System.Collections.Generic;
using System.Linq;
using ReactiveUI;
using SUSModder.Models;

namespace SUSModder.ViewModels
{
    /// <summary>E14: wiersz poziomu A zakładki Role — mod mający role (grupowanie w VM, nie w Core).</summary>
    public sealed class RoleModGroup
    {
        public int ModId { get; init; }
        public string Name { get; init; } = string.Empty;
        public int RoleCount { get; init; }
        public string? IconSourceKey { get; init; }
        public bool HasIcon => !string.IsNullOrEmpty(IconSourceKey);
        public string RoleCountText { get; init; } = string.Empty;
        public string RoleCountLabel { get; init; } = string.Empty;
    }

    /// <summary>
    /// E14: wiersz poziomu B — rola wybranego moda. Opakowanie modelu Role (model bez zmian):
    /// CategoryKey to znormalizowana wartość danych dla kropki kategorii (styl Tag=...),
    /// CategoryText/TypeText to surowe dane z API wyświetlane bez tłumaczenia.
    /// </summary>
    public sealed class RoleListItem
    {
        public RoleListItem(Role role)
        {
            Role = role;
            CategoryKey = RolesBrowserProjection.NormalizeCategory(role.Category);
        }

        public Role Role { get; }
        public string Name => Role.Name;
        public string Description => Role.Description;
        public string CategoryKey { get; }
        public string CategoryText => Role.Category;
        public string TypeText => Role.Type;
        public bool HasType => !string.IsNullOrWhiteSpace(Role.Type);
        public bool HasDescription => !string.IsNullOrWhiteSpace(Role.Description);
        public List<Ability> Abilities => Role.Abilities;
        public bool HasAbilities => Role.Abilities.Count > 0;
    }

    /// <summary>E14: opcja filtra — Value to wartość z API (filtrowanie), Label to tekst tłumaczony (wyświetlanie).</summary>
    public sealed class RoleFilterOption : ReactiveObject
    {
        private string _label;

        public RoleFilterOption(string value, string label)
        {
            Value = value;
            _label = label;
        }

        public string Value { get; }
        public string Label
        {
            get => _label;
            set => this.RaiseAndSetIfChanged(ref _label, value);
        }
    }

    /// <summary>
    /// E14: czyste funkcje projekcji danych ról — grupowanie modów (v2 Mods + legacy ModId),
    /// filtry wyłącznie po wartościach danych (nigdy po zlokalizowanym tekście), pluralizacja
    /// liczników i rozstrzyganie stanu ładowania. Bez Avalonii i bez lokalizacji — testowalne
    /// tak jak pozostałe silniki stanu w SUSModder.Tests.
    /// </summary>
    public static class RolesBrowserProjection
    {
        /// <summary>Wartość filtra „Wszystkie” (nie pochodzi z API).</summary>
        public const string AllValue = "All";

        public static IReadOnlyList<RoleModGroup> BuildModGroups(
            IEnumerable<Role> roles,
            IReadOnlyList<ModItem> catalogMods,
            Func<int, string> roleCountText,
            Func<int, string> roleCountLabel)
        {
            var names = new Dictionary<int, string>();
            var counts = new Dictionary<int, int>();

            void Accumulate(int modId, string? name)
            {
                if (modId <= 0)
                    return;
                counts[modId] = counts.TryGetValue(modId, out var count) ? count + 1 : 1;
                if (!string.IsNullOrWhiteSpace(name) &&
                    (!names.TryGetValue(modId, out var existing) || string.IsNullOrWhiteSpace(existing)))
                    names[modId] = name.Trim();
            }

            foreach (var role in roles)
            {
                if (role.Mods.Count > 0)
                {
                    foreach (var mod in role.Mods)
                        Accumulate(mod.Id, mod.Name);
                }
                else if (role.ModId > 0)
                {
                    Accumulate(role.ModId, role.ModName);
                }
            }

            return counts.Keys
                .Select(id =>
                {
                    var catalogMod = catalogMods.FirstOrDefault(m => m.Id == id);
                    var name = names.TryGetValue(id, out var fromApi) && !string.IsNullOrWhiteSpace(fromApi)
                        ? fromApi
                        : catalogMod?.Name ?? string.Empty;
                    return new RoleModGroup
                    {
                        ModId = id,
                        Name = name,
                        RoleCount = counts[id],
                        IconSourceKey = catalogMod?.IconSourceKey,
                        RoleCountText = roleCountText(counts[id]),
                        RoleCountLabel = roleCountLabel(counts[id])
                    };
                })
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.ModId)
                .ToList();
        }

        public static bool MatchesRoleMod(RoleModGroup group, string? query) =>
            string.IsNullOrWhiteSpace(query) || Contains(group.Name, query);

        /// <summary>Filtr roli: kategoria/typ po wartościach API (OrdinalIgnoreCase), search po nazwie/opisie/zdolnościach.</summary>
        public static bool MatchesRole(Role role, string? query, string? categoryFilter, string? typeFilter)
        {
            if (!MatchesValue(role.Category, categoryFilter))
                return false;
            if (!MatchesValue(role.Type, typeFilter))
                return false;
            if (string.IsNullOrWhiteSpace(query))
                return true;

            return Contains(role.Name, query)
                   || Contains(role.Description, query)
                   || role.Abilities.Any(a => Contains(a.Name, query) || Contains(a.Description, query));
        }

        /// <summary>Kanoniczna wielkość liter znanych kategorii (dane API mogą mieć inny casing); nieznane wartości bez zmian.</summary>
        public static string NormalizeCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return string.Empty;
            var trimmed = category.Trim();
            return trimmed.ToLowerInvariant() switch
            {
                "crewmate" => "Crewmate",
                "impostor" => "Impostor",
                "neutral" => "Neutral",
                "modifier" => "Modifier",
                _ => trimmed
            };
        }

        /// <summary>
        /// Klucz pluralizacji liczby ról. Reguła polska (1 / 2-4 bez nastek / reszta);
        /// EN ma identyczne teksty dla Few i Many, więc działa też dla angielskiego.
        /// </summary>
        public static string RoleCountKey(int count)
        {
            if (count == 1)
                return "UI.Roles.RoleCountOne";
            var digit = count % 10;
            var teens = count % 100;
            if (digit is >= 2 and <= 4 && teens is not (>= 12 and <= 14))
                return "UI.Roles.RoleCountFew";
            return "UI.Roles.RoleCountMany";
        }

        /// <summary>
        /// RolesService zwraca pustą listę także przy błędzie API (serwis nietknięty).
        /// O stanie błędu decyduje znany status offline API; pusta lista przy online = stan pusty.
        /// </summary>
        public static bool IsLoadError(int roleCount, bool apiOffline) => roleCount == 0 && apiOffline;

        /// <summary>Walidacja preselekcji moda (deep-link lub przywrócenie stanu) względem aktualnych danych.</summary>
        public static int? ResolveModId(int? modId, IReadOnlyList<RoleModGroup> groups) =>
            modId is int id && groups.Any(g => g.ModId == id) ? id : null;

        private static bool MatchesValue(string value, string? filter) =>
            filter is null ||
            filter == AllValue ||
            string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);

        private static bool Contains(string? value, string query) =>
            !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}