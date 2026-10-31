using System;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.ViewModels;

/// <summary>
/// 3.1-11: świadoma instalacja dostępnej bety z panelu szczegółów.
/// Domyślna ścieżka (przycisk „Zainstaluj” i auto-update) pozostaje stabilna.
/// </summary>
public partial class MainWindowViewModel
{
    private ReactiveCommand<ModItem, Unit>? _installAvailableBetaCommand;

    public ReactiveCommand<ModItem, Unit> InstallAvailableBetaCommand =>
        _installAvailableBetaCommand ??= ReactiveCommand.CreateFromTask<ModItem>(InstallAvailableBetaAsync);

    private async Task InstallAvailableBetaAsync(ModItem? modItem)
    {
        if (modItem is null || modItem.IsInstalling || modItem.IsVanilla)
            return;

        var beta = modItem.AvailableReleases.FirstOrDefault(r =>
                       r.IsBeta &&
                       string.Equals(r.Version, modItem.LatestBetaVersion, StringComparison.OrdinalIgnoreCase))
                   ?? modItem.AvailableReleases.FirstOrDefault(r => r.IsBeta);

        if (beta is null)
            return;

        var message = _localizationService.GetFormatted(
            "UI.Beta.ConfirmMessage",
            modItem.Name,
            beta.Version,
            string.IsNullOrWhiteSpace(beta.AmongVersion) ? modItem.AmongVersion : beta.AmongVersion);

        bool proceed = await ShowConfirmDialogAsync(
            message,
            _localizationService.Get("UI.Beta.ConfirmTitle"));

        if (!proceed)
            return;

        var configService = new ConfigService();
        var allConfigs = configService.LoadConfig();
        var modConfig = allConfigs.FirstOrDefault(c => c.Id == modItem.Id);

        if (modConfig is null)
        {
            await _userInteractionService.ShowErrorAsync(
                _localizationService.Get("ModOperations.ConfigNotFound"),
                _localizationService.Get("MainWindow.ErrorTitle"));
            return;
        }

        var selectedVersion = new ModVersionHistory
        {
            ModId = modItem.Id,
            ModVersion = beta.Version,
            AmongVersion = string.IsNullOrWhiteSpace(beta.AmongVersion) ? modItem.AmongVersion : beta.AmongVersion,
            IsBeta = true
        };

        await InstallSpecificVersionAsync(modItem, modConfig, selectedVersion, allConfigs);
    }
}
