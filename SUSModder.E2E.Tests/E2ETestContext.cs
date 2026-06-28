using SUSModder.Core.Utilities;

namespace SUSModder.E2E.Tests;

/// <summary>
/// Shared E2E test context — paths, cleanup, artifact management.
/// All E2E tests use isolated directories under the test root.
/// Set SUSMODDER_E2E_ROOT to override, otherwise uses %TEMP%\SUSModder-E2E-Tests.
/// Set SUSMODDER_E2E_NO_CLEANUP=1 to keep artifacts after tests.
/// </summary>
public sealed class E2ETestContext : IDisposable
{
    public string RootDir { get; }
    public string ArtifactsDir { get; }
    public string DownloadsDir { get; }
    public string ExtractDir { get; }
    public string InstallDir { get; }
    public string LogsDir { get; }
    public bool NoCleanup { get; }

    public E2ETestContext(string? testSuiteName = null)
    {
        var envRoot = Environment.GetEnvironmentVariable("SUSMODDER_E2E_ROOT");
        RootDir = !string.IsNullOrWhiteSpace(envRoot)
            ? envRoot
            : Path.Combine(Path.GetTempPath(), "SUSModder-E2E-Tests");

        NoCleanup = Environment.GetEnvironmentVariable("SUSMODDER_E2E_NO_CLEANUP") == "1";

        var suiteDir = string.IsNullOrWhiteSpace(testSuiteName)
            ? RootDir
            : Path.Combine(RootDir, testSuiteName);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var runDir = Path.Combine(suiteDir, timestamp);

        ArtifactsDir = Path.Combine(runDir, "artifacts");
        DownloadsDir = Path.Combine(runDir, "downloads");
        ExtractDir = Path.Combine(runDir, "extract");
        InstallDir = Path.Combine(runDir, "installs");
        LogsDir = Path.Combine(runDir, "logs");

        Directory.CreateDirectory(ArtifactsDir);
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(ExtractDir);
        Directory.CreateDirectory(InstallDir);
        Directory.CreateDirectory(LogsDir);

        PathSettings.SetCustomPath(InstallDir);
    }

    public string GetDownloadPath(string fileName) =>
        Path.Combine(DownloadsDir, fileName);

    public string GetExtractPath(string modName) =>
        Path.Combine(ExtractDir, Sanitize(modName));

    public string GetInstallPath(string modName) =>
        Path.Combine(InstallDir, Sanitize(modName));

    public string GetLogPath(string fileName) =>
        Path.Combine(LogsDir, fileName);

    public void WriteArtifact(string name, string content) =>
        File.WriteAllText(Path.Combine(ArtifactsDir, name), content);

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }

    public void Dispose()
    {
        if (!NoCleanup && Directory.Exists(RootDir))
        {
            try
            {
                Directory.Delete(RootDir, true);
            }
            catch
            {
                // best-effort — some files may be locked by processes
            }
        }
    }
}
