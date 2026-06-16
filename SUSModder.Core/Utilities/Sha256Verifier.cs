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
}
