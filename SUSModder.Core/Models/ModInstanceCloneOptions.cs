namespace SUSModder.Core.Models
{
    /// <summary>
    /// Opcje klonowania lokalnej instancji modpacka.
    /// </summary>
    public sealed class ModInstanceCloneOptions
    {
        public string NewDisplayName { get; set; } = string.Empty;
        public bool CopyDlls { get; set; } = true;
        public bool CopyTouConfig { get; set; } = true;
        public bool CopyIntegrationDll { get; set; } = true;
        public bool CopyPinnedVersion { get; set; } = true;
    }
}
