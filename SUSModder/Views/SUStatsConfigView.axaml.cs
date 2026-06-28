using Avalonia.Controls;
using Avalonia.LogicalTree;
using SUSModder.ViewModels;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services.Discord;
using SUSModder.Core.Data;
using SUSModder.Services;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views
{
    public partial class SUStatsConfigView : UserControl
    {
        public SUStatsConfigView()
        {
            InitializeComponent();
            DataContext = CreateViewModel();
        }

        private static SUStatsConfigViewModel CreateViewModel()
        {
            // Próba utworzenia przez DI, fallback do design-time konstruktora
            try
            {
                var diag = App.GetService<IDiagnosticsOutput>();
                var loc = App.GetService<ILocalizationService>();
                var discordOAuth = App.GetService<IDiscordOAuthService>();
                var clair = App.GetService<IClairDiscordService>();
                var sustatsRepo = App.GetService<ISustatsCredentialsRepository>();
                var userSettingsRepo = App.GetService<IUserSettingsRepository>();
                var discordAuthRepo = App.GetService<IDiscordAuthRepository>();
                var loopback = App.GetService<OAuthLoopbackListener>();

                return new SUStatsConfigViewModel(
                    discordOAuth, clair, sustatsRepo, userSettingsRepo,
                    discordAuthRepo, diag, loc, loopback);
            }
            catch
            {
                // Fallback: design-time / brak DI
                return new SUStatsConfigViewModel();
            }
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var mainWindow = this.FindLogicalAncestorOfType<MainWindow>();
            if (mainWindow?.DataContext is MainWindowViewModel vm)
            {
                vm.IsSUStatsConfigVisible = false;
            }
        }
    }
}
