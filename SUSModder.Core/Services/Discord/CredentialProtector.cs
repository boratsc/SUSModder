using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SUSModder.Core.Services.Discord;

/// <summary>
/// Szyfruje dane uwierzytelniające na dysk.
/// Windows: DPAPI (ProtectedData.Protect, DataProtectionScope.CurrentUser)
/// Linux:   Currently not supported — throws PlatformNotSupportedException.
///          Future: AES-256-GCM with key derived from user-specific secret.
/// </summary>
public static class CredentialProtector
{
    /// <summary>
    /// Szyfruje plaintext przy użyciu DPAPI (Windows).
    /// Na platformach innych niż Windows rzuca PlatformNotSupportedException.
    /// </summary>
    /// <param name="plaintext">Tekst do zaszyfrowania.</param>
    /// <returns>Zaszyfrowany tekst w Base64.</returns>
    public static string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (OperatingSystem.IsWindows())
        {
            return ProtectWindows(plaintext);
        }

        throw new PlatformNotSupportedException(
            "CredentialProtector is currently only supported on Windows. " +
            "Linux support (AES-256-GCM) will be added in a future release.");
    }

    /// <summary>
    /// Odszyfrowuje ciphertext przy użyciu DPAPI (Windows).
    /// Na platformach innych niż Windows rzuca PlatformNotSupportedException.
    /// </summary>
    /// <param name="ciphertextBase64">Zaszyfrowany tekst w Base64.</param>
    /// <returns>Odszyfrowany tekst jawny.</returns>
    public static string Unprotect(string ciphertextBase64)
    {
        ArgumentNullException.ThrowIfNull(ciphertextBase64);

        if (OperatingSystem.IsWindows())
        {
            return UnprotectWindows(ciphertextBase64);
        }

        throw new PlatformNotSupportedException(
            "CredentialProtector is currently only supported on Windows. " +
            "Linux support (AES-256-GCM) will be added in a future release.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string ProtectWindows(string plaintext)
    {
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        string result = Convert.ToBase64String(encrypted);

        Debug.WriteLine($"[CredentialProtector] DPAPI Protect: {MaskValue(plaintext)} -> base64({result.Length})");
        return result;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string UnprotectWindows(string ciphertextBase64)
    {
        try
        {
            byte[] encrypted = Convert.FromBase64String(ciphertextBase64);
            byte[] plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string result = Encoding.UTF8.GetString(plainBytes);

            Debug.WriteLine($"[CredentialProtector] DPAPI Unprotect: ok ({MaskValue(result)})");
            return result;
        }
        catch (CryptographicException ex)
        {
            Debug.WriteLine($"[CredentialProtector] DPAPI Unprotect failed: {ex.Message}");
            throw new CredentialProtectionException(
                "Nie udało się odszyfrować zapisanych danych logowania (DPAPI). " +
                "Wyloguj się i zaloguj ponownie przez Discord.",
                ex);
        }
    }

    // TODO: Add Linux support (AES-256-GCM) when Linux desktop is a target.
    // Key derivation should use a user-specific secret, not machine-id.
    // See: DOC/PLAN/2026-05-27-implement-discord-oauth2-pkce.md

    /// <summary>
    /// Maskuje wrażliwe dane do logów: pierwsze 8 znaków + "..."
    /// </summary>
    private static string MaskValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "(empty)";

        return value.Length <= 8 ? value + "..." : value[..8] + "...";
    }
}
