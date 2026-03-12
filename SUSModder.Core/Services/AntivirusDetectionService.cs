using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace SUSModder.Core.Services
{
    public sealed class AntivirusDetectionResult
    {
        public static AntivirusDetectionResult Empty { get; } = new(Array.Empty<string>());

        public AntivirusDetectionResult(IReadOnlyList<string> productNames)
        {
            ProductNames = productNames;
            Signature = string.Join("|", productNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> ProductNames { get; }

        public string Signature { get; }

        public bool HasThirdPartyAntivirus => ProductNames.Count > 0;
    }

    [SupportedOSPlatform("windows")]
    public sealed class AntivirusDetectionService
    {
        private static readonly string[] UninstallRegistryPaths =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        private static readonly string[] DefenderKeywords =
        {
            "microsoft defender",
            "windows defender",
            "microsoft defender antivirus",
            "microsoft defender for endpoint"
        };

        private static readonly (string CanonicalName, string[] Keywords)[] KnownAntivirusMappings =
        {
            ("MKS Vir", new[] { "mks vir", "mks_vir", "mksvir", "arca vir", "arca.vir" }),
            ("Avast", new[] { "avast" }),
            ("AVG", new[] { "avg" }),
            ("Malwarebytes", new[] { "malwarebytes", "malware bytes", "mbam", "malwarebytes privacy", "malwarebytes endpoint" }),
            ("Norton", new[] { "norton", "symantec endpoint", "symantec" }),
            ("Bitdefender", new[] { "bitdefender" }),
            ("ESET", new[] { "eset", "nod32" }),
            ("Kaspersky", new[] { "kaspersky" }),
            ("McAfee", new[] { "mcafee" }),
            ("Avira", new[] { "avira" }),
            ("Panda", new[] { "panda" }),
            ("Trend Micro", new[] { "trend micro", "trendmicro" }),
            ("F-Secure", new[] { "f-secure", "f secure" }),
            ("Sophos", new[] { "sophos" }),
            ("Comodo", new[] { "comodo" }),
            ("G Data", new[] { "g data", "gdata" }),
            ("K7", new[] { "k7" }),
            ("Webroot", new[] { "webroot" }),
            ("TotalAV", new[] { "totalav" }),
            ("Emsisoft", new[] { "emsisoft" }),
            ("ZoneAlarm", new[] { "zonealarm", "check point" })
        };

        public AntivirusDetectionResult DetectInstalledThirdPartyAntivirus()
        {
            if (!OperatingSystem.IsWindows())
            {
                return AntivirusDetectionResult.Empty;
            }

            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            TryDetectFromSecurityCenter(names);
            TryDetectFromInstalledApplications(names);

            return names.Count == 0
                ? AntivirusDetectionResult.Empty
                : new AntivirusDetectionResult(names.ToArray());
        }

        private static void TryDetectFromSecurityCenter(ISet<string> names)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"\\.\root\SecurityCenter2",
                    "SELECT displayName FROM AntiVirusProduct");

                using var collection = searcher.Get();
                foreach (var item in collection)
                {
                    var rawName = item["displayName"]?.ToString();
                    TryAddDetectedName(rawName, names, requireKnownVendor: false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AntivirusDetection] Failed to query antivirus products: {ex.Message}");
            }
        }

        private static void TryDetectFromInstalledApplications(ISet<string> names)
        {
            try
            {
                foreach (var registryPath in UninstallRegistryPaths)
                {
                    using var uninstallRoot = Registry.LocalMachine.OpenSubKey(registryPath);
                    if (uninstallRoot == null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
                    {
                        using var subKey = uninstallRoot.OpenSubKey(subKeyName);
                        var displayName = subKey?.GetValue("DisplayName")?.ToString();
                        TryAddDetectedName(displayName, names, requireKnownVendor: true);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AntivirusDetection] Failed to query installed applications: {ex.Message}");
            }
        }

        private static void TryAddDetectedName(string? rawName, ISet<string> names, bool requireKnownVendor)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return;
            }

            if (IsMicrosoftDefender(rawName))
            {
                return;
            }

            var normalizedName = NormalizeProductName(rawName, requireKnownVendor);
            if (!string.IsNullOrWhiteSpace(normalizedName))
            {
                names.Add(normalizedName);
            }
        }

        private static bool IsMicrosoftDefender(string productName)
        {
            var normalized = NormalizeForComparison(productName);
            return DefenderKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeProductName(string productName, bool requireKnownVendor = false)
        {
            var normalized = NormalizeForComparison(productName);

            foreach (var mapping in KnownAntivirusMappings)
            {
                if (mapping.Keywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    return mapping.CanonicalName;
                }
            }

            if (requireKnownVendor)
            {
                return string.Empty;
            }

            return productName.Trim();
        }

        private static string NormalizeForComparison(string value)
        {
            return value
                .Trim()
                .Replace("_", " ", StringComparison.Ordinal)
                .Replace("-", " ", StringComparison.Ordinal)
                .Replace("  ", " ", StringComparison.Ordinal)
                .ToLowerInvariant();
        }
    }
}
