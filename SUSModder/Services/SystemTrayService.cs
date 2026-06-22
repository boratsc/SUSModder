using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WinFormsApp = System.Windows.Forms.Application;

namespace SUSModder.Services
{
    /// <summary>
    /// Zarządza ikonką w zasobniku systemowym (system tray).
    /// Używa System.Windows.Forms.NotifyIcon (dostępne przez UseWindowsForms w csproj).
    /// </summary>
    public sealed class SystemTrayService : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private Icon? _trayIcon;
        private ContextMenuStrip? _contextMenu;
        private Window? _mainWindow;
        private bool _isVisible;
        private bool _disposed;
        private readonly UserSettingsService _userSettingsService;
        private readonly ILocalizationService? _localizationService;

        // Zapamiętane ostatnio uruchamiane mody (max 3)
        private readonly List<TrayModInfo> _recentMods = new();

        // Event dla MainWindow (przywracanie okna)
        public event Action? RestoreRequested;

        public SystemTrayService()
        {
            _userSettingsService = new UserSettingsService();
            _localizationService = App.GetService<ILocalizationService>();
        }

        /// <summary>
        /// Czy usługa jest aktywna (ikona widoczna w zasobniku).
        /// </summary>
        public bool IsVisible => _isVisible;

        /// <summary>
        /// Inicjalizuje ikonkę tray i podpina do głównego okna.
        /// Wywoływana po załadowaniu MainWindow.
        /// </summary>
        public void Initialize(Window mainWindow)
        {
            _mainWindow = mainWindow;

            if (_notifyIcon != null)
                return;

            _trayIcon = LoadTrayIcon();

            _contextMenu = new ContextMenuStrip();
            _contextMenu.Font = new Font("Segoe UI", 9F);
            _contextMenu.RenderMode = ToolStripRenderMode.Professional;
            _contextMenu.Opening += ContextMenu_Opening;

            _notifyIcon = new NotifyIcon
            {
                Icon = _trayIcon,
                Text = "SUSModder",
                ContextMenuStrip = _contextMenu,
                Visible = false
            };

            // Kliknięcie ikonki przywraca okno
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    RestoreWindow();
                }
            };

            Debug.WriteLine("[SystemTrayService] Zainicjalizowano");
        }

        /// <summary>
        /// Ładuje ikonę tray z zasobów Avalonia (nie z ikony procesu hosta — dotnet.exe przy F5).
        /// </summary>
        private static Icon LoadTrayIcon()
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://SUSModder/Assets/icon.ico"));
                return new Icon(stream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemTrayService] avares icon.ico: {ex.Message}");
            }

            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
                if (File.Exists(path))
                    return new Icon(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemTrayService] Assets/icon.ico: {ex.Message}");
            }

            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath) &&
                    exePath.Contains("SUSModder", StringComparison.OrdinalIgnoreCase))
                {
                    var extracted = Icon.ExtractAssociatedIcon(exePath);
                    if (extracted != null)
                        return extracted;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemTrayService] ExtractAssociatedIcon: {ex.Message}");
            }

            Debug.WriteLine("[SystemTrayService] Fallback: SystemIcons.Application");
            return SystemIcons.Application;
        }

        /// <summary>
        /// Pokazuje ikonkę w zasobniku.
        /// </summary>
        public void Show()
        {
            if (_notifyIcon == null || _disposed)
                return;

            _isVisible = true;
            _notifyIcon.Visible = true;
            Debug.WriteLine("[SystemTrayService] Ikonka widoczna w zasobniku");
        }

        /// <summary>
        /// Ukrywa ikonkę z zasobnika.
        /// </summary>
        public void Hide()
        {
            if (_notifyIcon == null || _disposed)
                return;

            _isVisible = false;
            _notifyIcon.Visible = false;
            Debug.WriteLine("[SystemTrayService] Ikonka ukryta z zasobnika");
        }

        /// <summary>
        /// Wyświetla dymek systemowy (balloon tip) nad ikonką tray.
        /// </summary>
        public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 5000)
        {
            if (_notifyIcon == null || _disposed)
                return;

            _notifyIcon.ShowBalloonTip(timeoutMs, title, text, icon);
        }

        /// <summary>
        /// Wyświetla dymek powitalny przy pierwszym minimalizowaniu do tray (jeśli jeszcze nie był wyświetlony).
        /// </summary>
        public void ShowFirstMinimizeNotificationIfNeeded()
        {
            if (_notifyIcon == null || _disposed)
                return;

            var settings = _userSettingsService.LoadUserSettings();
            if (settings.TrayFirstMinimizeShown)
                return;

            settings.TrayFirstMinimizeShown = true;
            _userSettingsService.SaveUserSettings(settings);

            var title = _localizationService?.Get("SystemTray.FirstMinimize.Title") ?? "SUSModder";
            var message = _localizationService?.Get("SystemTray.FirstMinimize.Message")
                ?? "Application has been minimized to the system tray.\n\nClick the tray icon to restore the window.";

            ShowBalloonTip(title, message, ToolTipIcon.Info, 8000);
        }

        /// <summary>
        /// Przywraca główne okno.
        /// Ikona w zasobniku pozostaje widoczna, jeśli użytkownik włączył minimalizowanie do tray.
        /// </summary>
        public void RestoreWindow()
        {
            if (_mainWindow == null)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
                _mainWindow.Focus();

                RestoreRequested?.Invoke();
            });
        }

        /// <summary>
        /// Zamyka aplikację (po potwierdzeniu w menu tray).
        /// Deleguje do MainWindow.ForceClose() aby poprawnie przejść przez OnClosing z cleanup.
        /// </summary>
        private void ExitApplication()
        {
            Hide();

            Dispatcher.UIThread.Post(() =>
            {
                // Użyj ForceClose() na MainWindow – to wywoła OnClosing z flagą _forceClose=true,
                // co zapewni poprawny cleanup (ViewModel.Dispose, ConsoleLogger.Shutdown, telemetry heartbeat)
                if (_mainWindow is Views.MainWindow mainWindow)
                {
                    mainWindow.ForceClose();
                }
                else if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
                else
                {
                    Environment.Exit(0);
                }
            });
        }

        /// <summary>
        /// Aktualizuje listę ostatnio uruchamianych modów w menu kontekstowym.
        /// </summary>
        public void UpdateRecentMods(IEnumerable<TrayModInfo> mods)
        {
            _recentMods.Clear();
            _recentMods.AddRange(mods.Take(3));
            Debug.WriteLine($"[SystemTrayService] Zaktualizowano listę modów: {_recentMods.Count} pozycji");
        }

        /// <summary>
        /// Odświeża widoczną ikonkę (np. po zmianie ustawień).
        /// </summary>
        public void Refresh()
        {
            if (_notifyIcon == null)
                return;

            var settings = _userSettingsService.LoadUserSettings();
            _notifyIcon.Visible = settings.MinimizeToTray && _isVisible;
        }

        /// <summary>
        /// Buduje menu kontekstowe przed jego otwarciem.
        /// </summary>
        private void ContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_contextMenu == null)
                return;

            _contextMenu.Items.Clear();

            var settings = _userSettingsService.LoadUserSettings();

            // Sekcja szybkiego uruchamiania modów
            if (settings.ShowQuickLaunchInTray && _recentMods.Count > 0)
            {
                foreach (var mod in _recentMods)
                {
                    var modItem = new ToolStripMenuItem($"🚀 {mod.Name}")
                    {
                        ToolTipText = $"Uruchom {mod.Name}"
                    };
                    var capturedId = mod.Id;
                    var capturedInstanceId = mod.InstanceId;
                    modItem.Click += (s, args) =>
                    {
                        Dispatcher.UIThread.Post(async () =>
                        {
                            if (_mainWindow?.DataContext is ViewModels.MainWindowViewModel vm)
                            {
                                if (!string.IsNullOrEmpty(capturedInstanceId))
                                    await vm.LaunchPackInstanceByIdAsync(capturedInstanceId);
                                else
                                    vm.LaunchModById(capturedId);
                            }
                        });
                    };
                    _contextMenu.Items.Add(modItem);
                }
                _contextMenu.Items.Add(new ToolStripSeparator());
            }

            // Przywróć
            var restoreText = _localizationService?.Get("SystemTray.ContextMenu.Restore") ?? "Restore SUSModder";
            var restoreItem = new ToolStripMenuItem($"📂 {restoreText}");
            restoreItem.Click += (s, args) => RestoreWindow();
            _contextMenu.Items.Add(restoreItem);

            // Zamknij
            var exitText = _localizationService?.Get("SystemTray.ContextMenu.Exit") ?? "Exit";
            var exitItem = new ToolStripMenuItem($"❌ {exitText}");
            exitItem.Click += (s, args) => ExitApplication();
            _contextMenu.Items.Add(exitItem);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _notifyIcon?.Dispose(); // dispose też przypisanej Icon
            _contextMenu?.Dispose();
            _notifyIcon = null;
            _trayIcon = null;
            _contextMenu = null;
            _recentMods.Clear();
            Debug.WriteLine("[SystemTrayService] Zniszczono");
        }
    }

    /// <summary>
    /// Informacje o modzie do wyświetlenia w menu tray.
    /// </summary>
    public class TrayModInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gdy ustawione — szybkie uruchomienie z lokalnej instancji (Moje zestawy).
        /// </summary>
        public string? InstanceId { get; set; }
    }
}
