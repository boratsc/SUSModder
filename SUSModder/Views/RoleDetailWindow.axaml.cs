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
            LoadRoleData(role);
        }

        private void LoadRoleData(Role role)
        {
            Title = $"Szczegóły roli - {role.Name}";
            RoleName.Text = role.Name;
            RoleCategory.Text = role.Category;
            RoleType.Text = role.Type;
            RoleDescription.Text = role.Description;
            ModName.Text = role.ModName;

            // Pokaż panel zdolności tylko jeśli rola ma zdolności
            AbilitiesPanel.IsVisible = role.Abilities.Any();
            AbilitiesItemsControl.ItemsSource = role.Abilities;
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
