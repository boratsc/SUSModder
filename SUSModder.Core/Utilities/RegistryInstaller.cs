using Microsoft.Win32;
using System;
using System.IO;

namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Zarządza rejestracją aplikacji w Windows Registry (Dodaj/usuń programy)
    /// </summary>
    public static class RegistryInstaller
    {
        private const string UNINSTALL_KEY = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SUSModder";

        /// <summary>
        /// Sprawdza czy aplikacja jest zarejestrowana w Windows Registry
        /// </summary>
        public static bool IsRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UNINSTALL_KEY);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Rejestruje aplikację w Windows "Dodaj/usuń programy"
        /// </summary>
        /// <param name="appVersion">Wersja aplikacji (np. "2.2.0")</param>
        /// <returns>True jeśli sukces</returns>
        public static bool RegisterApplication(string appVersion)
        {
            try
            {
                var exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SUSModder.exe");
                var installLocation = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;

                // Utwórz klucz rejestru
                using var key = Registry.CurrentUser.CreateSubKey(UNINSTALL_KEY);

                if (key == null)
                    return false;

                // Podstawowe informacje
                key.SetValue("DisplayName", "SUSModder");
                key.SetValue("DisplayVersion", appVersion);
                key.SetValue("Publisher", "SUSModder Team");
                key.SetValue("DisplayIcon", exePath);
                key.SetValue("InstallLocation", installLocation);

                // Polecenie deinstalacji
                string uninstallCommand;
                
                // Sprawdź czy to instalacja Velopack (Update.exe w katalogu nadrzędnym)
                var parentDir = Directory.GetParent(installLocation)?.FullName;
                var velopackUpdateExe = parentDir != null ? Path.Combine(parentDir, "Update.exe") : null;
                
                if (velopackUpdateExe != null && File.Exists(velopackUpdateExe))
                {
                    // Velopack uninstall (komenda bez myślników!)
                    uninstallCommand = $"\"{velopackUpdateExe}\" uninstall";
                    key.SetValue("UninstallString", uninstallCommand);
                    key.SetValue("QuietUninstallString", $"\"{velopackUpdateExe}\" uninstall -s");
                    System.Diagnostics.Debug.WriteLine($"[Registry] Velopack detected, using: {uninstallCommand}");
                }
                else
                {
                    // Legacy/Portable - uruchom uninstall.ps1 jeśli istnieje
                    var uninstallScriptPath = Path.Combine(installLocation, "uninstall.ps1");
                    if (File.Exists(uninstallScriptPath))
                    {
                        uninstallCommand = $"powershell.exe -ExecutionPolicy Bypass -File \"{uninstallScriptPath}\"";
                        key.SetValue("UninstallString", uninstallCommand);
                        System.Diagnostics.Debug.WriteLine($"[Registry] Using uninstall.ps1: {uninstallCommand}");
                    }
                    else
                    {
                        // Fallback - informuj użytkownika o ręcznym usunięciu
                        uninstallCommand = $"cmd.exe /c echo Aby usunąć aplikację, usuń katalog: {installLocation} && pause";
                        key.SetValue("UninstallString", uninstallCommand);
                        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        System.Diagnostics.Debug.WriteLine($"[Registry] No uninstaller found, using manual message");
                    }
                }

                // Szacunkowy rozmiar instalacji (w KB)
                var estimatedSize = CalculateInstallSize(installLocation);
                key.SetValue("EstimatedSize", estimatedSize, RegistryValueKind.DWord);

                // Metadane
                key.SetValue("URLInfoAbout", "https://susmodder.app");
                key.SetValue("HelpLink", "https://susmodder.app/help");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register application: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Usuwa wpis aplikacji z rejestru
        /// </summary>
        public static bool UnregisterApplication()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UNINSTALL_KEY, throwOnMissingSubKey: false);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to unregister application: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Oblicza rozmiar instalacji w KB
        /// </summary>
        private static int CalculateInstallSize(string directory)
        {
            try
            {
                var dirInfo = new DirectoryInfo(directory);
                long totalBytes = 0;

                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += file.Length;
                }

                // Konwersja na KB
                return (int)(totalBytes / 1024);
            }
            catch
            {
                return 100000; // ~100 MB fallback
            }
        }
    }
}
