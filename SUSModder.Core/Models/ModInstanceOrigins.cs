namespace SUSModder.Core.Models
{
    /// <summary>
    /// Wartości pola origin w mod_instances. Tylko „prawdziwe” zestawy trafiają do UI Moje zestawy.
    /// </summary>
    public static class ModInstanceOrigins
    {
        public const string Manual = "manual";
        public const string SharedPack = "shared_pack";
        public const string Clone = "clone";

        /// <summary>
        /// Wpis zsynchronizowany z katalogowym mods.InstallPath — nie jest zestawem użytkownika.
        /// </summary>
        public const string Legacy = "legacy";

        public static bool IsUserPack(string? origin) =>
            string.Equals(origin, Manual, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(origin, SharedPack, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(origin, Clone, StringComparison.OrdinalIgnoreCase);
    }
}
