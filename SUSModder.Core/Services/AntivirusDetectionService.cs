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

        public AntivirusDetectionResult(IReadOnlyList<string> productNames, bool sourcesAvailable = true)
        {
            ProductNames = productNames;
            Signature = string.Join("|", productNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            SourcesAvailable = sourcesAvailable;
        }

        public IReadOnlyList<string> ProductNames { get; }

        public string Signature { get; }
        public bool SourcesAvailable { get; }

        public bool HasThirdPartyAntivirus => ProductNames.Count > 0;
    }

    public sealed class AntivirusDetectionService
    {
        private static AntivirusDetectionResult? _lastDetectionResult;
        private static readonly object LastDetectionLock = new();

        /// <summary>
        /// Ostatni wynik wykrywania AV (np. ze startu aplikacji). Null, gdy jeszcze nie uruchomiono detekcji.
        /// </summary>
        public static AntivirusDetectionResult? LastDetectionResult
        {
            get
            {
                lock (LastDetectionLock)
                    return _lastDetectionResult;
            }
        }

        /// <summary>
        /// Zapisuje wynik detekcji do cache procesu (np. po sprawdzeniu przy starcie).
        /// </summary>
        public static void SetLastDetectionResult(AntivirusDetectionResult result)
        {
            lock (LastDetectionLock)
                _lastDetectionResult = result ?? AntivirusDetectionResult.Empty;
        }

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
                var empty = new AntivirusDetectionResult(Array.Empty<string>(), false);
                SetLastDetectionResult(empty);
                return empty;
            }

            return DetectInstalledThirdPartyAntivirusWindows(TryDetectFromSecurityCenter, TryDetectFromInstalledApplications);
        }

        [SupportedOSPlatform("windows")]
        internal AntivirusDetectionResult DetectInstalledThirdPartyAntivirusWindows(
            Func<ISet<string>, bool> securityCenterSource,
            Func<ISet<string>, bool> registrySource)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourcesAvailable = securityCenterSource(names) | registrySource(names);

            var result = names.Count == 0
                ? new AntivirusDetectionResult(Array.Empty<string>(), sourcesAvailable)
                : new AntivirusDetectionResult(names.ToArray(), sourcesAvailable);

            SetLastDetectionResult(result);
            return result;
        }

        [SupportedOSPlatform("windows")]
        private static bool TryDetectFromSecurityCenter(ISet<string> names)
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
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AntivirusDetection] Failed to query antivirus products: {ex.Message}");
                return false;
            }
        }

        [SupportedOSPlatform("windows")]
        private static bool TryDetectFromInstalledApplications(ISet<string> names)
        {
            try
            {
                bool queried = false;
                foreach (var registryPath in UninstallRegistryPaths)
                {
                    using var uninstallRoot = Registry.LocalMachine.OpenSubKey(registryPath);
                    if (uninstallRoot == null)
                        continue;
                    queried = true;

                    foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
                    {
                        using var subKey = uninstallRoot.OpenSubKey(subKeyName);
                        var displayName = subKey?.GetValue("DisplayName")?.ToString();
                        TryAddDetectedName(displayName, names, requireKnownVendor: true);
                    }
                }
                return queried;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AntivirusDetection] Failed to query installed applications: {ex.Message}");
                return false;
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
