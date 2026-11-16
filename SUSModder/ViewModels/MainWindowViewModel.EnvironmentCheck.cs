using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SUSModder.Core.Diagnostics.Environment;
using SUSModder.Core.Utilities;
using SUSModder.Views;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial: raport sprawdzenia środowiska (tylko odczyt) — z panelu napraw
    /// i z koordynatora startowego w App.
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>
        /// Otwiera raport sprawdzenia środowiska z kontekstem z ustawień użytkownika.
        /// Zwraca raport (null, gdy okno zamknięto przed wynikiem). Po kliknięciu
        /// „Narzędzie firewalla" w raporcie dispatchuje RepairOption.Firewall.
        /// </summary>
        internal async Task<SelfCheckReport?> ShowEnvironmentCheckReportAsync()
        {
            var settings = _userSettingsService.LoadUserSettings();
            var context = new SelfCheckContext
            {
                Mode = string.IsNullOrWhiteSpace(settings.Mode) ? "steam" : settings.Mode,
                VanillaInstallPath = string.IsNullOrWhiteSpace(settings.VanillaInstallPath) ? null : settings.VanillaInstallPath,
                ModsInstallPath = PathSettings.ModsInstallPath
            };

            var dialog = new EnvironmentCheckReportDialog(context, _localizationService);
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow == null)
            {
                dialog.Show();
                return null;
            }

            await dialog.ShowDialog(mainWindow);

            if (dialog.FirewallToolRequested)
            {
                await ExecuteRepairOptionAsync(RepairOption.Firewall);
            }

            return dialog.Report;
        }
    }
}
