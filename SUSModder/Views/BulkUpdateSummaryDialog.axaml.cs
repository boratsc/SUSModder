using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using SUSModder.Core.Models;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views
{
    /// <summary>
    /// Pojedyncza pozycja wspólnego podsumowania zbiorczej aktualizacji (3.1-06).
    /// </summary>
    public sealed class BulkUpdateSummaryEntry
    {
        public string ModName { get; init; } = string.Empty;
        public string CurrentVersion { get; init; } = string.Empty;
        public string TargetVersion { get; init; } = string.Empty;
        public string TargetAmongVersion { get; init; } = string.Empty;
        public UpdatePreview? Preview { get; init; }
    }

    /// <summary>
    /// Wspólne podsumowanie zbiorczej aktualizacji — jeden dialog dla całej paczki.
    ///
    /// Zasada 3.1-06: jedno podsumowanie dla operacji zbiorczej (bez modala na mod),
    /// a po zatwierdzeniu każda pozycja jest instalowana z przypiętą parą
    /// (wersja moda, wersja gry). Brak danych o dodatkach oznaczany jest jako
    /// „brak danych”, nigdy jako potwierdzona zgodność.
    /// </summary>
    public partial class BulkUpdateSummaryDialog : Window
    {
        /// <summary>
        /// Klucze podsumowania dodatków. Bez nich dialog pokazuje wyłącznie
        /// mody i wersje — bez tekstu nieprzetłumaczonego.
        /// </summary>
        private static readonly string[] RequiredAddonKeys =
        {
            "Dialogs.UpdateMod.Preview.Addon.Retained",
            "Dialogs.UpdateMod.Preview.Addon.RetainedUnknown",
            "Dialogs.UpdateMod.Preview.Addon.Incompatible",
            "Dialogs.UpdateMod.Preview.Addon.MissingCatalog",
            "Dialogs.UpdateMod.Preview.Addon.CustomFile",
            "Dialogs.UpdateMod.Preview.Addon.Unknown"
        };

        public bool Result { get; private set; }

        private readonly ILocalizationService _localizationService;

        public BulkUpdateSummaryDialog()
        {
            InitializeComponent();
            _localizationService = App.GetService<ILocalizationService>();
        }

        public BulkUpdateSummaryDialog(IReadOnlyList<BulkUpdateSummaryEntry> entries)
        {
            InitializeComponent();
            _localizationService = App.GetService<ILocalizationService>();

            var items = entries?.Where(e => e != null).ToList() ?? new List<BulkUpdateSummaryEntry>();

            Title = _localizationService.Get("Dialogs.BulkUpdate.Title");

            TitleText.Text = Title;
            HeaderText.Text = _localizationService.GetFormatted("UI.Bulk.UpdateSelected", items.Count);

            bool hasWarnings = false;

            foreach (var entry in items)
            {
                ModsList.Children.Add(CreateModBlock(entry, ref hasWarnings));
            }

            if (hasWarnings)
            {
                WarningText.Text = _localizationService.Get("Dialogs.BulkUpdate.Warning");
                WarningText.IsVisible = true;
            }

            CancelButton.Content = _localizationService.Get("Dialogs.UpdateMod.CancelButton");
            ConfirmButton.Content = _localizationService.Get("Dialogs.UpdateMod.UpdateButton");
        }

        private Control CreateModBlock(BulkUpdateSummaryEntry entry, ref bool hasWarnings)
        {
            this.TryFindResource("ModCardBackgroundBrush", out var backgroundResource);
            this.TryFindResource("ModCardBorderBrush", out var borderResource);

            var border = new Border
            {
                Background = backgroundResource as IBrush,
                BorderBrush = borderResource as IBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10)
            };

            var panel = new StackPanel { Spacing = 4 };

            var nameBlock = new TextBlock { Text = entry.ModName };
            nameBlock.Classes.Add("bulk-mod-name");
            panel.Children.Add(nameBlock);

            var versionBlock = new TextBlock
            {
                Text = $"{entry.CurrentVersion} → {entry.TargetVersion}"
            };
            versionBlock.Classes.Add("bulk-mod-version");
            panel.Children.Add(versionBlock);

            panel.Children.Add(new TextBlock
            {
                Text = _localizationService.Get("Dialogs.UpdateMod.AmongVersion") + " " +
                    (string.IsNullOrWhiteSpace(entry.TargetAmongVersion)
                        ? _localizationService.Get("Dialogs.UpdateMod.UnknownVersion") : entry.TargetAmongVersion),
                TextWrapping = TextWrapping.Wrap
            });
            var unknown = entry.Preview == null || entry.Preview.ConfigStatus == UpdateConfigStatus.Unknown;
            if (unknown) hasWarnings = true;
            panel.Children.Add(new TextBlock
            {
                Text = _localizationService.Get(unknown
                    ? "Dialogs.UpdateMod.Preview.ConfigUnknown" : "Dialogs.UpdateMod.Preview.ConfigNotGuaranteed"),
                TextWrapping = TextWrapping.Wrap
            });
            if (unknown)
                panel.Children.Add(new TextBlock
                {
                    Text = _localizationService.Get("Dialogs.UpdateMod.Preview.Failed"),
                    TextWrapping = TextWrapping.Wrap
                });

            bool showAddons = HasAddonLocalization();
            if (showAddons && entry.Preview != null)
            {
                foreach (var addon in entry.Preview.Addons)
                {
                    if (addon.RequiresAttention)
                        hasWarnings = true;

                    var addonBlock = new TextBlock
                    {
                        Text = FormatAddonStatus(addon),
                        TextWrapping = TextWrapping.Wrap
                    };
                    addonBlock.Classes.Add("bulk-addon");
                    if (addon.RequiresAttention)
                        addonBlock.Classes.Add("attention");

                    panel.Children.Add(addonBlock);
                }
            }

            border.Child = panel;
            return border;
        }

        private string FormatAddonStatus(UpdateAddonPreview addon)
        {
            string key = addon.Status switch
            {
                UpdateAddonStatus.Retained => "Dialogs.UpdateMod.Preview.Addon.Retained",
                UpdateAddonStatus.RetainedCompatibilityUnknown => "Dialogs.UpdateMod.Preview.Addon.RetainedUnknown",
                UpdateAddonStatus.RetainedIncompatible => "Dialogs.UpdateMod.Preview.Addon.Incompatible",
                UpdateAddonStatus.NotRestoredMissingCatalog => "Dialogs.UpdateMod.Preview.Addon.MissingCatalog",
                UpdateAddonStatus.NotRestoredCustomFile => "Dialogs.UpdateMod.Preview.Addon.CustomFile",
                _ => "Dialogs.UpdateMod.Preview.Addon.Unknown"
            };

            return _localizationService.GetFormatted(key, ViewModels.Helpers.UpdateAddonPresentation.NameWithVersions(addon));
        }

        private bool HasAddonLocalization()
        {
            foreach (var key in RequiredAddonKeys)
            {
                if (!TryGetLocalized(key, out _))
                    return false;
            }

            return true;
        }

        private bool TryGetLocalized(string key, out string value)
        {
            value = _localizationService.Get(key);
            return !string.IsNullOrWhiteSpace(value) && !string.Equals(value, $"[{key}]", StringComparison.Ordinal);
        }

        private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}
