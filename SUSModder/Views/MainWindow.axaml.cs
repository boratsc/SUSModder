using Avalonia;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using ReactiveUI;
using SUSModder.ViewModels;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SUSModder.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();

        // Nas³uchuj zmiany wybranego moda i aktualizuj opis
        this.WhenActivated(disposables =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.WhenAnyValue(x => x.SelectedMod)
                  .Subscribe(mod =>
                  {
                      if (mod != null)
                          SetDescriptionWithLinks(mod.Description ?? "");
                      else
                          SetDescriptionWithLinks("");
                  });
            }
        });
    }

    /// <summary>
    /// Ustawia opis z klikalnymi linkami w DescriptionPanel (StackPanel).
    /// </summary>
    public void SetDescriptionWithLinks(string description)
    {
        if (this.FindControl<StackPanel>("DescriptionPanel") is not StackPanel panel)
            return;

        panel.Children.Clear();

        if (string.IsNullOrEmpty(description))
            return;

        var regex = new Regex(@"(https?://[^\s]+)", RegexOptions.IgnoreCase);
        int lastIndex = 0;
        foreach (Match match in regex.Matches(description))
        {
            // Dodaj tekst przed linkiem
            if (match.Index > lastIndex)
            {
                var text = description.Substring(lastIndex, match.Index - lastIndex);
                panel.Children.Add(new TextBlock
                {
                    Text = text,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
            }

            // Dodaj klikalny link jako Button
            var link = match.Value;
            var btn = new Button
            {
                Content = new TextBlock
                {
                    Text = link,
                    TextDecorations = Avalonia.Media.TextDecorations.Underline
                },
                Padding = new Thickness(0),
                Background = null,
                BorderThickness = new Thickness(0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            btn.Classes.Add("link");
            btn.Click += (_, __) =>
            {
                try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); } catch { }
            };
            panel.Children.Add(btn);

            lastIndex = match.Index + match.Length;
        }

        // Dodaj tekst po ostatnim linku
        if (lastIndex < description.Length)
        {
            var text = description.Substring(lastIndex);
            panel.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
    }
}
