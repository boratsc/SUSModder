using System.IO.Compression;
using ProtoBuf;

namespace SUSModder.Core.GameIntegration.Steam;

internal sealed class DdAccountConfigWriter
{
    private static readonly string ToolsBaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SUSModder", "tools");

    private static readonly string DepotDownloaderDir =
        Path.Combine(ToolsBaseDir, $"depotdownloader-{DepotDownloaderRunner.Version}");

    public void WriteRefreshToken(string username, string refreshToken, string? guardData)
    {
        var configPath = PreferExistingConfigOrCreateDest();

        var store = new DdAccountSettingsStore();
        if (File.Exists(configPath))
            store = LoadExisting(configPath);

        store.LoginTokens[username] = refreshToken;
        if (guardData is not null)
            store.GuardData[username] = guardData;

        using var fileStream = File.Create(configPath);
        using var deflateStream = new DeflateStream(fileStream, CompressionMode.Compress);
        Serializer.Serialize(deflateStream, store);

        WriteLastAccount(username);
    }

    public bool HasToken(string username)
    {
        var configPath = FindReadableConfigPath();
        if (configPath is null || !File.Exists(configPath))
            return false;

        var store = LoadExisting(configPath);
        return store.LoginTokens.ContainsKey(username);
    }

    public bool HasAnyToken()
    {
        var configPath = FindReadableConfigPath();
        if (configPath is null || !File.Exists(configPath))
            return GetLastAccountUsername() is not null;

        var store = LoadExisting(configPath);
        return store.LoginTokens.Count > 0;
    }

    public string? GetAnyTokenUsername()
    {
        var configPath = FindReadableConfigPath();
        if (configPath is null || !File.Exists(configPath))
            return GetLastAccountUsername();

        var store = LoadExisting(configPath);
        return store.LoginTokens.Keys.FirstOrDefault() ?? GetLastAccountUsername();
    }

    public void MarkSuccessfulLogin(string username)
    {
        if (!string.IsNullOrWhiteSpace(username))
            WriteLastAccount(username.Trim());
    }

    public void BackupToCache()
    {
        var configPath = FindConfigPath();
        if (configPath is null || !File.Exists(configPath))
            return;

        var cachePath = GetCachePath();
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        File.Copy(configPath, cachePath, overwrite: true);

        try
        {
            var store = LoadExisting(cachePath);
            var username = store.LoginTokens.Keys.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(username))
                WriteLastAccount(username);
        }
        catch
        {
            // Best-effort marker only.
        }
    }

    public void RestoreFromCache()
    {
        var cachePath = GetCachePath();
        if (!File.Exists(cachePath))
            return;

        var destPath = PreferExistingConfigOrCreateDest();
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(cachePath, destPath, overwrite: true);
    }

    public void CleanCorrupt()
    {
        var configPath = FindConfigPath();
        if (configPath is null || !File.Exists(configPath))
            return;

        try
        {
            _ = LoadExisting(configPath);
        }
        catch
        {
            try { File.Delete(configPath); } catch { }
        }
    }

    public void ClearSession()
    {
        var cachePath = GetCachePath();
        if (File.Exists(cachePath))
        {
            try { File.Delete(cachePath); } catch { }
        }

        var lastAccountPath = GetLastAccountPath();
        if (File.Exists(lastAccountPath))
        {
            try { File.Delete(lastAccountPath); } catch { }
        }

        var configPath = FindConfigPath();
        if (configPath is not null && File.Exists(configPath))
        {
            try { File.Delete(configPath); } catch { }
        }
    }

    private static DdAccountSettingsStore LoadExisting(string path)
    {
        using var fileStream = File.OpenRead(path);
        using var deflateStream = new DeflateStream(fileStream, CompressionMode.Decompress);
        return Serializer.Deserialize<DdAccountSettingsStore>(deflateStream);
    }

    private static string GetSteamSessionDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SUSModder", "steam-session");

    private static string GetCachePath() =>
        Path.Combine(GetSteamSessionDir(), "account.config");

    private static string GetLastAccountPath() =>
        Path.Combine(GetSteamSessionDir(), "last-account.txt");

    private static string? GetLastAccountUsername()
    {
        var path = GetLastAccountPath();
        if (!File.Exists(path))
            return null;

        var username = File.ReadAllText(path).Trim();
        return string.IsNullOrWhiteSpace(username) ? null : username;
    }

    private static void WriteLastAccount(string username)
    {
        var path = GetLastAccountPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, username.Trim());
    }

    private static string? FindReadableConfigPath()
    {
        var configPath = FindConfigPath();
        if (configPath is not null)
            return configPath;

        var cachePath = GetCachePath();
        return File.Exists(cachePath) ? cachePath : null;
    }

    private static string PreferExistingConfigOrCreateDest()
    {
        var existing = FindConfigPath();
        if (existing is not null)
            return existing;

        foreach (var isolatedStorageBase in GetIsolatedStorageBases())
        {
            if (!Directory.Exists(isolatedStorageBase))
                continue;

            foreach (var dir in Directory.EnumerateDirectories(isolatedStorageBase, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, "AppFiles", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "AssemFiles", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(dir, "account.config");
                }
            }
        }

        return Path.Combine(DepotDownloaderDir, "account.config");
    }

    private static string? FindConfigPath()
    {
        foreach (var isolatedStorageBase in GetIsolatedStorageBases())
        {
            if (!Directory.Exists(isolatedStorageBase))
                continue;

            try
            {
                var candidate = Directory
                    .EnumerateFiles(isolatedStorageBase, "account.config", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (candidate is not null)
                    return candidate;
            }
            catch
            {
                // Keep searching fallbacks.
            }
        }

        var localPath = Path.Combine(DepotDownloaderDir, "account.config");
        return File.Exists(localPath) ? localPath : null;
    }

    private static IReadOnlyList<string> GetIsolatedStorageBases()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IsolatedStorage")
            ];
        }

        var localShare = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share");

        return
        [
            Path.Combine(localShare, "IsolatedStorage"),
            Path.Combine(localShare, ".isolated-storage")
        ];
    }
}

[ProtoContract]
internal sealed class DdAccountSettingsStore
{
    [ProtoMember(2)]
    public Dictionary<string, int> ContentServerPenalty { get; set; } = new();

    [ProtoMember(4)]
    public Dictionary<string, string> LoginTokens { get; set; } = new();

    [ProtoMember(5)]
    public Dictionary<string, string> GuardData { get; set; } = new();
}
