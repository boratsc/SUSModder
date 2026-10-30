using SUSModder.ViewModels.ModPackWizard;

namespace SUSModder.Tests.ViewModels.ModPackWizard;

/// <summary>
/// Allowlista źródeł spoza katalogu — te same reguły co w produkcyjnym formularzu:
/// tylko publiczny GitHub release asset i ścieżka DLL wyłącznie pod BepInEx/plugins.
/// </summary>
public class ModPackWizardSourceGuardTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo/releases/download/v1.0/Mod.zip", true)]
    [InlineData("https://github.com/Owner/Repo/releases/download/tag/file.dll", true)]
    [InlineData("http://github.com/owner/repo/releases/download/v1.0/Mod.zip", false)]          // brak TLS
    [InlineData("https://www.github.com/owner/repo/releases/download/v1.0/Mod.zip", false)]     // obcy host
    [InlineData("https://evil.com/github.com/owner/releases/download/v/Mod.zip", false)]        // maskarada
    [InlineData("https://user:pw@github.com/owner/repo/releases/download/v/Mod.zip", false)]    // userinfo
    [InlineData("https://github.com/owner/repo/releases/tag/v1.0", false)]                       // nie asset
    [InlineData("https://github.com/owner/repo/releases/download/onlytag", false)]              // brak pliku
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void GithubReleaseAssetAllowlist(string? url, bool expected) =>
        Assert.Equal(expected, ModPackWizardSourceGuard.IsAllowedGithubReleaseAssetUrl(url));

    [Theory]
    [InlineData("BepInEx/plugins", true)]
    [InlineData("BepInEx/plugins/subdir", true)]
    [InlineData("bepinex\\plugins\\x.dll", true)]
    [InlineData("", true)] // pusta = domyślna ścieżka instalatora
    [InlineData(null, true)]
    [InlineData("..", false)]
    [InlineData("BepInEx", false)]
    [InlineData("C:\\Windows\\plugins", false)]
    [InlineData("\\shared\\plugins", false)]
    [InlineData("BepInEx/plugins/../../evil", false)]
    public void DllInstallPathMustStayUnderPlugins(string? path, bool expected) =>
        Assert.Equal(expected, ModPackWizardSourceGuard.IsSafeDllInstallPath(path));

    [Fact]
    public void SuggestArtifactName_UsesAssetAndRepo()
    {
        var name = ModPackWizardSourceGuard.SuggestArtifactName(
            "https://github.com/myorg/townofus/releases/download/v5.0/ToU.dll");
        Assert.Equal("ToU (townofus)", name);

        var same = ModPackWizardSourceGuard.SuggestArtifactName(
            "https://github.com/myorg/ToU/releases/download/v5.0/ToU.dll");
        Assert.Equal("ToU", same);

        Assert.Null(ModPackWizardSourceGuard.SuggestArtifactName("https://example.com/x"));
    }
}
