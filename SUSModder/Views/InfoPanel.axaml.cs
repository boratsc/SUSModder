using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace SUSModder.Views
{
    public partial class InfoPanel : UserControl
    {
        private const string DiscordInviteUrl = "https://discord.gg/yHndwudMcX";
        private const string ContactEmail = "boratsc@gmail.com";

        public InfoPanel()
        {
            InitializeComponent();
        }

        private void OpenDiscord_Click(object? sender, RoutedEventArgs e)
            => OpenUrl(DiscordInviteUrl);

        private void OpenMail_Click(object? sender, RoutedEventArgs e)
            => OpenUrl($"mailto:{ContactEmail}");

        private void OpenSupport_Click(object? sender, RoutedEventArgs e)
            => Services.ProjectSupport.Open();

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}
