namespace SUSModder.Core.Models
{
    /// <summary>
    /// Snapshot konfiguracji przypisany do konkretnej lokalnej instancji modpacka.
    /// </summary>
    public class ModInstanceConfig
    {
        public long Id { get; set; }
        public string InstanceId { get; set; } = string.Empty;
        public string ConfigType { get; set; } = string.Empty;
        public string ConfigName { get; set; } = string.Empty;
        public string ConfigJson { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
