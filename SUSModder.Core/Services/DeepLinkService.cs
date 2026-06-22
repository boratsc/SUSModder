using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Parsowanie i rejestracja protokołu susmodder:// (HKCU — bez UAC).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class DeepLinkService
    {
        public const string ProtocolScheme = "susmodder";

        public sealed class DeepLinkParseResult
        {
            public bool IsValid { get; init; }
            public string? PackCode { get; init; }
            public bool AutoInstall { get; init; }
        }

        private readonly Action<string>? _log;
        public DeepLinkService(Action<string>? log = null) => _log = log;

        private void Log(string message)
        {
            _log?.Invoke(message);
            Debug.WriteLine($"[DeepLink] {message}");
        }

        /// <summary>
        /// Parsuje URI susmodder://pack/XXXX-XXXX-XXXX lub argument %1 z rejestru Windows.
        /// </summary>
        public static DeepLinkParseResult ParseDeepLink(string? uriOrArg)
        {
            if (string.IsNullOrWhiteSpace(uriOrArg))
                return new DeepLinkParseResult { IsValid = false };

            var raw = uriOrArg.Trim().Trim('"');
            if (!raw.StartsWith($"{ProtocolScheme}://", StringComparison.OrdinalIgnoreCase))
            {
                // Może być sam kod paczki
                if (ModPackCodeValidator.IsValid(raw))
                    return new DeepLinkParseResult { IsValid = true, PackCode = ModPackCodeValidator.Normalize(raw) };
                return new DeepLinkParseResult { IsValid = false };
            }

            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                return new DeepLinkParseResult { IsValid = false };

            if (!string.Equals(uri.Scheme, ProtocolScheme, StringComparison.OrdinalIgnoreCase))
                return new DeepLinkParseResult { IsValid = false };

            // susmodder://pack/CODE — w .NET „pack” jest hostem, kod w AbsolutePath (/CODE)
            // susmodder:///pack/CODE — czasem „pack” jest pierwszym segmentem ścieżki
            if (!TryExtractPackCode(uri, out var packCode) ||
                string.IsNullOrEmpty(packCode) ||
                !ModPackCodeValidator.IsValid(packCode))
                return new DeepLinkParseResult { IsValid = false };

            var autoInstall = false;
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var query = uri.Query.TrimStart('?');
                foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 &&
                        string.Equals(kv[0], "install", StringComparison.OrdinalIgnoreCase) &&
                        (kv[1] == "1" || string.Equals(kv[1], "true", StringComparison.OrdinalIgnoreCase)))
                    {
                        autoInstall = true;
                    }
                }
            }

            return new DeepLinkParseResult
            {
                IsValid = true,
                PackCode = ModPackCodeValidator.Normalize(packCode),
                AutoInstall = autoInstall
            };
        }

        /// <summary>
        /// Rejestruje handler protokołu w HKCU\Software\Classes\susmodder.
        /// </summary>
        public Task RegisterProtocolHandlerAsync(string executablePath)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    Log("RegisterProtocolHandler: executablePath jest pusty, pomijam.");
                    return;
                }

                if (!File.Exists(executablePath))
                {
                    Log($"RegisterProtocolHandler: EXE nie istnieje: {executablePath}");
                    return;
                }

                try
                {
                    var exe = executablePath.Contains(' ') ? $"\"{executablePath}\"" : executablePath;
                    var command = $"{exe} \"%1\"";

                    using var schemeKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolScheme}");
                    if (schemeKey == null)
                    {
                        Log("RegisterProtocolHandler: Nie można utworzyć klucza rejestru.");
                        return;
                    }
                    schemeKey.SetValue("", $"URL:{ProtocolScheme} Protocol");
                    schemeKey.SetValue("URL Protocol", "");

                    using var iconKey = schemeKey.CreateSubKey("DefaultIcon");
                    iconKey?.SetValue("", $"{exe},0");

                    using var shellKey = schemeKey.CreateSubKey(@"shell\open\command");
                    shellKey?.SetValue("", command);

                    Log($"RegisterProtocolHandler: Zarejestrowano handler: {command}");
                }
                catch (Exception ex)
                {
                    Log($"RegisterProtocolHandler: Błąd rejestracji: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Wyciąga ścieżkę EXE z zarejestrowanego polecenia (usuwa cudzysłowy i %1).
        /// Zwraca null jeśli nie można odczytać.
        /// </summary>
        public static string? GetRegisteredExePath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProtocolScheme}\shell\open\command");
                var raw = key?.GetValue("") as string;
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                // Oczyść polecenie: usuń %1 i cudzysłowy
                var cleaned = raw
                    .Replace("\"%1\"", "")
                    .Replace("%1", "")
                    .Trim()
                    .Trim('"');

                if (string.IsNullOrWhiteSpace(cleaned))
                    return null;

                return cleaned;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Wyciąga kod paczki z URI protokołu susmodder.
        /// </summary>
        internal static bool TryExtractPackCode(Uri uri, out string? packCode)
        {
            packCode = null;

            if (string.Equals(uri.Host, "pack", StringComparison.OrdinalIgnoreCase))
            {
                var path = uri.AbsolutePath.Trim('/');
                if (!string.IsNullOrEmpty(path))
                {
                    packCode = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
                    return true;
                }
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 &&
                string.Equals(segments[0], "pack", StringComparison.OrdinalIgnoreCase))
            {
                packCode = segments[1];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sprawdza czy protokół susmodder jest już zarejestrowany.
        /// Od wersji 2.9.x weryfikuje również czy zarejestrowana ścieżka EXE istnieje.
        /// </summary>
        public static bool IsProtocolRegistered()
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProtocolScheme}\shell\open\command");
            if (key?.GetValue("") is not string cmd || string.IsNullOrWhiteSpace(cmd))
                return false;

            // Zweryfikuj, czy zarejestrowana ścieżka EXE istnieje
            var exePath = GetRegisteredExePath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                Debug.WriteLine($"[DeepLink] IsProtocolRegistered: zarejestrowana ścieżka EXE nie istnieje: {exePath}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Sprawdza czy zarejestrowana ścieżka EXE jest tożsama z bieżącą ścieżką aplikacji.
        /// Używa ApplicationPaths.GetApplicationDirectory() zamiast Environment.ProcessPath
        /// aby obsłużyć tryb debug (dotnet.exe).
        /// </summary>
        public static bool IsProtocolUpToDate()
        {
            var registered = GetRegisteredExePath();
            if (string.IsNullOrEmpty(registered))
                return false;

            var currentExe = Path.Combine(ApplicationPaths.GetApplicationDirectory(), "SUSModder.exe");
            if (!File.Exists(currentExe))
                return false;

            return string.Equals(
                Path.GetFullPath(registered),
                Path.GetFullPath(currentExe),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
