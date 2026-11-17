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
        private static readonly object HashLock = new();
        private static string? _cachedHash;

        /// <summary>
        /// Pobiera anonimowy hash użytkownika (SHA256 z Hardware ID)
        /// </summary>
        /// <returns>64-znakowy hex string (SHA256)</returns>
        public static string GetAnonymousUserHash()
        {
            lock (HashLock)
            {
                if (!string.IsNullOrEmpty(_cachedHash))
                {
                    if (!AnonymousUserHash.IsValid(_cachedHash))
                        _cachedHash = AnonymousUserHash.EnsureValid(_cachedHash);
                    return _cachedHash;
                }

                try
                {
                    var hardwareId = GetHardwareIdentifier();
                    _cachedHash = AnonymousUserHash.EnsureValid(AnonymousUserHash.ComputeSha256Hex(hardwareId));
                    return _cachedHash;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to generate hardware hash: {ex.Message}");
                    _cachedHash = AnonymousUserHash.CreateFallback();
                    return _cachedHash;
                }
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
                // Stały materiał diagnostyczny; fallback przy błędzie zapewnia trwały hash.
                sb.Append(Environment.MachineName);
                sb.Append(Environment.UserName);
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
