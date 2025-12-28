using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SUSModder.Views
{
    public enum RepairOption
    {
        None,
        BlackScreen,
        Certificates,
        Regions,
        Firewall,
        EpicLogout,
        EpicLogin
    }

    public partial class RepairOptionsDialog : Window
    {
        public RepairOption SelectedOption { get; private set; } = RepairOption.None;
        
        /// <summary>
        /// Czy platforma to Steam (dla włączania/wyłączania opcji Firewall)
        /// </summary>
        public bool IsSteamPlatform { get; set; } = true;

        public RepairOptionsDialog()
        {
            InitializeComponent();
        }
        
        public RepairOptionsDialog(bool isSteamPlatform) : this()
        {
            IsSteamPlatform = isSteamPlatform;
            UpdateFirewallButtonState();
            UpdateEpicAuthSectionState();
        }
        
        private void UpdateFirewallButtonState()
        {
            if (FirewallButton != null)
            {
                FirewallButton.IsEnabled = IsSteamPlatform;
                FirewallButton.Opacity = IsSteamPlatform ? 1.0 : 0.5;
            }
        }
        
        private void UpdateEpicAuthSectionState()
        {
            bool isEpicPlatform = !IsSteamPlatform;
            
            if (EpicAuthSection != null)
            {
                EpicAuthSection.IsEnabled = isEpicPlatform;
                EpicAuthSection.Opacity = isEpicPlatform ? 1.0 : 0.5;
            }
            
            if (EpicLogoutButton != null)
            {
                EpicLogoutButton.IsEnabled = isEpicPlatform;
            }
            
            if (EpicLoginButton != null)
            {
                EpicLoginButton.IsEnabled = isEpicPlatform;
            }
        }

        private void OnBlackScreenClick(object? sender, RoutedEventArgs e)
        {
            SelectedOption = RepairOption.BlackScreen;
            Close();
        }

        private void OnCertificatesClick(object? sender, RoutedEventArgs e)
        {
            SelectedOption = RepairOption.Certificates;
            Close();
        }

        private void OnRegionsClick(object? sender, RoutedEventArgs e)
        {
            SelectedOption = RepairOption.Regions;
            Close();
        }
        
        private void OnFirewallClick(object? sender, RoutedEventArgs e)
        {
            if (IsSteamPlatform)
            {
                SelectedOption = RepairOption.Firewall;
                Close();
            }
        }
        
        private void OnEpicLogoutClick(object? sender, RoutedEventArgs e)
        {
            if (!IsSteamPlatform) // Epic platform
            {
                SelectedOption = RepairOption.EpicLogout;
                Close();
            }
        }
        
        private void OnEpicLoginClick(object? sender, RoutedEventArgs e)
        {
            if (!IsSteamPlatform) // Epic platform
            {
                SelectedOption = RepairOption.EpicLogin;
                Close();
            }
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            SelectedOption = RepairOption.None;
            Close();
        }
    }
}
