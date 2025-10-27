using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using SUSModder.Core.Services.Localization;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SUSModder.Views
{
    public partial class ConsoleWindow : Window
    {
        private readonly ObservableCollection<LogEntry> _logEntries = new();
        private static ConsoleWindow? _instance;
        private readonly ILocalizationService _localizationService;

        public ConsoleWindow()
        {
            InitializeComponent();
            _localizationService = App.GetService<ILocalizationService>();
            
            LogItemsControl.ItemsSource = _logEntries;
            _instance = this;

            Closing += (s, e) => _instance = null;
        }

        public static ConsoleWindow? Instance => _instance;

        public static void ShowConsole()
        {
            if (_instance == null)
            {
                _instance = new ConsoleWindow();
            }

            _instance.Show();
            _instance.Activate();
        }

        public static void WriteLog(string message, LogLevel level = LogLevel.Info)
        {
            if (_instance != null)
            {
                _instance.AddLogEntry(message, level);
            }
        }

        private void AddLogEntry(string message, LogLevel level)
        {
            var entry = new LogEntry
            {
                Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                Level = level
            };

            _logEntries.Add(entry);
            UpdateLogCount();

            // Auto-scroll jeśli włączone
            if (AutoScrollCheckBox.IsChecked == true)
            {
                ScrollToBottom();
            }

            // Ogranicz liczbę wpisów (np. do 1000)
            if (_logEntries.Count > 1000)
            {
                _logEntries.RemoveAt(0);
            }
        }

        private void UpdateLogCount()
        {
            var count = _logEntries.Count;
            LogCountText.Text = count == 1 
                ? _localizationService.Get("Console.EntriesCountSingular")
                : _localizationService.GetFormatted("Console.EntriesCount", count);
        }

        private void ScrollToBottom()
        {
            LogScrollViewer.ScrollToEnd();
        }

        private void OnClearClick(object? sender, RoutedEventArgs e)
        {
            _logEntries.Clear();
            UpdateLogCount();
            StatusText.Text = _localizationService.Get("Console.StatusCleared");
        }

        private async void OnCopyClick(object? sender, RoutedEventArgs e)
        {
            var allLogs = string.Join(Environment.NewLine, _logEntries.Select(l => l.Text));

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(allLogs);
                StatusText.Text = _localizationService.Get("Console.StatusCopied");
            }
        }

        private async void OnSaveClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null) return;

                var defaultFileName = _localizationService.GetFormatted("Console.SaveDialogDefaultName", 
                    DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));

                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = _localizationService.Get("Console.SaveDialogTitle"),
                    DefaultExtension = "txt",
                    SuggestedFileName = defaultFileName,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType(_localizationService.Get("Console.SaveDialogTextFiles")) 
                            { Patterns = new[] { "*.txt" } },
                        new FilePickerFileType(_localizationService.Get("Console.SaveDialogAllFiles")) 
                            { Patterns = new[] { "*" } }
                    }
                });

                if (file != null)
                {
                    var allLogs = string.Join(Environment.NewLine, _logEntries.Select(l => l.Text));
                    await using var stream = await file.OpenWriteAsync();
                    await using var writer = new StreamWriter(stream, Encoding.UTF8);
                    await writer.WriteAsync(allLogs);

                    StatusText.Text = _localizationService.GetFormatted("Console.StatusSaved", file.Name);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = _localizationService.GetFormatted("Console.StatusSaveError", ex.Message);
            }
        }
    }

    public class LogEntry
    {
        public string Text { get; set; } = string.Empty;
        public LogLevel Level { get; set; } = LogLevel.Info;

        public bool IsInfo => Level == LogLevel.Info;
        public bool IsWarning => Level == LogLevel.Warning;
        public bool IsError => Level == LogLevel.Error;
        public bool IsDebug => Level == LogLevel.Debug;
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
