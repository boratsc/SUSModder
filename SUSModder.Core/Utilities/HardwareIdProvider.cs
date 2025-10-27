using System;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
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
                return _cachedHash;

            try
            {
                // Zbierz unikalne identyfikatory sprzętowe
                var hardwareId = GetHardwareIdentifier();

                // Zahashuj SHA256 (jednostronnie - nie da się odtworzyć oryginalnych danych)
                _cachedHash = ComputeSha256Hash(hardwareId);

                return _cachedHash;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to generate hardware hash: {ex.Message}");

                // Fallback - losowy GUID (będzie się zmieniał przy każdym uruchomieniu)
                // Lepszy niż brak telemetrii, ale nie idealny
                _cachedHash = Guid.NewGuid().ToString("N");
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

        /// <summary>
        /// Oblicza SHA256 hash
        /// </summary>
        private static string ComputeSha256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = sha256.ComputeHash(bytes);

            // Konwersja do hex string
            var sb = new StringBuilder();
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
