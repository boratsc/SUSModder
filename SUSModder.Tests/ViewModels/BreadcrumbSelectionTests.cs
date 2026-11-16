using SUSModder.Core.Models;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// E15: segment breadcrumb górnej belki podąża za zaznaczeniem.
/// Czysta funkcja ResolveBreadcrumbSelection — MainWindowViewModel wymaga DI,
/// więc testujemy projekcję bez Avalonii (wzorzec: RolesBrowserTests, IconAssignmentProjectionTests).
/// </summary>
public class BreadcrumbSelectionTests
{
    private static ModItem Mod(string name) => new() { Name = name };

    private static ModInstanceItem Pack(string name) =>
        new(new ModInstance { DisplayName = name }, null, 0, false);

    [Fact]
    public void CatalogWithSelectionShowsModName() =>
        Assert.Equal("Town of Us", MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.Catalog, Mod("Town of Us"), null, null, ""));

    [Fact]
    public void CatalogWithoutSelectionIsEmpty() =>
        Assert.Equal(string.Empty, MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.Catalog, null, null, null, ""));

    [Fact]
    public void MyPacksWithSelectionShowsPackDisplayName() =>
        Assert.Equal("#ATR", MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.MyPacks, null, Pack("#ATR"), null, ""));

    [Fact]
    public void MyPacksWithoutSelectionIsEmpty() =>
        Assert.Equal(string.Empty, MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.MyPacks, null, null, null, ""));

    [Fact]
    public void DllAddonsWithSelectionShowsModName() =>
        Assert.Equal("Extra Roles", MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.DllAddons, null, null, Mod("Extra Roles"), ""));

    [Fact]
    public void DllAddonsWithoutSelectionIsEmpty() =>
        Assert.Equal(string.Empty, MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.DllAddons, null, null, null, ""));

    [Fact]
    public void RolesLevelBShowsRoleModName() =>
        Assert.Equal("Syzyfowa Beta", MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.Roles, null, null, null, "Syzyfowa Beta"));

    [Fact]
    public void RolesLevelAIsEmpty() =>
        Assert.Equal(string.Empty, MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.Roles, null, null, null, ""));

    /// <summary>Przełączenie zakładki: segment pochodzi wyłącznie z zaznaczenia aktywnej zakładki.</summary>
    [Theory]
    [InlineData(MainWindowViewModel.ModBrowserTab.Catalog, "Town of Us")]
    [InlineData(MainWindowViewModel.ModBrowserTab.MyPacks, "#ATR")]
    [InlineData(MainWindowViewModel.ModBrowserTab.DllAddons, "Extra Roles")]
    [InlineData(MainWindowViewModel.ModBrowserTab.Roles, "Syzyfowa Beta")]
    public void TabSwitchUsesOnlyActiveTabSelection(MainWindowViewModel.ModBrowserTab tab, string expected) =>
        Assert.Equal(expected, MainWindowViewModel.ResolveBreadcrumbSelection(
            tab, Mod("Town of Us"), Pack("#ATR"), Mod("Extra Roles"), "Syzyfowa Beta"));

    /// <summary>Przełączenie na zakładkę bez zaznaczenia czyści segment (nie pokazuje nazwy z innej zakładki).</summary>
    [Fact]
    public void TabSwitchToTabWithoutSelectionIsEmpty()
    {
        var catalogMod = Mod("Town of Us");
        Assert.Equal("Town of Us", MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.Catalog, catalogMod, null, null, ""));
        Assert.Equal(string.Empty, MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.MyPacks, catalogMod, null, null, ""));
        Assert.Equal(string.Empty, MainWindowViewModel.ResolveBreadcrumbSelection(
            MainWindowViewModel.ModBrowserTab.Roles, catalogMod, null, null, ""));
    }
}
