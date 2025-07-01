namespace SUSModder.Models;

public class Mod
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GitHubUrl { get; set; } = string.Empty;
    public string? IconPath { get; set; } // Ścieżka do ikony
    public string? InstallPath { get; set; }
    public bool IsInstalled => !string.IsNullOrEmpty(InstallPath);
}