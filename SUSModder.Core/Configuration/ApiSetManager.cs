using SUSModder.Core.Diagnostics;
using System.IO;
using System.Text;

namespace SUSModder.Core.Configuration
{
    public static class ApiSetManager
    {
        /// <summary>
        /// Zapisuje plik ApiSet.ini z konfiguracją SUStats
        /// </summary>
        /// <param name="filePath">Pełna ścieżka do pliku ApiSet.ini</param>
        /// <param name="token">Token API</param>
        /// <param name="endpoint">Endpoint API</param>
        /// <param name="secret">Secret API</param>
        /// <param name="diagnosticsOutput">Output do logowania (opcjonalny)</param>
        /// <returns>True jeśli zapisano pomyślnie, False w przypadku błędu</returns>
        public static bool SaveApiSetFile(string filePath, string token, string endpoint, string secret, IDiagnosticsOutput? diagnosticsOutput = null)
        {
            try
            {
                diagnosticsOutput?.Write($"Rozpoczynanie zapisu pliku ApiSet.ini...");
                diagnosticsOutput?.Write($"Ścieżka: {filePath}");

                // Walidacja parametrów
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    diagnosticsOutput?.Write("BŁĄD: Ścieżka pliku jest pusta");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    diagnosticsOutput?.Write("BŁĄD: Token jest pusty");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    diagnosticsOutput?.Write("BŁĄD: Endpoint jest pusty");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(secret))
                {
                    diagnosticsOutput?.Write("BŁĄD: Secret jest pusty");
                    return false;
                }

                // Stwórz katalog jeśli nie istnieje
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    diagnosticsOutput?.Write($"Utworzono katalog: {directory}");
                }

                // Przygotuj zawartość pliku
                var content = new StringBuilder();
                content.AppendLine("EnableApiExport=true");
                content.AppendLine($"ApiToken={token}");
                content.AppendLine($"ApiEndpoint={endpoint}");
                content.AppendLine("SaveLocalBackup=true");
                content.AppendLine($"Secret={secret}");

                // Zapisz plik
                File.WriteAllText(filePath, content.ToString(), Encoding.UTF8);

                diagnosticsOutput?.Write("✅ Plik ApiSet.ini zapisany pomyślnie");
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                diagnosticsOutput?.Write($"❌ BŁĄD: Brak uprawnień do zapisu pliku - {ex.Message}");
                return false;
            }
            catch (DirectoryNotFoundException ex)
            {
                diagnosticsOutput?.Write($"❌ BŁĄD: Nie znaleziono katalogu - {ex.Message}");
                return false;
            }
            catch (IOException ex)
            {
                diagnosticsOutput?.Write($"❌ BŁĄD: Problem z zapisem pliku - {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                diagnosticsOutput?.Write($"❌ BŁĄD: Nieoczekiwany błąd - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sprawdza czy plik ApiSet.ini istnieje i ma poprawną strukturę
        /// </summary>
        /// <param name="filePath">Ścieżka do pliku ApiSet.ini</param>
        /// <param name="diagnosticsOutput">Output do logowania (opcjonalny)</param>
        /// <returns>True jeśli plik istnieje i jest poprawny</returns>
        public static bool ValidateApiSetFile(string filePath, IDiagnosticsOutput? diagnosticsOutput = null)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    diagnosticsOutput?.Write($"Plik ApiSet.ini nie istnieje: {filePath}");
                    return false;
                }

                var content = File.ReadAllText(filePath);
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                bool hasEnableApiExport = false;
                bool hasApiToken = false;
                bool hasApiEndpoint = false;
                bool hasSaveLocalBackup = false;
                bool hasSecret = false;

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("EnableApiExport=")) hasEnableApiExport = true;
                    else if (trimmedLine.StartsWith("ApiToken=")) hasApiToken = true;
                    else if (trimmedLine.StartsWith("ApiEndpoint=")) hasApiEndpoint = true;
                    else if (trimmedLine.StartsWith("SaveLocalBackup=")) hasSaveLocalBackup = true;
                    else if (trimmedLine.StartsWith("Secret=")) hasSecret = true;
                }

                bool isValid = hasEnableApiExport && hasApiToken && hasApiEndpoint && hasSaveLocalBackup && hasSecret;

                if (isValid)
                {
                    diagnosticsOutput?.Write("✅ Plik ApiSet.ini jest poprawny");
                }
                else
                {
                    diagnosticsOutput?.Write("❌ Plik ApiSet.ini ma niepoprawną strukturę");
                    diagnosticsOutput?.Write($"EnableApiExport: {hasEnableApiExport}, ApiToken: {hasApiToken}, ApiEndpoint: {hasApiEndpoint}, SaveLocalBackup: {hasSaveLocalBackup}, Secret: {hasSecret}");
                }

                return isValid;
            }
            catch (Exception ex)
            {
                diagnosticsOutput?.Write($"❌ BŁĄD podczas walidacji pliku ApiSet.ini - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Usuwa plik ApiSet.ini jeśli istnieje
        /// </summary>
        /// <param name="filePath">Ścieżka do pliku ApiSet.ini</param>
        /// <param name="diagnosticsOutput">Output do logowania (opcjonalny)</param>
        /// <returns>True jeśli usunięto pomyślnie lub plik nie istniał</returns>
        public static bool RemoveApiSetFile(string filePath, IDiagnosticsOutput? diagnosticsOutput = null)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    diagnosticsOutput?.Write($"Plik ApiSet.ini nie istnieje (nie ma czego usuwać): {filePath}");
                    return true;
                }

                File.Delete(filePath);
                diagnosticsOutput?.Write("✅ Plik ApiSet.ini został usunięty");
                return true;
            }
            catch (Exception ex)
            {
                diagnosticsOutput?.Write($"❌ BŁĄD podczas usuwania pliku ApiSet.ini - {ex.Message}");
                return false;
            }
        }
    }
}
