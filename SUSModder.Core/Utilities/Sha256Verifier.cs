using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Core.Utilities;

public static class Sha256Verifier
{
    public static string ComputeHex(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<string> ComputeFileHexAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool VerifyBytes(byte[] bytes, string expectedSha256)
    {
        if (bytes.Length == 0 || string.IsNullOrWhiteSpace(expectedSha256))
            return false;

        var normalizedExpected = expectedSha256.Trim().ToLowerInvariant();
        if (normalizedExpected.Length != 64)
            return false;

        try
        {
            var actualHash = SHA256.HashData(bytes);
            var expectedHash = Convert.FromHexString(normalizedExpected);
            return CryptographicOperations.FixedTimeEquals(
                actualHash,
                expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies file SHA256. Returns false for missing/malformed expected hash or mismatch.
    /// On mismatch, deletes <paramref name="filePath"/> when <paramref name="deleteOnMismatch"/> is true.
    /// </summary>
    public static async Task<bool> VerifyFileAsync(
        string filePath,
        string? expectedSha256,
        bool deleteOnMismatch = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return false;

        var normalizedExpected = expectedSha256.Trim().ToLowerInvariant();
        if (normalizedExpected.Length != 64)
            return false;

        if (!File.Exists(filePath))
            return false;

        var actualHash = await ComputeFileHexAsync(filePath, ct);
        if (string.Equals(actualHash, normalizedExpected, StringComparison.OrdinalIgnoreCase))
            return true;

        if (deleteOnMismatch)
        {
            try { File.Delete(filePath); } catch { /* best effort */ }
        }

        return false;
    }

    /// <summary>
    /// True when expected hash is present and well-formed (64 hex chars).
    /// </summary>
    public static bool IsWellFormedHash(string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return false;
        var normalized = expectedSha256.Trim();
        if (normalized.Length != 64)
            return false;
        foreach (var c in normalized)
        {
            var isHex = (c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'f')
                || (c >= 'A' && c <= 'F');
            if (!isHex)
                return false;
        }
        return true;
    }
}
