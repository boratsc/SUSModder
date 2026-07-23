using System;
using System.Management;
using System.Runtime.Versioning;
using System.Text;

namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Generuje anonimowy hash użytkownika na podstawie Hardware ID
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class HardwareIdProvider
    {
        private static string? _cachedHash;

        /// <summary>
        /// Pobiera anonimowy hash użytkownika (SHA256 z Hardware ID)
        /// </summary>
        /// <returns>64-znakowy hex string (SHA256)</returns>
        public static string GetAnonymousUserHash()
        {
            if (!string.IsNullOrEmpty(_cachedHash))
            {
                // Napraw cache z historycznego fallbacku GUID "N" (32 hex).
                if (!AnonymousUserHash.IsValid(_cachedHash))
                    _cachedHash = AnonymousUserHash.EnsureValid(_cachedHash);
                return _cachedHash;
            }

            try
            {
                // Zbierz unikalne identyfikatory sprzętowe
                var hardwareId = GetHardwareIdentifier();

                // Zahashuj SHA256 (jednostronnie - nie da się odtworzyć oryginalnych danych)
                // API (creatorHash / X-User-Hash) wymaga dokładnie 64 lowercase hex.
                _cachedHash = AnonymousUserHash.EnsureValid(AnonymousUserHash.ComputeSha256Hex(hardwareId));

                return _cachedHash;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to generate hardware hash: {ex.Message}");

                // Fallback: SHA256 z losowego seeda — NIGDY surowy GUID "N" (32 hex),
                // bo backend odrzuca to jako "Invalid creatorHash format (64 hex chars)".
                _cachedHash = AnonymousUserHash.CreateFallback();
                return _cachedHash;
            }
        }

        /// <summary>
        /// Pobiera unikalne identyfikatory sprzętowe (CPU + Motherboard + BIOS)
        /// </summary>
        private static string GetHardwareIdentifier()
        {
            var sb = new StringBuilder();

            try
            {
                // CPU ID
                var cpuId = GetWmiProperty("Win32_Processor", "ProcessorId");
                sb.Append(cpuId ?? "UNKNOWN_CPU");

                // Motherboard Serial
                var mbSerial = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
                sb.Append(mbSerial ?? "UNKNOWN_MB");

                // BIOS Serial
                var biosSerial = GetWmiProperty("Win32_BIOS", "SerialNumber");
                sb.Append(biosSerial ?? "UNKNOWN_BIOS");

                // Machine GUID (Windows Registry)
                var machineGuid = GetMachineGuid();
                sb.Append(machineGuid ?? "UNKNOWN_GUID");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting hardware ID: {ex.Message}");
                // Jeśli nie można pobrać - użyj timestamp jako salt
                sb.Append(Environment.MachineName);
                sb.Append(Environment.UserName);
                sb.Append(DateTime.UtcNow.Ticks.ToString());
            }

            return sb.ToString();
        }

        /// <summary>
        /// Pobiera właściwość WMI
        /// </summary>
        private static string? GetWmiProperty(string wmiClass, string property)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
                using var collection = searcher.Get();

                foreach (var obj in collection)
                {
                    var value = obj[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
            catch
            {
                // Ignore WMI errors
            }

            return null;
        }

        /// <summary>
        /// Pobiera Machine GUID z Windows Registry
        /// </summary>
        private static string? GetMachineGuid()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return key?.GetValue("MachineGuid")?.ToString();
            }
            catch
            {
                return null;
            }
        }

    }
}
