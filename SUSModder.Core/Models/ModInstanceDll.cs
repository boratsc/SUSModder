namespace SUSModder.Core.Models
{
    /// <summary>
    /// DLL zainstalowany w konkretnej lokalnej instancji modpacka.
    /// </summary>
    public class ModInstanceDll
    {
        public long Id { get; set; }
        public string InstanceId { get; set; } = string.Empty;
        public int? DllModId { get; set; }
        public string DllName { get; set; } = string.Empty;
        public string DllVersion { get; set; } = string.Empty;
        public string Source { get; set; } = "catalog";
        public string? Sha256 { get; set; }
        public string VtStatus { get; set; } = "unknown";
        public string InstalledPath { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
    }
}
