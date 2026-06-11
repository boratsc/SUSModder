using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using SUSModder.ViewModels;

namespace SUSModder.Views;

public partial class ModChangelogDialog : Window
{
    private readonly ModChangelogViewModel _viewModel;

    public ModChangelogDialog() : this(null!)
    {
    }

    public ModChangelogDialog(ModChangelogViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? new ModChangelogViewModel(
            null!, null!, 0, string.Empty, "pl");

        DataContext = _viewModel;

        _viewModel.CloseRequested += (_, _) => Close();
        _viewModel.Entries.CollectionChanged += OnEntriesChanged;

        Loaded += async (_, _) => await _viewModel.LoadChangelogAsync();
    }

    private static bool TryRes(object key, out IBrush? brush)
    {
        if (Avalonia.Application.Current?.TryFindResource(key, out var res) == true && res is IBrush b)
        {
            brush = b;
            return true;
        }
        brush = null;
        return false;
    }

    private static IBrush? Res(object key)
    {
        return TryRes(key, out var brush) ? brush : null;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PopulateEntries();
    }

    private void PopulateEntries()
    {
        EntriesPanel.Children.Clear();

        foreach (var entry in _viewModel.Entries)
        {
            EntriesPanel.Children.Add(CreateEntryCard(entry));
        }
    }

    private Border CreateEntryCard(ChangelogEntryItem entry)
    {
        var stack = new StackPanel { Spacing = 6 };

        // Row 1: version + release name + date
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var leftStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        leftStack.Children.Add(new TextBlock
        {
            Text = entry.Version,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = Res("TextPrimaryBrush") ?? Brushes.White
        });
        if (!string.IsNullOrWhiteSpace(entry.ReleaseName))
        {
            leftStack.Children.Add(new TextBlock
            {
                Text = entry.ReleaseName,
                FontSize = 13,
                Foreground = Res("TextSecondaryBrush") ?? Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        headerGrid.Children.Add(leftStack);
        Grid.SetColumn(leftStack, 0);

        if (!string.IsNullOrWhiteSpace(entry.PublishedAtFormatted))
        {
            var dateBlock = new TextBlock
            {
                Text = entry.PublishedAtFormatted,
                FontSize = 11,
                Foreground = Res("TextSecondaryBrush") ?? Brushes.DimGray,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerGrid.Children.Add(dateBlock);
            Grid.SetColumn(dateBlock, 1);
        }

        stack.Children.Add(headerGrid);

        // Row 2: body (markdown → Inlines)
        if (entry.HasBody)
        {
            var bodyBlock = BuildMarkdownTextBlock(entry.Body);
            stack.Children.Add(bodyBlock);
        }

        // Row 3: release link
        if (entry.HasReleaseUrl)
        {
            var linkBtn = new Button
            {
                Content = _viewModel.OpenReleaseText,
                FontSize = 12,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Res("AccentBrush") ?? Brushes.DodgerBlue,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            linkBtn.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(entry.ReleaseUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = entry.ReleaseUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ModChangelog] Failed to open URL: {ex.Message}");
                    }
                }
            };
            stack.Children.Add(linkBtn);
        }

        return new Border
        {
            Background = Res("ModCardBackgroundBrush") ?? Brushes.Transparent,
            BorderBrush = Res("ModCardBorderBrush") ?? Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = stack
        };
    }

    /// <summary>
    /// Parses simple Markdown into Avalonia Inline elements.
    /// Supports: **bold**, *italic*, `code`, - bullets, # headers, [links](url).
    /// </summary>
    private static TextBlock BuildMarkdownTextBlock(string markdown)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = Res("TextSecondaryBrush") ?? Brushes.Gray,
            Margin = new Thickness(0, 2, 0, 2)
        };

        var inlines = textBlock.Inlines!;
        var lines = markdown.Split('\n');
        bool firstLine = true;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
            {
                if (!firstLine && inlines.Count > 0)
                    inlines.Add(new LineBreak());
                continue;
            }

            if (!firstLine && inlines.Count > 0)
                inlines.Add(new LineBreak());

            // Header: ### or ## or #
            var headerMatch = Regex.Match(line, @"^(#{1,3})\s+(.+)$");
            if (headerMatch.Success)
            {
                int level = headerMatch.Groups[1].Length;
                double fontSize = level switch { 1 => 16, 2 => 14, _ => 13 };
                var fontWeight = level <= 2 ? FontWeight.Bold : FontWeight.SemiBold;
                inlines.Add(new Run(headerMatch.Groups[2].Value)
                {
                    FontSize = fontSize,
                    FontWeight = fontWeight
                });
                firstLine = false;
                continue;
            }

            // Bullet: - item or * item
            var bulletMatch = Regex.Match(line, @"^(\s*)[-*]\s+(.+)$");
            if (bulletMatch.Success)
            {
                inlines.Add(new Run("  • "));
                AppendInlinesFromMarkdownSpan(inlines, bulletMatch.Groups[2].Value);
                firstLine = false;
                continue;
            }

            // Numbered list: 1. item
            var numMatch = Regex.Match(line, @"^(\s*)\d+\.\s+(.+)$");
            if (numMatch.Success)
            {
                var indent = numMatch.Groups[1].Value;
                var numText = Regex.Match(line, @"^\s*(\d+\.)").Groups[1].Value;
                var rest = numMatch.Groups[2].Value;
                inlines.Add(new Run($"  {indent}{numText} "));
                AppendInlinesFromMarkdownSpan(inlines, rest);
                firstLine = false;
                continue;
            }

            // Regular paragraph
            AppendInlinesFromMarkdownSpan(inlines, line);
            firstLine = false;
        }

        return textBlock;
    }

    private static void AppendInlinesFromMarkdownSpan(InlineCollection inlines, string span)
    {
        var pattern = @"(\*\*(.+?)\*\*)|(\*(.+?)\*)|(`(.+?)`)|(\[(.+?)\]\((.+?)\))";
        int lastIndex = 0;

        foreach (Match m in Regex.Matches(span, pattern))
        {
            if (m.Index > lastIndex)
            {
                inlines.Add(new Run(span[lastIndex..m.Index]));
            }

            if (m.Groups[1].Success)
            {
                inlines.Add(new Run(m.Groups[2].Value) { FontWeight = FontWeight.Bold });
            }
            else if (m.Groups[3].Success)
            {
                inlines.Add(new Run(m.Groups[4].Value) { FontStyle = FontStyle.Italic });
            }
            else if (m.Groups[5].Success)
            {
                inlines.Add(new Run(m.Groups[6].Value)
                {
                    FontFamily = new FontFamily("Consolas, Courier New, monospace")
                });
            }
            else if (m.Groups[7].Success)
            {
                var linkText = m.Groups[9].Value;
                var linkUrl = m.Groups[10].Value;
                inlines.Add(new Run(linkText + " ")
                {
                    Foreground = Res("AccentBrush") ?? Brushes.DodgerBlue,
                    TextDecorations = TextDecorations.Underline
                });
                inlines.Add(new Run($"({linkUrl})")
                {
                    FontSize = 11,
                    Foreground = Brushes.Gray
                });
            }

            lastIndex = m.Index + m.Length;
        }

        if (lastIndex < span.Length)
        {
            inlines.Add(new Run(span[lastIndex..]));
        }
    }
}
