using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SUSModder.Core.Utilities;

/// <summary>Stan tokenu właściciela w lokalnym magazynie (fail-closed: Unreadable ≠ Missing).</summary>
public enum OwnerTokenState
{
    /// <summary>Plik/wpis nie istnieje — paczka legacy albo tworzona na innym urządzeniu.</summary>
    Missing,
    /// <summary>Token odczytany i poprawny.</summary>
    Available,
    /// <summary>Błąd IO/DPAPI/uszkodzony plik lub wpis — nie wysyłać hashu czatu w ciemno.</summary>
    Unreadable
}

/// <summary>
/// Magazyn tokenów właściciela paczek (API v2, ownershipMode=ownerToken).
/// Jeden plik JSON (%APPDATA%\SUSModder\pack-owner-tokens): znormalizowany kod paczki → base64 blob DPAPI CurrentUser.
/// Fail-closed: błąd zapisu propaguje wyjątek (nie połyka jak AnonymousUserHashStore);
/// nieczytelny plik/wpis przy odczycie = Unreadable (write paths nie wysyłają wtedy hashu czatu).
/// Token jest przechowywany i zwracany DOKŁADNIE w postaci zwróconej przez serwer (bez normalizacji case).
/// Wartości tokenów NIGDY nie trafiają do logów (nawet maskowane — dlatego nie używamy CredentialProtector).
/// </summary>
internal static class PackOwnerTokenStore
{
    /// <summary>Nazwa pliku magazynu w %APPDATA%\SUSModder (współdzielona z factory resetem, który go oszczędza).</summary>
    internal const string FileName = "pack-owner-tokens";

    private static readonly object Gate = new();

    // Token: 64 hex, dowolny case (serwer może zwrócić uppercase). Osobny walidator niż AnonymousUserHash,
    // który pozostaje strict lowercase dla creatorHash.
    private static readonly Regex Hex64AnyCase = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool IsValidTokenFormat(string? value) =>
        !string.IsNullOrEmpty(value) && Hex64AnyCase.IsMatch(value);

    internal static string? DefaultPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return string.IsNullOrWhiteSpace(appData) ? null : Path.Combine(appData, "SUSModder", FileName);
        }
    }

    /// <summary>
    /// Zapisuje token pod znormalizowanym kodem paczki. Każdy błąd (IO, DPAPI, platforma) propaguje —
    /// wywołujący musi przerwać operację (fail-closed).
    /// </summary>
    internal static void Save(string? path, string packCode, string ownerToken)
    {
        if (!ModPackCodeValidator.IsValid(packCode))
            throw new ArgumentException("Invalid pack code.", nameof(packCode));
        if (!IsValidTokenFormat(ownerToken))
            throw new ArgumentException("Invalid owner token format (64 hex).", nameof(ownerToken));
        if (string.IsNullOrWhiteSpace(path))
            throw new IOException("Owner token store path is unavailable.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Owner token storage requires Windows DPAPI.");

        var normalized = ModPackCodeValidator.Normalize(packCode);
        lock (Gate)
        {
            var map = LoadMapForWrite(path);
            map[normalized] = ProtectWindows(ownerToken);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                // Flush(true) = zrzut na dysk przed podmianą pliku (trwałość po utracie zasilania).
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, map);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch { /* best-effort sprzątanie .tmp */ }
            }
        }
    }

    /// <summary>
    /// Odczyt tokenu. Tylko Available zwraca true; Missing i Unreadable → false (fail-closed).
    /// </summary>
    internal static bool TryGet(string? path, string packCode, out string ownerToken) =>
        GetState(path, packCode, out ownerToken) == OwnerTokenState.Available;

    /// <summary>
    /// Tri-state odczyt: Missing (plik/wpis nie istnieje) vs Unreadable (IO/DPAPI/uszkodzony plik lub wpis).
    /// </summary>
    internal static OwnerTokenState GetState(string? path, string packCode, out string ownerToken)
    {
        ownerToken = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !ModPackCodeValidator.IsValid(packCode) || !OperatingSystem.IsWindows())
            return OwnerTokenState.Missing;

        try
        {
            lock (Gate)
            {
                var map = ReadMap(path, out var fileExists);
                if (!fileExists)
                    return OwnerTokenState.Missing;
                if (map == null)
                    return OwnerTokenState.Unreadable;
                if (!map.TryGetValue(ModPackCodeValidator.Normalize(packCode), out var blob))
                    return OwnerTokenState.Missing;

                var token = UnprotectWindows(blob);
                if (!IsValidTokenFormat(token))
                    return OwnerTokenState.Unreadable;

                ownerToken = token;
                return OwnerTokenState.Available;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or FormatException or CryptographicException or PlatformNotSupportedException or ArgumentException)
        {
            return OwnerTokenState.Unreadable;
        }
    }

    /// <summary>
    /// Migawka stanów dla wielu kodów po JEDNYM odczycie pliku (odsprzęglenie wątku UI od IO per paczka).
    /// Klucze: znormalizowane kody paczek; niepoprawny kod → Missing pod oryginalnym kluczem.
    /// </summary>
    internal static IReadOnlyDictionary<string, OwnerTokenState> GetStates(string? path, IEnumerable<string> packCodes)
    {
        var result = new Dictionary<string, OwnerTokenState>(StringComparer.Ordinal);

        Dictionary<string, string>? map = null;
        var fileExists = false;
        var fileUnreadable = false;
        if (!string.IsNullOrWhiteSpace(path) && OperatingSystem.IsWindows())
        {
            try
            {
                lock (Gate)
                {
                    map = ReadMap(path, out fileExists);
                    fileUnreadable = fileExists && map == null;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                or FormatException or CryptographicException or PlatformNotSupportedException or ArgumentException)
            {
                fileUnreadable = true;
            }
        }

        foreach (var code in packCodes)
        {
            if (!ModPackCodeValidator.IsValid(code))
            {
                result[code] = OwnerTokenState.Missing;
                continue;
            }

            var normalized = ModPackCodeValidator.Normalize(code);
            if (!fileExists)
            {
                result[normalized] = OwnerTokenState.Missing;
                continue;
            }
            if (fileUnreadable || map == null || !map.TryGetValue(normalized, out var blob))
            {
                result[normalized] = fileUnreadable ? OwnerTokenState.Unreadable : OwnerTokenState.Missing;
                continue;
            }

            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    result[normalized] = OwnerTokenState.Missing;
                    continue;
                }
                var token = UnprotectWindows(blob);
                result[normalized] = IsValidTokenFormat(token) ? OwnerTokenState.Available : OwnerTokenState.Unreadable;
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
            {
                result[normalized] = OwnerTokenState.Unreadable;
            }
        }

        return result;
    }

    // Istniejący plik, którego nie da się odczytać/sparsować, blokuje zapis —
    // nadpisanie go skasowałoby tokeny innych paczek.
    private static Dictionary<string, string> LoadMapForWrite(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        return map ?? throw new IOException("Owner token store file is corrupted.");
    }

    private static Dictionary<string, string>? ReadMap(string path, out bool fileExists)
    {
        fileExists = File.Exists(path);
        return fileExists
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            : null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string ProtectWindows(string plaintext) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser));

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string UnprotectWindows(string ciphertextBase64) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ciphertextBase64), null, DataProtectionScope.CurrentUser));
}