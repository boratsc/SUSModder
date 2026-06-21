using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using SUSModder.Core.Configuration;
using SUSModder.Core.Services;

namespace SUSModder.Core.Utilities;

/// <summary>
/// Resolves the desktop application version for telemetry and diagnostics.
/// Avoids falling back to SUSModder.Core assembly version, which is commonly 1.0.0.
/// </summary>
public static class AppVersionResolver
{
    public const string UnknownVersion = "0.0.0";

    public static string Resolve(
        UserSettingsService? userSettingsService = null,
        IEnumerable<string>? candidateDirectories = null,
        Assembly? entryAssembly = null)
    {
        var fileVersion = TryResolveFromVersionFiles(candidateDirectories ?? GetDefaultVersionDirectories());
        if (!string.IsNullOrWhiteSpace(fileVersion))
            return fileVersion;

        var settingsVersion = TryResolveFromUserSettings(userSettingsService);
        if (!string.IsNullOrWhiteSpace(settingsVersion))
            return settingsVersion;

        var assemblyVersion = TryResolveFromEntryAssembly(entryAssembly ?? Assembly.GetEntryAssembly());
        if (!string.IsNullOrWhiteSpace(assemblyVersion))
            return assemblyVersion;

        Debug.WriteLine($"[AppVersion] Version could not be resolved - using {UnknownVersion} fallback");
        return UnknownVersion;
    }

    private static string? TryResolveFromVersionFiles(IEnumerable<string> candidateDirectories)
    {
        foreach (var directory in candidateDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            var versionFilePath = Path.Combine(directory, "version.json");
            var version = TryReadVersionFile(versionFilePath);
            if (!string.IsNullOrWhiteSpace(version))
            {
                Debug.WriteLine($"[AppVersion] Loaded version from {versionFilePath}: {version}");
                return version;
            }
        }

        return null;
    }

    private static string? TryResolveFromUserSettings(UserSettingsService? userSettingsService)
    {
        if (userSettingsService == null)
            return null;

        try
        {
            var appVersion = userSettingsService.LoadAppVersion();
            if (!string.IsNullOrWhiteSpace(appVersion.CurrentVersion))
            {
                Debug.WriteLine($"[AppVersion] Loaded version from UserSettingsService: {appVersion.CurrentVersion}");
                return appVersion.CurrentVersion;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppVersion] Failed to load version from UserSettingsService: {ex.Message}");
        }

        return null;
    }

    private static string? TryResolveFromEntryAssembly(Assembly? entryAssembly)
    {
        if (entryAssembly == null)
            return null;

        var informationalVersion = entryAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (IsUsefulAssemblyVersion(informationalVersion))
        {
            Debug.WriteLine($"[AppVersion] Loaded version from entry assembly informational version: {informationalVersion}");
            return informationalVersion;
        }

        var assemblyVersion = entryAssembly.GetName().Version?.ToString(3);
        if (IsUsefulAssemblyVersion(assemblyVersion))
        {
            Debug.WriteLine($"[AppVersion] Loaded version from entry assembly version: {assemblyVersion}");
            return assemblyVersion;
        }

        return null;
    }

    private static bool IsUsefulAssemblyVersion(string? version)
    {
        return !string.IsNullOrWhiteSpace(version) &&
               !string.Equals(version, "1.0.0", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadVersionFile(string versionFilePath)
    {
        try
        {
            if (!File.Exists(versionFilePath))
                return null;

            var json = File.ReadAllText(versionFilePath);
            var versionData = JsonSerializer.Deserialize<AppVersion>(json);
            return string.IsNullOrWhiteSpace(versionData?.CurrentVersion)
                ? null
                : versionData.CurrentVersion;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppVersion] Failed to read {versionFilePath}: {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<string> GetDefaultVersionDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in BuildDefaultVersionDirectories())
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            var normalized = Path.GetFullPath(directory);
            if (seen.Add(normalized))
                yield return normalized;
        }
    }

    private static IEnumerable<string> BuildDefaultVersionDirectories()
    {
        var processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrWhiteSpace(processDirectory))
        {
            yield return processDirectory;
            yield return Path.Combine(processDirectory, "current");

            var parent = Directory.GetParent(processDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
                yield return Path.Combine(parent, "current");
        }

        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "current");
    }
}
