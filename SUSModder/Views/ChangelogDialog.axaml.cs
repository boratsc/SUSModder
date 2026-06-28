using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SUSModder.Core.Models;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views
{
    public partial class ChangelogDialog : Window
    {
        private readonly ChangelogData _changelogData;
        private readonly ILocalizationService _localizationService;

        public ChangelogDialog()
        {
            InitializeComponent();

            _changelogData = new ChangelogData();
            _localizationService = null!;
        }

        public ChangelogDialog(ChangelogData changelogData, ILocalizationService localizationService)
            : this()
        {
            _changelogData = changelogData ?? throw new ArgumentNullException(nameof(changelogData));
            _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

            PopulateDialog();
        }

        private void PopulateDialog()
        {
            // Ustaw wersję i datę
            var versionDateKey = "Changelog.VersionDate";
            var versionDateTemplate = _localizationService.Get(versionDateKey);
            VersionDateText.Text = versionDateTemplate
                .Replace("{version}", _changelogData.Version)
                .Replace("{date}", _changelogData.Date);

            // Dodaj sekcje
            foreach (var section in _changelogData.Sections)
            {
                var sectionBorder = CreateSectionElement(section);
                SectionsContainer.Children.Add(sectionBorder);
            }
        }

        private Border CreateSectionElement(ChangelogSection section)
        {
            var border = new Border();
            border.Classes.Add("changelog-section");

            var stackPanel = new StackPanel
            {
                Spacing = 4
            };

            // Tytuł sekcji z ikoną
            if (!string.IsNullOrWhiteSpace(section.Icon) || !string.IsNullOrWhiteSpace(section.Title))
            {
                var titlePanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Margin = new Thickness(0, 0, 0, 6)
                };

                if (!string.IsNullOrWhiteSpace(section.Icon))
                {
                    titlePanel.Children.Add(new TextBlock
                    {
                        Text = section.Icon,
                        FontSize = 16,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                var sectionTitle = ResolveSectionTitle(section);
                if (!string.IsNullOrWhiteSpace(sectionTitle))
                {
                    titlePanel.Children.Add(new TextBlock
                    {
                        Text = sectionTitle,
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                stackPanel.Children.Add(titlePanel);
            }

            // Lista itemów – używamy pojedynczego TextBlock z "• " prefixem
            // (StackPanel poziomy nie pozwala na TextWrapping, bo daje nieskończoną szerokość)
            foreach (var item in section.Items)
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "•  " + item,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Foreground = (IBrush)Application.Current!.FindResource("TextSecondaryBrush")!,
                    Margin = new Thickness(4, 1, 0, 1)
                });
            }

            border.Child = stackPanel;
            return border;
        }

        /// <summary>
        /// Resolves the section title: uses titleKey if available (localized),
        /// otherwise falls back to the literal title field.
        /// </summary>
        private string ResolveSectionTitle(ChangelogSection section)
        {
            if (!string.IsNullOrWhiteSpace(section.TitleKey))
            {
                var localized = _localizationService.Get(section.TitleKey);
                // Jeśli klucz zwrócił się jako [KEY_NAME], nie znaleziono tłumaczenia
                if (!localized.StartsWith("[") || !localized.EndsWith("]"))
                    return localized;
            }

            return section.Title ?? string.Empty;
        }

        private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnGitHubButtonClick(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_changelogData.GithubUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _changelogData.GithubUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChangelogDialog] Błąd otwierania URL: {ex.Message}");
                }
            }

            Close();
        }
    }
}
