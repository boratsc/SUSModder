using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SUSModder.Core.Utilities;
using SUSModder.Views;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający akcje zewnętrzne: donacje, Discord, SUStats, lobby settings, fix black screen, shortcuts
    /// </summary>
    public partial class MainWindowViewModel
    {
        #region External Links & Windows

        private void ShowRecommendedDiscords()
        {
            try
            {
                var discordsWindow = new RecommendedDiscordsWindow();
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    discordsWindow.Show(mainWindow);
                }
                else
                {
                    discordsWindow.Show();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening Discord servers window: {ex.Message}");
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowErrorDialogAsync($"Nie udało się otworzyć okna Discord serwerów: {ex.Message}", "Błąd");
                });
            }
        }

        private async void ShowSUStatsConfig()
        {
            try
            {
                var suStatsWindow = new SUStatsConfigWindow();
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await suStatsWindow.ShowDialog(mainWindow);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening SUStats config window: {ex.Message}");
                await ShowErrorDialogAsync($"Nie udało się otworzyć okna konfiguracji SUStats: {ex.Message}", "Błąd");
            }
        }

        #endregion

        #region Game Settings & Tools

        private async Task ShowLobbySetDialog()
        {
            var dialog = new LobbySetDialog();
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
                if (dialog.DialogResult)
                {
                    await ShowMessageAsync("Sukces", $"Ustawiono liczbę graczy na {dialog.PlayerCount}");
                }
            }
        }

        private async Task ExecuteFixBlackScreenAsync()
        {
            try
            {
                // Dialog potwierdzenia na UI thread
                var confirmResult = await ShowConfirmDialogAsync(
                    "Czy jesteś pewny, że chcesz zrestartować ustawienia gry?",
                    "Potwierdzenie");

                if (!confirmResult)
                    return;

                // Operacje na plikach w background thread
                await Task.Run(() => FixBlackScreen.ExecuteFixCore());

                // Dialog sukcesu na UI thread
                await ShowMessageAsync("Sukces", "Ustawienia gry zostały zresetowane.");
            }
            catch (Exception ex)
            {
                // Dialog błędu na UI thread
                await ShowErrorDialogAsync($"Wystąpił błąd podczas resetowania ustawień: {ex.Message}", "Błąd");
            }
        }

        private void ShowRoles()
        {
            if (SelectedMod != null)
            {
                // SelectedMod.Id to ID moda z config.json
                // Przekazujemy to samo ID jako configId i modId
                var rolesWindow = new RolesWindow(SelectedMod.Id, SelectedMod.Id, SelectedMod.Name);
                rolesWindow.Show();
            }
        }

        #endregion

        #region File & Folder Operations

        private void OpenFolder()
        {
            if (SelectedMod?.InstallPath != null && Directory.Exists(SelectedMod.InstallPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SelectedMod.InstallPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Nie udało się otworzyć folderu: {ex.Message}");
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await ShowErrorDialogAsync($"Nie udało się otworzyć folderu: {ex.Message}", "Błąd");
                    });
                }
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowErrorDialogAsync("Folder instalacji nie istnieje lub mod nie jest zainstalowany.", "Błąd");
                });
            }
        }

        private void CreateShortcut()
        {
            if (SelectedMod?.InstallPath != null && Directory.Exists(SelectedMod.InstallPath))
            {
                try
                {
                    string amongUsExePath = Path.Combine(SelectedMod.InstallPath, "Among Us.exe");
                    if (File.Exists(amongUsExePath))
                    {
                        // Sprawdź czy jesteśmy na Windows
                        if (OperatingSystem.IsWindows())
                        {
                            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            string shortcutPath = Path.Combine(desktopPath, $"{SelectedMod.Name}.lnk");

                            CreateWindowsShortcut(amongUsExePath, shortcutPath, SelectedMod.InstallPath);
                        }
                        else
                        {
                            // Na innych platformach możesz pokazać komunikat lub zaimplementować inne rozwiązanie
                            System.Diagnostics.Debug.WriteLine("Shortcut creation is only supported on Windows");
                            // Opcjonalnie: pokaż komunikat użytkownikowi
                            _ = ShowMessageAsync("Informacja", "Tworzenie skrótów jest dostępne tylko na Windows.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Nie udało się utworzyć skrótu: {ex.Message}");
                }
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async void CreateWindowsShortcut(string targetPath, string shortcutPath, string workingDirectory)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    await ShowErrorDialogAsync("Nie można uzyskać dostępu do WScript.Shell", "Błąd");
                    return;
                }

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    await ShowErrorDialogAsync("Nie można utworzyć instancji WScript.Shell", "Błąd");
                    return;
                }

                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDirectory;
                shortcut.Description = $"Skrót do {Path.GetFileNameWithoutExtension(targetPath)}";
                shortcut.Save();

                System.Diagnostics.Debug.WriteLine($"Shortcut created: {shortcutPath}");

                // Dialog sukcesu
                await ShowMessageAsync("Sukces", $"Skrót został utworzony na pulpicie:\n{Path.GetFileName(shortcutPath)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating Windows shortcut: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas tworzenia skrótu: {ex.Message}", "Błąd");
            }
        }

        #endregion
    }
}
