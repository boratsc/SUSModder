using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;

namespace Updater
{
    public class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: Updater <target-dir> <zip-file-path>");
                return;
            }

            string targetDir = args[0];
            string tempFilePath = args[1];
            string tempExtractPath = Path.Combine(Path.GetTempPath(), "LatestVersionExtract");

            // Czekaj aż SUSModder.exe się zamknie w katalogu targetDir
            string exeName = "SUSModder.exe";
            bool found = false;
            foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName)))
            {
                try
                {
                    if (proc.MainModule.FileName.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        Console.WriteLine("Czekam na zamknięcie SUSModder.exe...");
                        proc.WaitForExit();
                    }
                }
                catch { /* ignoruj procesy systemowe */ }
            }
            if (found)
            {
                // Daj jeszcze sekundę na zwolnienie plików przez system
                System.Threading.Thread.Sleep(1000);
            }

            try
            {
                Console.WriteLine("Rozpakowywanie archiwum ZIP...");
                if (Directory.Exists(tempExtractPath))
                {
                    Directory.Delete(tempExtractPath, true);
                }
                Directory.CreateDirectory(tempExtractPath);

                // --- SPRZĄTANIE: Usuń stare pliki i katalogi, których nie ma w nowej wersji ---
                var newFiles = new HashSet<string>(
                    Directory.GetFiles(tempExtractPath, "*", SearchOption.AllDirectories)
                        .Select(f => Path.GetRelativePath(tempExtractPath, f).Replace('\\', '/'))
                );

                foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
                {
                    string relPath = Path.GetRelativePath(targetDir, file).Replace('\\', '/');

                    // Wyjątki:
                    if (relPath.Equals("config.json", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (relPath.StartsWith("updater/", StringComparison.OrdinalIgnoreCase) || relPath.Equals("updater", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (newFiles.Contains(relPath))
                        continue;

                    try
                    {
                        Console.WriteLine($"Usuwam niepotrzebny plik: {file}");
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Nie udało się usunąć pliku {file}: {ex.Message}");
                    }
                }

                foreach (var dir in Directory.GetDirectories(targetDir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    string relPath = Path.GetRelativePath(targetDir, dir).Replace('\\', '/');
                    if (relPath.StartsWith("updater/", StringComparison.OrdinalIgnoreCase) || relPath.Equals("updater", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        try
                        {
                            Console.WriteLine($"Usuwam pusty katalog: {dir}");
                            Directory.Delete(dir);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Nie udało się usunąć katalogu {dir}: {ex.Message}");
                        }
                    }
                }
                // --- KONIEC SPRZĄTANIA ---

                using (ZipArchive archive = ZipFile.OpenRead(tempFilePath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        Console.WriteLine($"Processing entry: {entry.FullName}");

                        // Pomijanie config.json i updatera
                        if (entry.FullName.EndsWith("config.json") || entry.FullName.StartsWith("SUSModder/updater/"))
                        {
                            continue;
                        }

                        if (entry.FullName.StartsWith("SUSModder/"))
                        {
                            string relativePath = entry.FullName.Substring("SUSModder/".Length);
                            string destinationPath = Path.Combine(tempExtractPath, relativePath);

                            if (entry.Name == "")
                            {
                                Console.WriteLine($"Tworzenie katalogu: {destinationPath}");
                                Directory.CreateDirectory(destinationPath);
                            }
                            else
                            {
                                Console.WriteLine($"Rozpakowywanie pliku: {destinationPath}");
                                entry.ExtractToFile(destinationPath, overwrite: true);
                            }
                        }
                    }
                }

                Console.WriteLine("Instalowanie nowej wersji...");
                foreach (string file in Directory.GetFiles(tempExtractPath, "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(tempExtractPath, file);
                    string destFile = Path.Combine(targetDir, relativePath);
                    Console.WriteLine($"Kopiowanie {file} do {destFile}");

                    string destDir = Path.GetDirectoryName(destFile) ?? string.Empty;
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Console.WriteLine($"Tworzenie katalogu: {destDir}");
                        Directory.CreateDirectory(destDir);
                    }
                    File.Copy(file, destFile, true);
                }



                string appExePath = Path.Combine(targetDir, "SUSModder.exe");
                if (File.Exists(appExePath))
                {
                    Console.WriteLine($"Próba uruchomienia aplikacji: {appExePath}");
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = appExePath,
                            UseShellExecute = true,
                            WorkingDirectory = targetDir
                        });
                        Console.WriteLine("Aplikacja została uruchomiona pomyślnie.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Błąd podczas uruchamiania aplikacji: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("Plik wykonywalny nie istnieje: " + appExePath);
                }

                Console.WriteLine("Usuwanie tymczasowych plików...");
                Directory.Delete(tempExtractPath, true);
                File.Delete(tempFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Uaktualnienie nie powiodło się: {ex.Message}");
                Console.WriteLine("Naciśnij Enter, aby zakończyć.");
                Console.ReadLine();
            }
        }
    }
}
