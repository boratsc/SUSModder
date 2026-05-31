using System;
using System.IO;

namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Katalog aplikacji (appsettings.json, tools/) — nie katalog dotnet.exe przy F5 / dotnet run.
    /// </summary>
    public static class ApplicationPaths
    {
        public static string GetApplicationDirectory()
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
            {
                var name = Path.GetFileName(processPath);
                if (!string.Equals(name, "dotnet.exe", StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory;
            }

            return AppContext.BaseDirectory;
        }

        public static string AppSettingsPath =>
            Path.Combine(GetApplicationDirectory(), "appsettings.json");
    }
}
