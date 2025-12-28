using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.Models;
using System.Linq;

namespace SUSModder.Views
{
    public partial class RoleDetailWindow : Window
    {
        public RoleDetailWindow()
        {
            InitializeComponent();
        }
        public RoleDetailWindow(Role role)
        {
            InitializeComponent();
            DataContext = role;
            
            // Ustaw tytuł okna
            Title = $"Szczegóły roli - {role.Name}";
            
            // Pokaż panel zdolności tylko jeśli rola ma zdolności
            AbilitiesPanel.IsVisible = role.Abilities.Any();
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
