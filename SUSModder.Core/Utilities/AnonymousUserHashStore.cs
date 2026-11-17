using System.Security.Cryptography;

namespace SUSModder.Core.Utilities;

internal static class AnonymousUserHashStore
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> Fallbacks = new(StringComparer.OrdinalIgnoreCase);

    internal static string GetOrCreate()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = string.IsNullOrWhiteSpace(appData) ? null : Path.Combine(appData, "SUSModder", "anonymous-user-hash");
        return GetOrCreate(path, () => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
    }

    internal static string GetOrCreate(string? path, Func<string> create)
    {
        lock (Gate)
        {
            var key = path ?? string.Empty;
            try
            {
                var existing = path is null ? null : File.ReadAllText(path).Trim();
                if (AnonymousUserHash.IsValid(existing))
                    return existing;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }

            if (Fallbacks.TryGetValue(key, out var fallback))
                return fallback;
            var hash = create();
            if (!AnonymousUserHash.IsValid(hash))
                throw new InvalidOperationException("Fallback user hash generator returned an invalid hash.");

            Fallbacks[key] = hash;
            try
            {
                if (path is not null)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(temporaryPath, hash);
                    File.Move(temporaryPath, path, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }

            return hash;
        }
    }
}
