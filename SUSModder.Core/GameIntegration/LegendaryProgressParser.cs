using System;
using System.Text.RegularExpressions;

namespace SUSModder.Core.GameIntegration
{
    public class LegendaryProgressParser
    {
        private static readonly Regex ProgressRegex = new Regex(@"= Progress: (\d+\.?\d*)% \((\d+)/(\d+)\)", RegexOptions.Compiled);
        private static readonly Regex DownloadSizeRegex = new Regex(@"Download size: ([\d.]+) (\w+)", RegexOptions.Compiled);
        private static readonly Regex InstallSizeRegex = new Regex(@"Install size: ([\d.]+) (\w+)", RegexOptions.Compiled);
        private static readonly Regex EtaRegex = new Regex(@"ETA: (\d{2}:\d{2}:\d{2})", RegexOptions.Compiled);
        private static readonly Regex SpeedRegex = new Regex(@"Download\s+-\s+([\d.]+) (\w+)/s", RegexOptions.Compiled);

        public static LegendaryProgress? ParseProgress(string logLine)
        {
            if (string.IsNullOrEmpty(logLine))
                return null;

            var progress = new LegendaryProgress();
            bool hasData = false;

            // Parse progress percentage
            var progressMatch = ProgressRegex.Match(logLine);
            if (progressMatch.Success)
            {
                if (double.TryParse(progressMatch.Groups[1].Value, out double percentage))
                {
                    progress.ProgressPercentage = percentage;
                    progress.CurrentFiles = int.Parse(progressMatch.Groups[2].Value);
                    progress.TotalFiles = int.Parse(progressMatch.Groups[3].Value);
                    hasData = true;
                }
            }

            // Parse download size
            var downloadMatch = DownloadSizeRegex.Match(logLine);
            if (downloadMatch.Success)
            {
                progress.DownloadSize = $"{downloadMatch.Groups[1].Value} {downloadMatch.Groups[2].Value}";
                hasData = true;
            }

            // Parse install size
            var installMatch = InstallSizeRegex.Match(logLine);
            if (installMatch.Success)
            {
                progress.InstallSize = $"{installMatch.Groups[1].Value} {installMatch.Groups[2].Value}";
                hasData = true;
            }

            // Parse ETA
            var etaMatch = EtaRegex.Match(logLine);
            if (etaMatch.Success)
            {
                progress.ETA = etaMatch.Groups[1].Value;
                hasData = true;
            }

            // Parse download speed
            var speedMatch = SpeedRegex.Match(logLine);
            if (speedMatch.Success)
            {
                progress.DownloadSpeed = $"{speedMatch.Groups[1].Value} {speedMatch.Groups[2].Value}/s";
                hasData = true;
            }

            return hasData ? progress : null;
        }

        public static string GetPhase(string logLine)
        {
            if (logLine.Contains("Preparing download"))
                return "Przygotowywanie pobierania...";
            if (logLine.Contains("Parsing game manifest"))
                return "Analizowanie manifestu gry...";
            if (logLine.Contains("Starting download workers"))
                return "Rozpoczynanie pobierania...";
            if (logLine.Contains("= Progress:"))
                return "Pobieranie w toku...";
            if (logLine.Contains("Waiting for installation to finish"))
                return "Finalizowanie instalacji...";
            if (logLine.Contains("Finished installation process"))
                return "Instalacja zakończona!";
            if (logLine.Contains("Launching"))
                return "Uruchamianie gry...";

            return string.Empty;
        }
    }

    public class LegendaryProgress
    {
        public double ProgressPercentage { get; set; }
        public int CurrentFiles { get; set; }
        public int TotalFiles { get; set; }
        public string? DownloadSize { get; set; }
        public string? InstallSize { get; set; }
        public string? ETA { get; set; }
        public string? DownloadSpeed { get; set; }
        public string Phase { get; set; } = string.Empty;

        public string GetStatusMessage()
        {
            var parts = new List<string>();

            if (ProgressPercentage > 0)
                parts.Add($"{ProgressPercentage:F1}%");

            if (!string.IsNullOrEmpty(ETA) && ETA != "00:00:00")
                parts.Add($"ETA: {ETA}");

            if (!string.IsNullOrEmpty(DownloadSpeed))
                parts.Add($"Prędkość: {DownloadSpeed}");

            if (CurrentFiles > 0 && TotalFiles > 0)
                parts.Add($"Pliki: {CurrentFiles}/{TotalFiles}");

            return string.Join(" | ", parts);
        }
    }
}
