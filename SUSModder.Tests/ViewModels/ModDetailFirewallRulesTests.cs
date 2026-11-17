using SUSModder.Services.Localization;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Etap 2: „Reguły firewalla" w menu „Więcej" karty moda — wejście per-mod z preselekcją
/// (bez dialogu wyboru). MainWindowViewModel nie jest konstruowalny w testach (ciężkie DI/Avalonia,
/// brak headless) — wiring pilnowany na źródle, wzorzec: LaunchDiagnosticsFirewallToolTests.
/// </summary>
public sealed class ModDetailFirewallRulesTests
{
    [Fact]
    public void MenuItemLabel_IsLocalizedInPlAndEn()
    {
        var loc = new LocalizationService();
        loc.ChangeCulture("pl");
        Assert.Equal("Reguły firewalla", loc.Get("UI.Buttons.FirewallRules"));
        loc.ChangeCulture("en");
        Assert.Equal("Firewall rules", loc.Get("UI.Buttons.FirewallRules"));
    }

    [Fact]
    public void MenuItem_BindsCommandWithModParameter_OnlyForInstalledFullNonVanilla()
    {
        var axaml = ReadView("ModDetailDrawer.axaml");
        var start = axaml.IndexOf("UI.Buttons.FirewallRules", StringComparison.Ordinal);
        Assert.True(start >= 0, "Brak pozycji UI.Buttons.FirewallRules w menu drawera");
        var block = axaml[start..axaml.IndexOf("</MenuItem>", start, StringComparison.Ordinal)];

        Assert.Contains("DataContext.OpenModFirewallRulesCommand}", block);
        Assert.Contains("CommandParameter=\"{Binding}\"", block);
        // Widoczność: zainstalowany mod full, nie vanilla (vanilla obsługuje globalne narzędzie).
        Assert.Contains("BoolConverters.And", block);
        Assert.Contains("<Binding Path=\"IsInstalled\"/>", block);
        Assert.Contains("<Binding Path=\"IsFullMod\"/>", block);
        Assert.Contains("<Binding Path=\"IsVanilla\" Converter=\"{x:Static BoolConverters.Not}\"/>", block);
        Assert.Contains("fic:Symbol.ShieldKeyhole", block);
        // Pozycja przed separatorem akcji destrukcyjnych (Usuń).
        Assert.True(start < axaml.IndexOf("UI.Buttons.Delete", StringComparison.Ordinal));
    }

    [Fact]
    public void Command_ReusesBuildCandidates_AndPreselectsSingleMod()
    {
        var vm = ReadViewModel("MainWindowViewModel.ExternalActions.cs");
        Assert.Contains("ReactiveCommand.CreateFromTask<ModItem>(OpenModFirewallRulesAsync)", vm);

        var start = vm.IndexOf("private async Task OpenModFirewallRulesAsync", StringComparison.Ordinal);
        var method = vm[start..vm.IndexOf("#endregion", start, StringComparison.Ordinal)];
        // Ścieżki Steam/Epic wyłącznie z BuildCandidates — zero duplikacji logiki.
        Assert.Contains("FirewallToolPlan.BuildCandidates(", method);
        Assert.Contains(".FirstOrDefault(c => !c.IsVanilla)", method);
        Assert.DoesNotContain("EpicInstallPathResolver", method);
        Assert.DoesNotContain("Among Us.exe", method);
        // Brak kandydata (np. brak exe Steam) → neutralny komunikat, nie crash.
        Assert.Contains("_localizationService.Get(\"UI.Repair.Firewall.ExeNotFound\")", method);
        Assert.Contains("await ExecuteFixFirewallAsync(candidate);", method);
    }

    [Fact]
    public void GlobalEntries_StillUseSelectionDialog()
    {
        var vm = ReadViewModel("MainWindowViewModel.ExternalActions.cs");
        Assert.Contains("private Task ExecuteFixFirewallAsync() => ExecuteFixFirewallAsync(preselected: null);", vm);
        Assert.Contains("await ExecuteFixFirewallAsync();", vm); // dispatch RepairOption.Firewall
        Assert.Contains("public Task OpenFirewallToolAsync() => ExecuteRepairOptionAsync(RepairOption.Firewall);",
            ReadViewModel("MainWindowViewModel.LaunchDiagnostics.cs"));
    }

    [Fact]
    public void Preselection_SkipsSelectionDialog_ButKeepsValidationOwnershipAndElevation()
    {
        var vm = ReadViewModel("MainWindowViewModel.ExternalActions.cs");
        var start = vm.IndexOf("private async Task ExecuteFixFirewallAsync(FirewallModItem? preselected)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var method = vm[start..vm.IndexOf("private async Task ShowFirewallNameConflictAsync", start, StringComparison.Ordinal)];

        // Dialog wyboru tylko w gałęzi bez preselekcji, dokładnie raz w całym pliku.
        var guard = method.IndexOf("var selected = preselected;", StringComparison.Ordinal);
        var nullBranch = method.IndexOf("if (selected == null)", StringComparison.Ordinal);
        var dialog = method.IndexOf("new FirewallModSelectionDialog(", StringComparison.Ordinal);
        Assert.True(guard >= 0 && nullBranch > guard && dialog > nullBranch);
        Assert.Equal(1, vm.Split("new FirewallModSelectionDialog(").Length - 1);

        // Reszta flow bez zmian i w tej samej kolejności co przy wyborze z dialogu.
        var validation = method.IndexOf("FirewallRuleService.IsValidProgramPath", StringComparison.Ordinal);
        var confirm = method.IndexOf("UI.Repair.Firewall.ConfirmMessage", StringComparison.Ordinal);
        var ownership = method.IndexOf("FirewallToolPlan.Decide(", StringComparison.Ordinal);
        var conflict = method.IndexOf("ShowFirewallNameConflictAsync()", StringComparison.Ordinal);
        var elevation = method.IndexOf("Verb = \"runas\"", StringComparison.Ordinal);
        var uacCancel = method.IndexOf("NativeErrorCode == 1223", StringComparison.Ordinal);
        var cleanup = method.IndexOf("File.Delete(tempFile)", StringComparison.Ordinal);
        Assert.True(dialog < validation, "walidacja ścieżki po wyborze/preselekcji");
        Assert.True(validation < confirm, "potwierdzenie po walidacji");
        Assert.True(confirm < ownership, "ownership pre-check po potwierdzeniu");
        Assert.True(ownership < conflict && conflict < elevation, "konflikt nazw przed elewacją");
        Assert.True(elevation < uacCancel && uacCancel < cleanup, "obsługa anulowania UAC i sprzątanie");
    }

    private static string ReadViewModel(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "SUSModder", "ViewModels", fileName));

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "SUSModder", "Views", fileName));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Views", "MainWindow.axaml"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}