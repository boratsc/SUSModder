using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.GameIntegration.Steam;

public sealed partial class DepotDownloaderRunner
{
    public const string Version = "3.4.0";
    public const string SteamAppId = "945360";
    public const string SteamDepotId = "945361";

    private const string LinuxX64Sha256 = "a999dec66b4850fc961bd50366696d23c2d0fad7b18790e6a5647b2f19097a53";
    private const string WindowsX64Sha256 = "41c9e9f0df54b3ad02e67a11726756e5c73283bd7c2e1b04acfa5ae4c2ed3767";

    private static readonly SemaphoreSlim EnsureSemaphore = new(1, 1);
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private static readonly DdAccountConfigWriter ConfigWriter = new();

    private static readonly string ToolsBaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SUSModder", "tools");

    private static readonly string DepotDownloaderDir =
        Path.Combine(ToolsBaseDir, $"depotdownloader-{Version}");

    public Action<DepotDownloadProgress>? OnProgress { get; set; }
    public Action<string>? OnLogLine { get; set; }

    public static string[] BuildArgs(string targetDirectory, string? manifestId, bool useQrAuth)
    {
        var args = new List<string>
        {
            "-app", SteamAppId,
            "-depot", SteamDepotId,
            "-dir", targetDirectory
        };

        if (!string.IsNullOrWhiteSpace(manifestId))
        {
            args.Add("-manifest");
            args.Add(manifestId);
        }

        if (useQrAuth)
        {
            args.Add("-qr");
            args.Add("-remember-password");
        }
        else if (ConfigWriter.GetAnyTokenUsername() is { } savedUsername)
        {
            args.Add("-username");
            args.Add(savedUsername);
            args.Add("-remember-password");
        }

        return args.ToArray();
    }

    public bool HasSavedCredentials() => ConfigWriter.HasAnyToken();

    public async Task RunDownloadAsync(
        string targetDirectory,
        string manifestId,
        bool useQrAuth,
        IDiagnosticsOutput? log,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(targetDirectory);

        var executablePath = await EnsureDepotDownloaderAsync(log, ct);
        var args = BuildArgs(targetDirectory, manifestId, useQrAuth);

        ConfigWriter.CleanCorrupt();
        ConfigWriter.RestoreFromCache();

        var outputEncoding = ResolveProcessOutputEncoding();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = outputEncoding,
                StandardErrorEncoding = outputEncoding
            }
        };

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        log?.Write($"[DepotDownloader] Uruchamiam: {executablePath} {string.Join(' ', args)}");
        process.Start();

        var outputLog = new StringBuilder();
        var pumpState = new StreamPumpState();

        var stdoutReader = Task.Run(() => PumpStreamAsync(
            process.StandardOutput.BaseStream,
            outputLog,
            log,
            "[DepotDownloader]",
            ct,
            pumpState,
            process), ct);

        var stderrReader = Task.Run(() => PumpStreamAsync(
            process.StandardError.BaseStream,
            outputLog,
            log,
            "[DepotDownloader:ERR]",
            ct,
            pumpState,
            process), ct);

        await RunToCompletionAsync(
            process,
            stdoutReader,
            stderrReader,
            outputLog,
            log,
            ct,
            timeout ?? TimeSpan.FromMinutes(45));
    }

    private sealed class StreamPumpState
    {
        public bool TwofaHandled;
        public int FilesDownloaded;
    }

    private async Task PumpStreamAsync(
        Stream stream,
        StringBuilder outputLog,
        IDiagnosticsOutput? log,
        string prefix,
        CancellationToken ct,
        StreamPumpState state,
        Process process)
    {
        var buffer = new byte[4096];
        var textBuf = new StringBuilder();
        var outputDecoder = new ProcessStreamDecoder(ResolveProcessOutputEncoding());

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(buffer, ct);
                if (bytesRead == 0)
                    break;

                var decoded = outputDecoder.Decode(buffer, bytesRead);
                textBuf.Append(decoded);
                outputLog.Append(decoded);

                if (!state.TwofaHandled && ContainsTwoFactorPrompt(textBuf.ToString()))
                {
                    state.TwofaHandled = true;
                    log?.Write("[DepotDownloader] Wymagany kod Steam Guard — użyj logowania QR.");
                    try { process.StandardInput.Close(); } catch { }
                    textBuf.Clear();
                    continue;
                }

                FlushLines(textBuf, prefix, log, OnLogLine, line =>
                {
                    if (line.StartsWith("[+]", StringComparison.Ordinal))
                    {
                        var fileName = line[3..].Trim();
                        state.FilesDownloaded++;
                        OnProgress?.Invoke(new DepotDownloadProgress(state.FilesDownloaded, fileName));
                        return;
                    }

                    var progressMatch = PercentProgressRegex().Match(line);
                    if (progressMatch.Success
                        && double.TryParse(
                            progressMatch.Groups["percent"].Value,
                            System.Globalization.NumberStyles.AllowDecimalPoint,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var percent))
                    {
                        var fileName = progressMatch.Groups["file"].Value.Trim();
                        OnProgress?.Invoke(new DepotDownloadProgress(
                            state.FilesDownloaded,
                            string.IsNullOrWhiteSpace(fileName) ? null : fileName,
                            percent));
                    }
                });
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on cancel.
        }
    }

    private static bool ContainsTwoFactorPrompt(string text) =>
        text.Contains("Two-factor code:", StringComparison.OrdinalIgnoreCase)
        || text.Contains("STEAM GUARD!", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\s*(?<percent>\d+(?:\.\d+)?)%\s+(?<file>.+)$")]
    private static partial Regex PercentProgressRegex();

    private static void FlushLines(
        StringBuilder textBuf,
        string prefix,
        IDiagnosticsOutput? log,
        Action<string>? logLineCallback,
        Action<string>? onLine = null)
    {
        var full = textBuf.ToString();
        int nl;
        while ((nl = full.IndexOf('\n')) >= 0)
        {
            var line = full[..nl].TrimEnd('\r');
            if (!string.IsNullOrEmpty(line))
            {
                log?.Write($"{prefix} {line}");
                logLineCallback?.Invoke(line);
                onLine?.Invoke(line);
            }

            full = full[(nl + 1)..];
        }

        textBuf.Clear();
        textBuf.Append(full);
    }

    private static async Task RunToCompletionAsync(
        Process process,
        Task stdoutReader,
        Task stderrReader,
        StringBuilder outputLog,
        IDiagnosticsOutput? log,
        CancellationToken ct,
        TimeSpan timeout)
    {
        try
        {
            var processTask = process.WaitForExitAsync(ct);
            var timeoutTask = Task.Delay(timeout, ct);
            var completed = await Task.WhenAny(processTask, timeoutTask);

            if (completed == timeoutTask)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException(
                    $"DepotDownloader przekroczył limit czasu ({timeout.TotalMinutes:0} min).");
            }

            await stdoutReader;
            await stderrReader;

            ConfigWriter.BackupToCache();

            if (process.ExitCode != 0)
            {
                var logText = outputLog.ToString();
                if (ContainsTwoFactorPrompt(logText))
                {
                    throw new InvalidOperationException(
                        "Wymagane logowanie Steam (QR). Sesja wygasła — zaloguj się ponownie.");
                }

                var errorDetail = ParseDdError(logText);
                throw new InvalidOperationException(
                    $"DepotDownloader zakończył się kodem {process.ExitCode}: {errorDetail}");
            }

            var username = TryGetUsername(process.StartInfo.ArgumentList);
            if (!string.IsNullOrWhiteSpace(username))
                ConfigWriter.MarkSuccessfulLogin(username);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private static string? TryGetUsername(IList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "-username", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    public static string ParseDdError(string output)
    {
        if (output.Contains("InvalidPassword", StringComparison.OrdinalIgnoreCase))
            return "Nieprawidłowe hasło Steam lub wygasły token. Zaloguj się ponownie przez QR.";
        if (output.Contains("RateLimitExceeded", StringComparison.OrdinalIgnoreCase))
            return "Zbyt wiele prób logowania. Poczekaj ok. 15 minut.";
        if (output.Contains("not available from this account", StringComparison.OrdinalIgnoreCase))
            return "Twoje konto Steam nie posiada Among Us w bibliotece.";
        if (output.Contains("TwoFactorCodeMismatch", StringComparison.OrdinalIgnoreCase))
            return "Nieprawidłowy kod Steam Guard.";
        if (output.Contains("AccountLogonDenied", StringComparison.OrdinalIgnoreCase))
            return "Steam Guard odrzucił logowanie. Zaloguj się ponownie przez QR.";

        var lastLines = output.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(3);
        return string.Join(" | ", lastLines);
    }

    public static bool IsQrArtLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var blockChars = 0;
        foreach (var ch in line)
        {
            if (ch is ' ' or '\t')
                continue;

            if (IsQrGlyph(ch))
            {
                blockChars++;
                continue;
            }

            return false;
        }

        return blockChars >= 8;
    }

    public static bool TryExtractQrBlock(IEnumerable<string> lines, out string qrBlock)
    {
        var blockLines = new List<string>();
        foreach (var line in lines)
        {
            if (IsQrArtLine(line))
                blockLines.Add(line);
            else if (blockLines.Count > 0)
                break;
        }

        if (blockLines.Count >= 3)
        {
            qrBlock = string.Join(Environment.NewLine, blockLines);
            return true;
        }

        qrBlock = string.Empty;
        return false;
    }

    private static bool IsQrGlyph(char ch) =>
        ch is '█' or '▀' or '▄' or '▌' or '▐' or '░' or '▒' or '▓'
        || ch is >= '\u2580' and <= '\u259F';

    internal static Encoding ResolveProcessOutputEncoding()
    {
        EnsureCodePagesRegistered();

        if (!OperatingSystem.IsWindows())
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // DepotDownloader (redirected stdout, brak okna konsoli) wypisuje AsciiQRCode
        // bajtami OEM — UTF-8 daje U+FFFD i UI nie widzi kodu QR.
        try
        {
            var oemCodePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            if (oemCodePage > 0)
                return Encoding.GetEncoding(oemCodePage);
        }
        catch (ArgumentException)
        {
            // Fallback poniżej.
        }

        return Encoding.GetEncoding(437);
    }

    private static readonly Lock CodePagesLock = new();
    private static bool _codePagesRegistered;

    private static void EnsureCodePagesRegistered()
    {
        if (_codePagesRegistered)
            return;

        lock (CodePagesLock)
        {
            if (_codePagesRegistered)
                return;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _codePagesRegistered = true;
        }
    }

    private sealed class ProcessStreamDecoder(Encoding encoding)
    {
        private readonly Decoder _decoder = encoding.GetDecoder();

        public string Decode(byte[] buffer, int count)
        {
            var chars = new char[encoding.GetMaxCharCount(count)];
            var charCount = _decoder.GetChars(buffer, 0, count, chars, 0);
            return new string(chars, 0, charCount);
        }
    }

    private static async Task<string> EnsureDepotDownloaderAsync(IDiagnosticsOutput? log, CancellationToken ct)
    {
        var executablePath = Path.Combine(DepotDownloaderDir, OperatingSystem.IsWindows()
            ? "DepotDownloader.exe"
            : "DepotDownloader");

        if (File.Exists(executablePath))
            return executablePath;

        await EnsureSemaphore.WaitAsync(ct);
        try
        {
            if (File.Exists(executablePath))
                return executablePath;

            Directory.CreateDirectory(DepotDownloaderDir);

            var downloadUrl = OperatingSystem.IsWindows()
                ? $"https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_{Version}/DepotDownloader-windows-x64.zip"
                : $"https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_{Version}/DepotDownloader-linux-x64.zip";

            var expectedSha256 = OperatingSystem.IsWindows() ? WindowsX64Sha256 : LinuxX64Sha256;

            var zipPath = Path.Combine(DepotDownloaderDir, "depotdownloader.zip");
            var tmpPath = $"{zipPath}.tmp.{Guid.NewGuid():N}";

            log?.Write($"[DepotDownloader] Pobieram {downloadUrl}...");

            await using (var downloadStream = await SharedHttpClient.GetStreamAsync(downloadUrl, ct))
            await using (var fileStream = File.Create(tmpPath))
            {
                await downloadStream.CopyToAsync(fileStream, ct);
            }

            using var sha = SHA256.Create();
            await using var verifyStream = File.OpenRead(tmpPath);
            var hash = await sha.ComputeHashAsync(verifyStream, ct);
            var actualHex = Convert.ToHexString(hash).ToLowerInvariant();
            if (!string.Equals(actualHex, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tmpPath);
                throw new InvalidOperationException(
                    $"Weryfikacja DepotDownloader nie powiodła się: oczekiwano {expectedSha256}, otrzymano {actualHex}");
            }

            verifyStream.Close();
            File.Move(tmpPath, zipPath, overwrite: true);
            ZipFile.ExtractToDirectory(zipPath, DepotDownloaderDir, overwriteFiles: true);
            File.Delete(zipPath);

            if (!OperatingSystem.IsWindows())
            {
                var chmod = Process.Start(new ProcessStartInfo("chmod", $"+x {executablePath}")
                {
                    UseShellExecute = false
                });
                if (chmod is not null)
                    await chmod.WaitForExitAsync(ct);
            }

            return executablePath;
        }
        finally
        {
            EnsureSemaphore.Release();
        }
    }
}
