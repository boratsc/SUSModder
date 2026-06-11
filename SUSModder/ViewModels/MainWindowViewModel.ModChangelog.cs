using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services;
using SUSModder.Views;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    private async Task OpenModChangelogAsync()
    {
        var selectedMod = SelectedMod;
        if (selectedMod == null || selectedMod.Id <= 0)
            return;

        try
        {
            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(SUSModder.Core.Utilities.ApplicationPaths.GetApplicationDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            var configuration = configBuilder.Build();

            var diagnosticsOutput = new UIDiagnosticsOutput(message =>
                System.Diagnostics.Debug.WriteLine($"[ModChangelog] {message}"));

            var apiClient = new SUSModderApiClient(configuration, diagnosticsOutput);
            var changelogService = new ModChangelogService(apiClient, diagnosticsOutput);

            var lang = _localizationService.CurrentCulture == "en" ? "en" : "pl";
            var viewModel = new ModChangelogViewModel(
                changelogService,
                _localizationService,
                selectedMod.Id,
                selectedMod.Name,
                lang,
                limit: 10);

            var dialog = new ModChangelogDialog(viewModel);

            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModChangelog] Failed to open dialog: {ex.Message}");
        }
    }

}
