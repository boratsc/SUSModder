using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SUSModder.Core.Utilities;

/// <summary>
/// Walidacja i generowanie anonimowego user/creator hash (SHA256 = 64 lowercase hex).
/// Backend API wymaga dokładnie tego formatu (np. creatorHash).
/// </summary>
internal static class AnonymousUserHash
{
    private static readonly Regex LowerHex64 = new("^[a-f0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) && LowerHex64.IsMatch(value);

    public static string ComputeSha256Hex(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// Fallback gdy nie da się odczytać HWID — zawsze zwraca poprawny 64-hex (nigdy surowego GUID).
    /// </summary>
    public static string CreateFallback() =>
        ComputeSha256Hex("hwid-fallback:" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Jeśli wartość nie spełnia kontraktu API, przehashuj do 64 lowercase hex.
    /// </summary>
    public static string EnsureValid(string? value)
    {
        if (IsValid(value))
            return value!;

        if (string.IsNullOrEmpty(value))
            return CreateFallback();

        return ComputeSha256Hex("hwid-rehash:" + value);
    }
}
