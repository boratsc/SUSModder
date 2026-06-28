using System;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Lokalna instancja modpacka: konkretna instalacja moda FULL z własnym folderem i metadanymi.
    /// </summary>
    public class ModInstance
    {
        public string InstanceId { get; set; } = Guid.NewGuid().ToString("D");
        public string DisplayName { get; set; } = string.Empty;
        public int BaseModId { get; set; }
        public string BaseModName { get; set; } = string.Empty;
        public string FullModVersion { get; set; } = string.Empty;
        public string AmongVersion { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string InstallPath { get; set; } = string.Empty;
        public string Origin { get; set; } = "manual";
        public string? SourcePackCode { get; set; }
        public string? PinnedVersion { get; set; }
        public bool AutoUpdateEnabled { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
        public string? LastLaunchedAt { get; set; }
    }
}
