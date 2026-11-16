using System.Collections.Generic;
using System.Linq;
using SUSModder.Models;
using SUSModder.Services.Localization;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// E14: projekcja zakładki Role — grupowanie modów (v2 Mods + legacy ModId), liczniki,
/// filtry niezależne od języka UI, pluralizacja, stan ładowania i preselekcja deep-linku.
/// Czyste funkcje bez Avalonii (MainWindowViewModel wymaga DI, jak w pozostałych testach).
/// </summary>
public class RolesBrowserTests
{
    private static Role V2Role(int id, string name, params (int Id, string Name)[] mods) => new()
    {
        Id = id,
        Name = name,
        Mods = mods.Select(m => new RoleModRef { Id = m.Id, Name = m.Name }).ToList()
    };

    private static IReadOnlyList<RoleModGroup> Build(IEnumerable<Role> roles, IReadOnlyList<ModItem>? catalog = null) =>
        RolesBrowserProjection.BuildModGroups(roles, catalog ?? new List<ModItem>(), n => n.ToString(), n => $"count:{n}");

    // ── Grupowanie modów ──

    [Fact]
    public void V2RolesGroupByEveryAssociatedModWithNamesFromApi()
    {
        var roles = new[]
        {
            V2Role(1, "Sheriff", (10, "Town of Us"), (20, "Other Mod")),
            V2Role(2, "Medic", (10, "Town of Us"))
        };
        var groups = Build(roles);
        Assert.Equal(2, groups.Count);
        var town = groups.Single(g => g.ModId == 10);
        Assert.Equal("Town of Us", town.Name);
        Assert.Equal(2, town.RoleCount);
        Assert.Equal("2", town.RoleCountText);
        Assert.Equal("count:2", town.RoleCountLabel);
        Assert.Equal(1, groups.Single(g => g.ModId == 20).RoleCount);
    }

    [Fact]
    public void LegacyV1RolesGroupByModIdWithLegacyModName()
    {
        var roles = new[]
        {
            new Role { Id = 1, Name = "Old Role", ModId = 30, ModName = "Legacy Mod" },
            new Role { Id = 2, Name = "Old Role 2", ModId = 30, ModName = "Legacy Mod" }
        };
        var group = Assert.Single(Build(roles));
        Assert.Equal(30, group.ModId);
        Assert.Equal("Legacy Mod", group.Name);
        Assert.Equal(2, group.RoleCount);
    }

    [Fact]
    public void V2AndLegacyReferencesToSameModMergeIntoOneGroup()
    {
        var roles = new[]
        {
            V2Role(1, "Sheriff", (10, "Town of Us")),
            new Role { Id = 2, Name = "Legacy", ModId = 10, ModName = "Town of Us" }
        };
        var group = Assert.Single(Build(roles));
        Assert.Equal(10, group.ModId);
        Assert.Equal(2, group.RoleCount);
    }

    [Fact]
    public void RolesWithoutModAssociationAreIgnored()
    {
        var roles = new[] { new Role { Id = 1, Name = "Orphan" }, V2Role(2, "Sheriff", (10, "Mod")) };
        var group = Assert.Single(Build(roles));
        Assert.Equal(10, group.ModId);
    }

    [Fact]
    public void EmptyApiNameFallsBackToCatalogName()
    {
        var catalog = new List<ModItem> { new() { Id = 10, Name = "Catalog Name", PngFileName = "mod.png" } };
        var group = Assert.Single(Build(new[] { V2Role(1, "Sheriff", (10, "")) }, catalog));
        Assert.Equal("Catalog Name", group.Name);
    }

    [Fact]
    public void CatalogMatchProvidesIconAndMissingMatchStaysNeutral()
    {
        var catalog = new List<ModItem> { new() { Id = 10, Name = "With Icon", PngFileName = "mod.png" } };
        var groups = Build(new[] { V2Role(1, "A", (10, "With Icon"), (99, "No Icon")) }, catalog);
        var withIcon = groups.Single(g => g.ModId == 10);
        Assert.True(withIcon.HasIcon);
        Assert.Equal("mod.png", withIcon.IconSourceKey);
        var withoutIcon = groups.Single(g => g.ModId == 99);
        Assert.False(withoutIcon.HasIcon);
        Assert.Null(withoutIcon.IconSourceKey);
    }

    [Fact]
    public void GroupsAreSortedByName()
    {
        var groups = Build(new[] { V2Role(1, "A", (2, "Zeta"), (1, "Alpha")) });
        Assert.Equal(new[] { "Alpha", "Zeta" }, groups.Select(g => g.Name));
    }

    // ── Filtry niezależne od języka UI ──

    [Fact]
    public void CategoryAndTypeFiltersUseApiValuesNotLocalizedLabels()
    {
        var role = new Role { Name = "Sheriff", Category = "Crewmate", Type = "Role" };
        // Wartości danych API działają (także inny casing).
        Assert.True(RolesBrowserProjection.MatchesRole(role, null, "Crewmate", "Role"));
        Assert.True(RolesBrowserProjection.MatchesRole(role, null, "crewmate", "role"));
        // Zlokalizowane etykiety filtrów NIE są wartościami danych — nie pasują.
        Assert.False(RolesBrowserProjection.MatchesRole(role, null, "Załoga", "Rola"));
        Assert.False(RolesBrowserProjection.MatchesRole(role, null, "Impostor", "All"));
        Assert.False(RolesBrowserProjection.MatchesRole(role, null, "All", "Modifier"));
        // „Wszystkie” przepuszcza wszystko.
        Assert.True(RolesBrowserProjection.MatchesRole(role, null, "All", "All"));
        Assert.True(RolesBrowserProjection.MatchesRole(role, null, null, null));
    }

    [Fact]
    public void RoleSearchMatchesNameDescriptionAndAbilities()
    {
        var role = new Role
        {
            Name = "Sheriff",
            Description = "Shoots the impostor",
            Abilities = new List<Ability> { new() { Name = "Shoot", Description = "Kill ability" } }
        };
        Assert.True(RolesBrowserProjection.MatchesRole(role, "sher", "All", "All"));
        Assert.True(RolesBrowserProjection.MatchesRole(role, "IMPOSTOR", "All", "All"));
        Assert.True(RolesBrowserProjection.MatchesRole(role, "shoot", "All", "All"));
        Assert.True(RolesBrowserProjection.MatchesRole(role, "kill ability", "All", "All"));
        Assert.False(RolesBrowserProjection.MatchesRole(role, "medic", "All", "All"));
    }

    [Fact]
    public void ModSearchMatchesGroupNameCaseInsensitive()
    {
        var group = new RoleModGroup { ModId = 1, Name = "Town of Us" };
        Assert.True(RolesBrowserProjection.MatchesRoleMod(group, "town"));
        Assert.True(RolesBrowserProjection.MatchesRoleMod(group, "  "));
        Assert.False(RolesBrowserProjection.MatchesRoleMod(group, "other"));
    }

    [Theory]
    [InlineData("crewmate", "Crewmate")]
    [InlineData("IMPOSTOR", "Impostor")]
    [InlineData(" Neutral ", "Neutral")]
    [InlineData("modifier", "Modifier")]
    [InlineData("Custom", "Custom")]
    [InlineData(null, "")]
    public void NormalizeCategoryCanonicalizesKnownValuesOnly(string? input, string expected) =>
        Assert.Equal(expected, RolesBrowserProjection.NormalizeCategory(input));

    // ── Pluralizacja liczników ──

    [Theory]
    [InlineData(0, "UI.Roles.RoleCountMany")]
    [InlineData(1, "UI.Roles.RoleCountOne")]
    [InlineData(2, "UI.Roles.RoleCountFew")]
    [InlineData(4, "UI.Roles.RoleCountFew")]
    [InlineData(5, "UI.Roles.RoleCountMany")]
    [InlineData(12, "UI.Roles.RoleCountMany")]
    [InlineData(14, "UI.Roles.RoleCountMany")]
    [InlineData(22, "UI.Roles.RoleCountFew")]
    [InlineData(112, "UI.Roles.RoleCountMany")]
    public void RoleCountKeyFollowsPolishPluralRules(int count, string expectedKey) =>
        Assert.Equal(expectedKey, RolesBrowserProjection.RoleCountKey(count));

    [Fact]
    public void RoleCountPluralsRenderCorrectlyInBothLocales()
    {
        var loc = new LocalizationService();
        Assert.Equal("1 rola", loc.GetFormatted(RolesBrowserProjection.RoleCountKey(1), 1));
        Assert.Equal("2 role", loc.GetFormatted(RolesBrowserProjection.RoleCountKey(2), 2));
        Assert.Equal("5 ról", loc.GetFormatted(RolesBrowserProjection.RoleCountKey(5), 5));
        Assert.Equal("12 ról", loc.GetFormatted(RolesBrowserProjection.RoleCountKey(12), 12));
        loc.ChangeCulture("en");
        Assert.Equal("1 role", loc.GetFormatted(RolesBrowserProjection.RoleCountKey(1), 1));
        Assert.Equal("2 roles", loc.GetFormatted(RolesBrowserProjection.RoleCountKey(2), 2));
        Assert.Equal("5 roles", loc.GetFormatted(RolesBrowserProjection.RoleCountKey(5), 5));
    }

    // ── Stan ładowania / retry ──

    [Fact]
    public void EmptyRolesWithOfflineApiIsErrorOtherwiseEmptyState()
    {
        // RolesService zwraca pustą listę także przy błędzie — offline API odróżnia błąd od pustki.
        Assert.True(RolesBrowserProjection.IsLoadError(0, apiOffline: true));
        Assert.False(RolesBrowserProjection.IsLoadError(0, apiOffline: false));
        Assert.False(RolesBrowserProjection.IsLoadError(3, apiOffline: true));
    }

    // ── Preselekcja deep-linku i przywracanie stanu ──

    [Fact]
    public void ResolveModIdAcceptsOnlyModsPresentInGroups()
    {
        var groups = Build(new[] { V2Role(1, "Sheriff", (10, "Mod A"), (20, "Mod B")) });
        Assert.Equal(10, RolesBrowserProjection.ResolveModId(10, groups));
        Assert.Null(RolesBrowserProjection.ResolveModId(999, groups));
        Assert.Null(RolesBrowserProjection.ResolveModId(null, groups));
    }

    [Fact]
    public void RolesTabSessionStateIsIndependentAndProcessOnly()
    {
        var session = new BrowserSessionState();
        var roles = session.For(MainWindowViewModel.ModBrowserTab.Roles);
        roles.Query = "sheriff";
        roles.ModId = 10;
        roles.RoleId = 5;
        roles.RoleCategory = "Impostor";
        roles.RoleType = "Role";

        Assert.Equal("sheriff", session.For(MainWindowViewModel.ModBrowserTab.Roles).Query);
        Assert.Equal(5, roles.RoleId);
        Assert.Equal("Impostor", roles.RoleCategory);
        Assert.Null(session.For(MainWindowViewModel.ModBrowserTab.Catalog).RoleId);
        Assert.Null(session.For(MainWindowViewModel.ModBrowserTab.Catalog).RoleCategory);

        var next = new BrowserSessionState().For(MainWindowViewModel.ModBrowserTab.Roles);
        Assert.Null(next.RoleId);
        Assert.Null(next.ModId);
        Assert.Empty(next.Query);
    }

    // ── Wiersz roli ──

    [Fact]
    public void RoleListItemExposesDataValuesAndNormalizedCategory()
    {
        var role = new Role
        {
            Name = "Jester",
            Description = "Get voted out",
            Category = "neutral",
            Type = "Role",
            Abilities = new List<Ability> { new() { Name = "Haunt" } }
        };
        var item = new RoleListItem(role);
        Assert.Equal("Jester", item.Name);
        Assert.Equal("neutral", item.CategoryText);
        Assert.Equal("Neutral", item.CategoryKey);
        Assert.Equal("Role", item.TypeText);
        Assert.True(item.HasType);
        Assert.True(item.HasDescription);
        Assert.True(item.HasAbilities);
        Assert.Same(role, item.Role);
    }
}