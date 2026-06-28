using System;
using System.Security.Cryptography;
using System.Text;

namespace SUSModder.Core.Services.Discord;

/// <summary>
/// Pomocnicze metody PKCE i walidacji state dla Discord OAuth2.
/// Wydzielone do testów jednostkowych.
/// </summary>
public static class DiscordOAuthPkce
{
    /// <summary>
    /// Generuje code_verifier: 64 bajty losowe, URL-safe Base64 bez padding.
    /// </summary>
    public static string GenerateCodeVerifier()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(randomBytes);
    }

    /// <summary>
    /// Oblicza code_challenge = URL-safe Base64(SHA256(code_verifier)), bez padding.
    /// </summary>
    public static string GenerateCodeChallenge(string codeVerifier)
    {
        var codeVerifierBytes = Encoding.ASCII.GetBytes(codeVerifier);
        var sha256Bytes = SHA256.HashData(codeVerifierBytes);
        return Base64UrlEncode(sha256Bytes);
    }

    /// <summary>
    /// Generuje OAuth state parameter: 32 bajty losowe w hex (lowercase).
    /// </summary>
    public static string GenerateState()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(randomBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Waliduje parametr state z callbacku OAuth (anti-CSRF).
    /// </summary>
    public static OAuthStateValidationResult ValidateState(string? expectedState, string? callbackState)
    {
        if (string.IsNullOrEmpty(expectedState))
        {
            return new OAuthStateValidationResult(false, "Sesja OAuth wygasła. Spróbuj zalogować się ponownie.");
        }

        if (string.IsNullOrEmpty(callbackState))
        {
            return new OAuthStateValidationResult(false, "Brak parametru state w callbacku. Autoryzacja odrzucona.");
        }

        if (!string.Equals(expectedState, callbackState, StringComparison.Ordinal))
        {
            return new OAuthStateValidationResult(false, "Niezgodność parametru state. Autoryzacja odrzucona.");
        }

        return new OAuthStateValidationResult(true, null);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

/// <summary>
/// Wynik walidacji parametru state OAuth.
/// </summary>
/// <param name="IsValid">Czy state jest poprawny</param>
/// <param name="ErrorMessage">Komunikat błędu (gdy IsValid = false)</param>
public readonly record struct OAuthStateValidationResult(bool IsValid, string? ErrorMessage);
