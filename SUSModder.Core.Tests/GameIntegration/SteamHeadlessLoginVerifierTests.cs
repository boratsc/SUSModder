using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.GameIntegration.Steam;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

/// <summary>
/// Headless-weryfikacja logowania Steam (EOS) — bezpieczne granice:
/// żadnego zabijania niepotwierdzonych procesów, żadnych fałszywych sukcesów,
/// pominięcie przy uruchomionej grze, timeout i anulowanie.
/// </summary>
public sealed class SteamHeadlessLoginVerifierTests
{
    private const string SuccessMarker = "finishing flow, login status LoggedIn";

    private sealed class FakeEnv
    {
        public bool GameRunning { get; set; }
        public string? LaunchedUri { get; set; }
        public Exception? LaunchError { get; set; }
        public Func<int, string?>? LogProvider { get; set; }
        public List<string?> ReadLogPaths { get; } = new();
        public Func<string, int[]> Finder { get; set; } = _ => Array.Empty<int>();
        public Exception? FinderError { get; set; }
        public List<int> Killed { get; } = new();
        public List<string> DeletedLogs { get; } = new();
        public string LogPath { get; set; } = @"C:\AppData\SUSModder\Logs\steam-headless-test.log";

        public SteamHeadlessLoginVerifier Build(TimeSpan timeout, TimeSpan poll) => new(
            isGameRunning: () => GameRunning,
            launchUri: uri =>
            {
                if (LaunchError != null) throw LaunchError;
                LaunchedUri = uri;
            },
            readLogFile: path =>
            {
                ReadLogPaths.Add(path);
                return LogProvider?.Invoke(ReadLogPaths.Count);
            },
            findConfirmedOwnPids: token =>
            {
                if (FinderError != null) throw FinderError;
                return Finder(token);
            },
            killConfirmedPid: (pid, _) => Killed.Add(pid),
            deleteOwnLog: path => DeletedLogs.Add(path),
            logPathFactory: () => LogPath,
            timeout: timeout,
            pollInterval: poll,
            isGameInstalled: () => true);
    }

    private static readonly TimeSpan FastTimeout = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task GameAlreadyRunning_SkipsWithoutLaunchOrKill()
    {
        var env = new FakeEnv { GameRunning = true };

        var result = await env.Build(FastTimeout, FastPoll).RunAsync();

        Assert.Equal(SteamHeadlessLoginStatus.GameAlreadyRunning, result.Status);
        Assert.Null(env.LaunchedUri);
        Assert.Empty(env.Killed);
        Assert.Empty(env.DeletedLogs);
    }

    [Fact]
    public async Task GameNotInstalled_DoesNotAskSteamToInstallIt()
    {
        var launched = false;
        var verifier = new SteamHeadlessLoginVerifier(
            isGameRunning: () => false,
            isGameInstalled: () => false,
            launchUri: _ => launched = true);

        var result = await verifier.RunAsync();

        Assert.Equal(SteamHeadlessLoginStatus.GameNotInstalled, result.Status);
        Assert.False(launched);
    }

    [Fact]
    public async Task SuccessMarkerInLog_ConfirmsLogin()
    {
        var env = new FakeEnv { LogProvider = _ => $"... blah\n{SuccessMarker} - steam\n" };

        var result = await env.Build(FastTimeout, FastPoll).RunAsync();

        Assert.Equal(SteamHeadlessLoginStatus.Confirmed, result.Status);
        // Sukces: nasz własny log (potencjalne dane konta) jest usuwany.
        Assert.Equal(new[] { env.LogPath }, env.DeletedLogs);
        Assert.NotNull(env.LaunchedUri);
    }

    [Fact]
    public async Task FinishedFlowWithLoggedIn_ConfirmsLogin()
    {
        var env = new FakeEnv { LogProvider = _ => "EOS: finishing flow, login status LoggedIn" };

        var result = await env.Build(FastTimeout, FastPoll).RunAsync();

        Assert.Equal(SteamHeadlessLoginStatus.Confirmed, result.Status);
    }

    [Fact]
    public async Task NoMarker_TimeoutIsNotConfirmedAndKeepsLogForDiagnostics()
    {
        var env = new FakeEnv
        {
            LogProvider = _ => "some game output without any login signal",
            // Finder nie wskazuje żadnego procesu z naszym tokenem — nic nie wolno zabić.
            Finder = _ => Array.Empty<int>(),
        };

        var result = await env.Build(FastTimeout, FastPoll).RunAsync();

        Assert.Equal(SteamHeadlessLoginStatus.NotConfirmed, result.Status);
        Assert.Equal(env.LogPath, result.LogPath);
        Assert.Empty(env.DeletedLogs); // diagnostyka zostaje
        Assert.Empty(env.Killed);
    }

    [Fact]
    public async Task Timeout_ConfirmedOwnPidIsStopped()
    {
        var env = new FakeEnv
        {
            LogProvider = _ => null,
            Finder = token => token == "steam-headless-test.log" ? new[] { 4242 } : Array.Empty<int>(),
        };

        var result = await env.Build(FastTimeout, FastPoll).RunAsync();

        Assert.Equal(SteamHeadlessLoginStatus.NotConfirmed, result.Status);
        Assert.Equal(new[] { 4242 }, env.Killed);
        Assert.True(result.OwnInstanceStopped);
    }

    [Fact]
    public async Task OwnershipFinderFails_NeverKillsAnything()
    {
        var env = new FakeEnv
        {
            LogProvider = _ => null,
            FinderError = new InvalidOperationException("WMI niedostępne"),
        };

        var result = await env.Build(FastTimeout, FastPoll).RunAsync();

        Assert.Equal(SteamHeadlessLoginStatus.NotConfirmed, result.Status);
        Assert.Empty(env.Killed);
        Assert.True(result.OwnInstanceUnconfirmed);
    }

    [Fact]
    public async Task Cancel_ReturnsCancelledAndKeepsLog()
    {
        using var cts = new CancellationTokenSource();
        var env = new FakeEnv { LogProvider = _ => null };
        var verifier = env.Build(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10));

        var task = verifier.RunAsync(cts.Token);
        await Task.Delay(40);
        cts.Cancel();

        var result = await task;
        Assert.Equal(SteamHeadlessLoginStatus.Cancelled, result.Status);
        Assert.Equal(env.LogPath, result.LogPath);
    }

    [Fact]
    public async Task MissingSteamHandler_ReportsSteamUnavailableWithoutMonitoring()
    {
        var env = new FakeEnv { LaunchError = new Win32Exception(2, "no association") };

        var result = await env.Build(FastTimeout, FastPoll).RunAsync();

        Assert.Equal(SteamHeadlessLoginStatus.SteamUnavailable, result.Status);
        Assert.Empty(env.ReadLogPaths);
        Assert.Empty(env.Killed);
    }

    [Fact]
    public async Task LaunchThrowsOtherError_ReportsFailed()
    {
        var env = new FakeEnv { LaunchError = new InvalidOperationException("boom") };

        var result = await env.Build(FastTimeout, FastPoll).RunAsync();

        Assert.Equal(SteamHeadlessLoginStatus.Failed, result.Status);
        Assert.Equal("boom", result.Diagnostic);
    }

    [Fact]
    public void BuildLaunchUri_MatchesTestedFormatWithEscapedQuotedPath()
    {
        const string path = @"C:\Users\Jan Kowalski\AppData\Roaming\SUSModder\Logs\steam-headless-测试.log";

        var uri = SteamHeadlessLoginVerifier.BuildLaunchUri(path);

        Assert.StartsWith("steam://run/945360//", uri, StringComparison.Ordinal);
        Assert.EndsWith("/", uri, StringComparison.Ordinal);
        var args = Uri.UnescapeDataString(uri[("steam://run/945360//".Length)..^1]);
        Assert.Equal($"-batchmode -nographics -logFile \"{path}\"", args);
        // Spacje/unicode muszą być percent-encoded, aby Steam nie potłókł ścieżki.
        Assert.DoesNotContain(" ", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void LogConfirmsLogin_RejectsPartialOrUnrelatedLines()
    {
        Assert.False(SteamHeadlessLoginVerifier.LogConfirmsLogin(null));
        Assert.False(SteamHeadlessLoginVerifier.LogConfirmsLogin(""));
        Assert.False(SteamHeadlessLoginVerifier.LogConfirmsLogin("login status not logged in"));
        Assert.False(SteamHeadlessLoginVerifier.LogConfirmsLogin("finishing flow"));
        Assert.False(SteamHeadlessLoginVerifier.LogConfirmsLogin("platform login is successless attempt"));
        Assert.False(SteamHeadlessLoginVerifier.LogConfirmsLogin("Platform Login is successfully configured? nope, ambiguous"));
        Assert.False(SteamHeadlessLoginVerifier.LogConfirmsLogin("Platform Login is success for user X"));
        Assert.True(SteamHeadlessLoginVerifier.LogConfirmsLogin("FINISHING FLOW, Login Status LoggedIn"));
    }

    [Fact]
    public void CommandLineMatchesOwnLog_OnlyWithUniqueToken()
    {
        Assert.True(SteamHeadlessLoginVerifier.CommandLineMatchesOwnLog(
            @"""C:\Games\Among Us.exe"" -batchmode -nographics -logFile C:\Logs\steam-headless-abc.log",
            "steam-headless-abc.log"));
        // Obcy proces bez naszego tokena — nigdy nie jest zatwierdzany do zabicia.
        Assert.False(SteamHeadlessLoginVerifier.CommandLineMatchesOwnLog(
            @"""C:\Games\Among Us.exe""", "steam-headless-abc.log"));
        Assert.False(SteamHeadlessLoginVerifier.CommandLineMatchesOwnLog(null, "steam-headless-abc.log"));
        Assert.False(SteamHeadlessLoginVerifier.CommandLineMatchesOwnLog("x", ""));
    }

    [Fact]
    public void DefaultLogPath_IsUniqueWithAsciiToken()
    {
        var first = SteamHeadlessLoginVerifier.DefaultLogPath();
        var second = SteamHeadlessLoginVerifier.DefaultLogPath();

        Assert.NotEqual(first, second);
        var name = System.IO.Path.GetFileName(first);
        Assert.StartsWith("steam-headless-", name, StringComparison.Ordinal);
        Assert.EndsWith(".log", name, StringComparison.Ordinal);
        foreach (var c in name)
            Assert.True(c < 128, $"token musi być ASCII: {c}");
    }
}
