using System.Runtime.Versioning;

namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Windows implementacja IHardwareIdProvider.
    /// Opakowuje istniejącą statyczną logikę HardwareIdProvider.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsHardwareIdProvider : IHardwareIdProvider
    {
        /// <inheritdoc/>
        public string GetAnonymousUserHash()
        {
            return HardwareIdProvider.GetAnonymousUserHash();
        }
    }
}
