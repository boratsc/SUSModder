using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views
{
    public partial class DllUpdateConfirmDialog : Window
    {
        public bool Result { get; private set; }
        private readonly ILocalizationService _localizationService;

        public DllUpdateConfirmDialog()
        {
            _localizationService = App.GetService<ILocalizationService>();
            InitializeComponent();
        }

        public DllUpdateConfirmDialog(DllUpdateInfo updateInfo) : this()
        {
            // Nazwa moda DLL
            var dllModNameText = this.FindControl<TextBlock>("DllModNameText");
            if (dllModNameText != null)
            {
                dllModNameText.Text = updateInfo.DllMod.ModName;
            }

            // Nowa wersja
            var newVersionText = this.FindControl<TextBlock>("NewVersionText");
            if (newVersionText != null)
            {
                newVersionText.Text = updateInfo.NewVersion;
            }

            // Lista lokalizacji z ich wersjami
            var locationsItemsControl = this.FindControl<ItemsControl>("LocationsItemsControl");
            if (locationsItemsControl != null)
            {
                locationsItemsControl.ItemsSource = updateInfo.LocationUpdates;
            }

            // Liczba lokalizacji
            var locationCountText = this.FindControl<TextBlock>("LocationCountText");
            if (locationCountText != null)
            {
                int count = updateInfo.LocationUpdates?.Count ?? 0;
                locationCountText.Text = count == 1 
                    ? _localizationService.Get("DllManager.UpdateConfirm.LocationCountSingular")
                    : _localizationService.GetFormatted("DllManager.UpdateConfirm.LocationCount", count);
            }

            // Tytuł okna
            this.Title = _localizationService.GetFormatted("DllManager.UpdateConfirm.WindowTitle", updateInfo.DllMod.ModName);
        }

        private void ConfirmButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void CancelButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}
