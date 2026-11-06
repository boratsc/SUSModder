using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

/// <summary>
/// Zewnętrzny przepływ legendary: odświeżanie base_urls (import do pustego katalogu),
/// instalacja z przypiętym manifestem, ponawianie po błędzie i przerwanie po timeoutcie.
/// Testy używają symulowanego runnera - bez prawdziwego legendary i Epic Games.
/// </summary>
public class EpicVersionManagerLegendaryFlowTests
{
    private const string AppId = "963137e4c29d4c79a81323b8fab03a40";
    private const string BaseUrl = "https://cdn.example.com/Builds/Org/ns/build/default";
    private const string AmongVersion = "2026.3.17";

    [Fact]
    public async Task Install_WithBaseUrls_DoesNotWarmUp()
    {
        using var sandbox = new LegendaryConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);

        var runner = new FakeLegendaryRunner(_ => LegendaryRunResult.Success());
        var manager = CreateManager(runner, sandbox);

        bool result = await manager.RunPinnedManifestInstallAsync(InstallArguments);

        Assert.True(result);
        Assert.Single(runner.Commands);
        Assert.Equal(InstallArguments, runner.Commands[0]);
    }

    [Fact]
    public async Task Install_WithoutBaseUrls_WarmsUpWithImportIntoEmptyFolder()
    {
        using var sandbox = new LegendaryConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);

        string? warmupPath = null;
        var runner = new FakeLegendaryRunner(arguments =>
        {
            if (IsWarmup(arguments))
            {
                // Import wskazuje na pusty katalog - legendary nie znajdzie plików gry i nic nie zarejestruje.
                warmupPath = ExtractQuotedPath(arguments);
                Assert.True(Directory.Exists(warmupPath));
                Assert.Empty(Directory.GetFileSystemEntries(warmupPath));

                // legendary zapisuje świeże adresy CDN w metadanych
                sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
                return LegendaryRunResult.Success();
            }

            return LegendaryRunResult.Success();
        });
        var manager = CreateManager(runner, sandbox);

        bool result = await manager.RunPinnedManifestInstallAsync(InstallArguments);

        Assert.True(result);
        Assert.Equal(2, runner.Commands.Count);

        string warmup = runner.Commands[0];
        Assert.StartsWith($"import {AppId}", warmup);
        Assert.DoesNotContain("--prefix", warmup);
        Assert.DoesNotContain("install", warmup);

        // Po czystym zakończeniu procesu katalog pomocniczy jest usuwany.
        Assert.False(Directory.Exists(warmupPath!));

        Assert.Equal(InstallArguments, runner.Commands[1]);
    }

    [Fact]
    public async Task Install_WithRegisteredGame_SkipsWarmupAndKeepsOriginalFallback()
    {
        using var sandbox = new LegendaryConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);
        sandbox.WriteInstalledRegistered(AppId);

        var runner = new FakeLegendaryRunner(arguments =>
            arguments.Contains("--manifest")
                ? new LegendaryRunResult(1, new[] { "ValueError: No base URLs found, please try again." })
                : LegendaryRunResult.Success());
        var manager = CreateManager(runner, sandbox);

        bool result = await manager.RunPinnedManifestInstallAsync(InstallArguments);

        // Odświeżanie pomijamy (gra zarejestrowana), ale pierwotna ścieżka próbuje instalacji.
        Assert.DoesNotContain(runner.Commands, c => IsWarmup(c));
        Assert.Contains(runner.Commands, c => c.Contains("--manifest"));
        Assert.False(result);
    }

    [Fact]
    public async Task Install_WhenRefreshFailsButProcessEnded_StillTriesPinnedInstall()
    {
        using var sandbox = new LegendaryConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);

        // Odświeżenie kończy się bez błędu timeoutu, ale base_urls nadal nie ma.
        var runner = new FakeLegendaryRunner(arguments =>
            IsWarmup(arguments)
                ? new LegendaryRunResult(1, new[] { "ERROR: no manifest" })
                : LegendaryRunResult.Success());
        var manager = CreateManager(runner, sandbox);

        bool result = await manager.RunPinnedManifestInstallAsync(InstallArguments);

        // HasBaseUrls może nie umieć odczytać konfiguracji, a legendary umie - próbujemy install.
        Assert.True(result);
        Assert.Equal(2, runner.Commands.Count);
        Assert.StartsWith($"import {AppId}", runner.Commands[0]);
        Assert.Equal(InstallArguments, runner.Commands[1]);
    }

    [Fact]
    public async Task Install_RetriesAfterMissingBaseUrlsError()
    {
        using var sandbox = new LegendaryConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);

        int installAttempts = 0;
        var runner = new FakeLegendaryRunner(arguments =>
        {
            if (IsWarmup(arguments))
            {
                sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
                return LegendaryRunResult.Success();
            }

            installAttempts++;
            return installAttempts == 1
                ? new LegendaryRunResult(1, new[] { "ValueError: No base URLs found, please try again." })
                : LegendaryRunResult.Success();
        });
        var manager = CreateManager(runner, sandbox);

        bool result = await manager.RunPinnedManifestInstallAsync(InstallArguments);

        Assert.True(result);
        Assert.Equal(3, runner.Commands.Count);
        Assert.Equal(InstallArguments, runner.Commands[0]);
        Assert.StartsWith($"import {AppId}", runner.Commands[1]);
        Assert.Equal(InstallArguments, runner.Commands[2]);
    }

    [Fact]
    public async Task Install_WhenWarmupTimesOut_AbortsWithoutFurtherCommands()
    {
        using var sandbox = new LegendaryConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);

        var runner = new FakeLegendaryRunner(arguments =>
            IsWarmup(arguments)
                ? new LegendaryRunResult(-1, timedOut: true)
                : LegendaryRunResult.Success());
        var manager = CreateManager(runner, sandbox);

        bool result = await manager.RunPinnedManifestInstallAsync(InstallArguments);

        // Drzewo procesów mogło przeżyć - nie wolno uruchamiać kolejnych komend legendary.
        Assert.False(result);
        Assert.Single(runner.Commands);
        Assert.StartsWith($"import {AppId}", runner.Commands[0]);

        // Katalogu nie usuwamy po timeoutcie - proces mógł go jeszcze używać.
        string warmupPath = ExtractQuotedPath(runner.Commands[0]);
        Assert.True(Directory.Exists(warmupPath));
        Directory.Delete(warmupPath, recursive: true);
    }

    [Fact]
    public async Task InstallGameAsync_WhenManifestMissing_FailsWithoutRunningLegendary()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();

        var runner = new FakeLegendaryRunner(_ => LegendaryRunResult.Success());
        var manager = CreateManager(runner, sandbox, manifestDirectory.Path);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        bool result = await manager.InstallGameAsync(modConfig, AmongVersion);

        Assert.False(result);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task InstallGameAsync_WhenPinnedInstallFails_DoesNotLaunchAndReturnsFalse()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
        manifestDirectory.WriteFile($"{AppId}_{AmongVersion}.manifest", "manifest");

        var runner = new FakeLegendaryRunner(arguments =>
            arguments.Contains("launch")
                ? LegendaryRunResult.Success()
                : new LegendaryRunResult(1, new[] { "ERROR: install failed" }));
        var manager = CreateManager(runner, sandbox, manifestDirectory.Path);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        bool result = await manager.InstallGameAsync(modConfig, AmongVersion);

        Assert.False(result);
        Assert.DoesNotContain(runner.Commands, c => c.Contains("launch"));
    }

    [Fact]
    public async Task InstallGameAsync_WhenPinnedInstallFails_DoesNotMoveGamePayload()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
        manifestDirectory.WriteFile($"{AppId}_{AmongVersion}.manifest", "manifest");

        // Katalog gry z plikiem payloadu, który EnsureEpicPayloadInsideGameDirectory przenosi do AmongUs.
        string gameDirectory = Path.Combine(manifestDirectory.Path, "AmongUs");
        Directory.CreateDirectory(gameDirectory);
        string payload = Path.Combine(manifestDirectory.Path, "winhttp.dll");
        File.WriteAllText(payload, "payload");

        var runner = new FakeLegendaryRunner(arguments =>
            new LegendaryRunResult(1, new[] { "ERROR: install failed" }));
        var manager = CreateManager(runner, sandbox, manifestDirectory.Path);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        bool result = await manager.InstallGameAsync(modConfig, AmongVersion);

        Assert.False(result);
        Assert.True(File.Exists(payload));
        Assert.False(File.Exists(Path.Combine(gameDirectory, "winhttp.dll")));
    }

    [Fact]
    public async Task TerminateLegendaryProcessAsync_WhenTerminationThrows_DoesNotPropagate()
    {
        // Wyjątek z kończenia procesu po timeoutcie nie może przerwać obsługi timeoutu.
        await EpicVersionManager.TerminateLegendaryProcessAsync(
            hasExited: () => false,
            kill: () => throw new InvalidOperationException("kill failed"),
            waitForExitAsync: _ => throw new IOException("wait failed"),
            grace: TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void LegendaryProcessGuard_BlocksUntilReset()
    {
        LegendaryProcessGuard.Reset();
        try
        {
            Assert.False(LegendaryProcessGuard.IsBlocked());

            // Blokada po niepewnym timeoutcie trwa do restartu aplikacji.
            LegendaryProcessGuard.MarkUnfinished();
            Assert.True(LegendaryProcessGuard.IsBlocked());
            Assert.True(LegendaryProcessGuard.IsBlocked());
        }
        finally
        {
            LegendaryProcessGuard.Reset();
        }
    }

    [Fact]
    public async Task ExecuteLegendaryProcessAsync_WhenProcessDoesNotStart_DoesNotBlockFurtherCommands()
    {
        using var sandbox = new LegendaryConfigSandbox();
        var runner = new FakeLegendaryRunner(_ => LegendaryRunResult.Success());
        var manager = CreateManager(runner, sandbox);

        LegendaryProcessGuard.Reset();
        try
        {
            string missingExecutable = Path.Combine(
                Path.GetTempPath(),
                "susmodder-no-such-legendary-" + Guid.NewGuid().ToString("N") + ".exe");

            var result = await manager.ExecuteLegendaryProcessAsync(
                missingExecutable,
                "--version",
                TimeSpan.FromSeconds(5),
                null);

            // Proces nie wystartował - nic nie działa w tle, więc blokada nie jest potrzebna.
            Assert.NotEqual(0, result.ExitCode);
            Assert.False(LegendaryProcessGuard.IsBlocked());
        }
        finally
        {
            LegendaryProcessGuard.Reset();
        }
    }

    [Fact]
    public async Task ExecuteLegendaryProcessAsync_WhenTimeout_BlocksFurtherCommands()
    {
        using var sandbox = new LegendaryConfigSandbox();
        var runner = new FakeLegendaryRunner(_ => LegendaryRunResult.Success());
        var manager = CreateManager(runner, sandbox);

        LegendaryProcessGuard.Reset();
        try
        {
            var result = await manager.ExecuteLegendaryProcessAsync(
                "cmd.exe",
                "/c ping -n 30 127.0.0.1 > nul",
                TimeSpan.FromMilliseconds(500),
                null);

            // Proces wystartował, a jego zakończenie jest niepewne - kolejne komendy legendary są blokowane.
            Assert.True(result.TimedOut);
            Assert.True(LegendaryProcessGuard.IsBlocked());
        }
        finally
        {
            LegendaryProcessGuard.Reset();
        }
    }

    [Fact]
    public async Task HandleEpicGameAsync_WhenBlocked_RunsNoLegendaryCommands()
    {
        using var sandbox = new LegendaryConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);

        var runner = new FakeLegendaryRunner(_ => LegendaryRunResult.Success());
        var manager = CreateManager(runner, sandbox);

        LegendaryProcessGuard.Reset();
        try
        {
            LegendaryProcessGuard.MarkUnfinished();

            string? reportedMod = null;
            manager.EpicLaunchError += (modName, _) => reportedMod = modName;

            await manager.HandleEpicGameAsync(CreateModConfig(Path.GetTempPath()));

            // Blokada sprawdzana jest na wejściu - żadna komenda legendary ani operacja na plikach.
            Assert.Empty(runner.Commands);
            Assert.NotNull(reportedMod);
        }
        finally
        {
            LegendaryProcessGuard.Reset();
        }
    }

    [Fact]
    public async Task Reinstallation_WhenManifestDownloadFails_DoesNotRunUninstall()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);

        var runner = new FakeLegendaryRunner(_ => LegendaryRunResult.Success());
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: false);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        bool result = await manager.PerformReinstallationSequence(modConfig);

        // Manifest nieudany => reinstalacja przerwana, pliki gry nietknięte.
        Assert.False(result);
        Assert.DoesNotContain(runner.Commands, c => c.Contains("uninstall"));
    }

    [Fact]
    public async Task Reinstallation_WhenRegisteredPathDiffers_DoesNotRunUninstall()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
        sandbox.WriteInstalledRegistered(AppId, @"C:\Games\ObcaInstalacja");

        var runner = new FakeLegendaryRunner(_ => LegendaryRunResult.Success());
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: true);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        bool result = await manager.PerformReinstallationSequence(modConfig);

        // Preflight: zarejestrowana ścieżka wskazuje na obcą instalację - nie kasujemy.
        Assert.False(result);
        Assert.DoesNotContain(runner.Commands, c => c.Contains("uninstall"));
    }

    [Fact]
    public async Task Reinstallation_WhenRegisteredPathMatches_RunsUninstallWithSkipUninstaller()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
        sandbox.WriteInstalledRegistered(AppId, manifestDirectory.Path);
        manifestDirectory.WriteFile($"{AppId}_{AmongVersion}.manifest", "manifest");

        var runner = new FakeLegendaryRunner(_ => LegendaryRunResult.Success());
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: true);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        await manager.PerformReinstallationSequence(modConfig);

        Assert.Contains(runner.Commands, c => c.Contains("uninstall") && c.Contains("--skip-uninstaller"));
    }

    [Fact]
    public async Task InstallFlow_PreservesInstalledUrlsBeforeUninstall()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);
        File.WriteAllText(Path.Combine(sandbox.Directory, "installed.json"),
            "{\"" + AppId + "\":{\"base_urls\":[\"" + BaseUrl + "\"]}}");
        manifestDirectory.WriteFile($"{AppId}_{AmongVersion}.manifest", "manifest");
        var runner = CreateSimulatedRunner(sandbox, new SimulatedLegendaryState { PathOnInstall = manifestDirectory.Path });
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: true);

        Assert.True(await manager.PerformEpicInstallFlowAsync(CreateModConfig(manifestDirectory.Path)));
        Assert.True(LegendaryBaseUrlState.HasBaseUrlsInMetadata(sandbox.Directory, AppId));
        Assert.Contains(runner.Commands, c => c.Contains("--keep-files"));
    }

    [Fact]
    public async Task InstallFlow_WhenMetadataCannotBePreserved_StopsBeforeUninstall()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, "{broken");
        File.WriteAllText(Path.Combine(sandbox.Directory, "installed.json"),
            "{\"" + AppId + "\":{\"base_urls\":[\"" + BaseUrl + "\"]}}");
        var runner = CreateSimulatedRunner(sandbox, new SimulatedLegendaryState());
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: true);

        Assert.False(await manager.PerformEpicInstallFlowAsync(CreateModConfig(manifestDirectory.Path)));
        Assert.Empty(runner.Commands);
        Assert.Equal("{broken", File.ReadAllText(Path.Combine(sandbox.Directory, "metadata", AppId + ".json")));
    }

    [Fact]
    public async Task InstallFlow_WhenImportLeavesNoEntry_ProceedsWithFallbackInstall()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
        manifestDirectory.WriteFile($"{AppId}_{AmongVersion}.manifest", "manifest");

        // Świeża instalacja: import pustego katalogu nie rejestruje gry, instalacja już rejestruje.
        var state = new SimulatedLegendaryState
        {
            PathOnImport = null,
            PathOnInstall = manifestDirectory.Path
        };

        var runner = CreateSimulatedRunner(sandbox, state);
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: true);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        bool result = await manager.PerformEpicInstallFlowAsync(modConfig);

        // Brak wpisu po imporcie to nie błąd - operacja idzie dalej do instalacji z manifestem.
        Assert.True(result);
        Assert.Contains(runner.Commands, c => c.Contains("--manifest"));
        Assert.Contains(runner.Commands, c => c.StartsWith("launch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InstallFlow_WhenImportStateIsUnknown_AbortsBeforeInstall()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
        manifestDirectory.WriteFile($"{AppId}_{AmongVersion}.manifest", "manifest");

        var state = new SimulatedLegendaryState
        {
            PathOnImport = manifestDirectory.Path,
            CorruptInstalledJsonOnImport = true
        };

        var runner = CreateSimulatedRunner(sandbox, state);
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: true);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        string? reported = null;
        manager.EpicLaunchError += (_, details) => reported = details;

        bool result = await manager.PerformEpicInstallFlowAsync(modConfig);

        // Niepewny stan wpisu (uszkodzony JSON) - operacja przerwana, pliki gry nietknięte.
        Assert.False(result);
        Assert.Contains("nie udało się ustalić stanu instalacji", reported);
        Assert.DoesNotContain(runner.Commands, c => c.StartsWith("install", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InstallFlow_WhenImportRegistersForeignPath_AbortsBeforeInstall()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
        manifestDirectory.WriteFile($"{AppId}_{AmongVersion}.manifest", "manifest");

        var state = new SimulatedLegendaryState
        {
            PathOnImport = Path.Combine(manifestDirectory.Path, "ObcaInstalacja"),
            PathOnInstall = manifestDirectory.Path
        };

        var runner = CreateSimulatedRunner(sandbox, state);
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: true);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        bool? reported = null;
        manager.EpicLaunchError += (_, _) => reported = true;

        bool result = await manager.PerformEpicInstallFlowAsync(modConfig);

        // Obca ścieżka w legendary - nie kasujemy i nie instalujemy.
        Assert.False(result);
        Assert.True(reported);
        Assert.DoesNotContain(runner.Commands, c => c.StartsWith("install", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InstallFlow_WithoutRecoverableBaseUrls_DoesNotRunDestructiveUninstall()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        // Brak adresów CDN w metadanych - po destrukcyjnym uninstall nie da się odtworzyć gry.
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);
        manifestDirectory.WriteFile($"{AppId}_{AmongVersion}.manifest", "manifest");

        var state = new SimulatedLegendaryState
        {
            PathOnImport = manifestDirectory.Path,
            PathOnInstall = manifestDirectory.Path
        };

        var runner = CreateSimulatedRunner(sandbox, state);
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: true);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        bool? reported = null;
        manager.EpicLaunchError += (_, _) => reported = true;

        bool result = await manager.PerformEpicInstallFlowAsync(modConfig);

        // Fail closed: destrukcyjny uninstall (bez --keep-files) nie może się wykonać.
        Assert.False(result);
        Assert.True(reported);
        Assert.DoesNotContain(runner.Commands, c => c.Contains("--skip-uninstaller"));
    }

    [Fact]
    public async Task Reinstallation_WhenEntryMissing_SkipsUninstallAndInstalls()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
        manifestDirectory.WriteFile($"{AppId}_{AmongVersion}.manifest", "manifest");

        string gameDirectory = Path.Combine(manifestDirectory.Path, "AmongUs");
        Directory.CreateDirectory(gameDirectory);
        File.WriteAllText(Path.Combine(gameDirectory, "Among Us.exe"), "exe");

        // Brak wpisu w legendary (świeża instalacja) - nie ma czego kasować.
        var runner = new FakeLegendaryRunner(_ => LegendaryRunResult.Success());
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: true);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        bool result = await manager.PerformReinstallationSequence(modConfig);

        Assert.True(result);
        Assert.DoesNotContain(runner.Commands, c => c.Contains("uninstall"));
        Assert.Contains(runner.Commands, c => c.StartsWith("install", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reinstallation_WhenEntryUnknown_DoesNotRunUninstall()
    {
        using var sandbox = new LegendaryConfigSandbox();
        using var manifestDirectory = new TempDirectory();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
        sandbox.WriteInstalledBroken();

        var runner = new FakeLegendaryRunner(_ => LegendaryRunResult.Success());
        var manager = new ManifestStubEpicVersionManager(runner, sandbox, manifestDirectory.Path, manifestAvailable: true);
        var modConfig = CreateModConfig(manifestDirectory.Path);

        bool result = await manager.PerformReinstallationSequence(modConfig);

        // Niepewny stan wpisu - reinstalacja przerwana, pliki gry nietknięte.
        Assert.False(result);
        Assert.DoesNotContain(runner.Commands, c => c.Contains("uninstall"));
    }

    [Fact]
    public async Task AuthenticateAsync_DoesNotWriteAuthorizationCodeToLogs()
    {
        const string authCode = "abcdef0123456789ABCDEF0123456789";
        using var sandbox = new LegendaryConfigSandbox();

        var output = new RecordingDiagnosticsOutput();
        var interaction = new FakeEpicUserInteraction("{\"authorizationCode\":\"" + authCode + "\"}");
        var runner = new FakeLegendaryRunner(arguments =>
        {
            if (arguments.StartsWith("auth --import", StringComparison.Ordinal))
                return new LegendaryRunResult(1, new[] { "ERROR: No EGS login session found" });

            // legendary może powtórzyć kod w wyjściu procesu - nie wolno go zalogować.
            return new LegendaryRunResult(0, new[] { "authorization code: " + authCode });
        });

        var manager = new EpicVersionManager(output, interaction, runner.RunAsync, sandbox.Directory);

        LegendaryProcessGuard.Reset();
        try
        {
            bool result = await manager.AuthenticateAsync();

            Assert.True(result);
            Assert.Contains(runner.Commands, c => c.StartsWith("auth --code", StringComparison.Ordinal));
            Assert.DoesNotContain(output.Lines, line => line.Contains(authCode, StringComparison.Ordinal));
        }
        finally
        {
            LegendaryProcessGuard.Reset();
        }
    }

    private static EpicVersionManager CreateManager(
        FakeLegendaryRunner runner,
        LegendaryConfigSandbox sandbox,
        string? manifestDirectory = null)
        => new(
            new NullDiagnosticsOutput(),
            new NullEpicUserInteraction(),
            runner.RunAsync,
            sandbox.Directory,
            manifestDirectory);

    private static ModConfiguration CreateModConfig(string installPath)
        => new()
        {
            Id = 42,
            ModName = "Test Mod",
            AmongVersion = AmongVersion,
            InstallPath = installPath
        };

    private static bool IsWarmup(string arguments)
        => arguments.StartsWith("import", StringComparison.Ordinal);

    /// <summary>Import katalogu gry (bez --skip-dlcs) - odróżniamy go od importu pomocniczego.</summary>
    private static bool IsGameImport(string arguments)
        => arguments.StartsWith("import", StringComparison.Ordinal)
            && !arguments.Contains("--skip-dlcs", StringComparison.Ordinal);

    /// <summary>
    /// Stan symulowanego legendary - testy sterują tym, co legendary zapisuje w installed.json.
    /// </summary>
    private sealed class SimulatedLegendaryState
    {
        /// <summary>Ścieżka rejestrowana po imporcie katalogu gry (null = import nic nie rejestruje).</summary>
        public string? PathOnImport { get; init; }

        /// <summary>Ścieżka rejestrowana po instalacji (null = instalacja nic nie rejestruje).</summary>
        public string? PathOnInstall { get; init; }

        /// <summary>Import psuje installed.json - stan wpisu jest wtedy niepewny.</summary>
        public bool CorruptInstalledJsonOnImport { get; init; }
    }

    /// <summary>
    /// Symuluje legendary: uninstall usuwa wpis z installed.json, import i install go rejestrują.
    /// </summary>
    private static FakeLegendaryRunner CreateSimulatedRunner(
        LegendaryConfigSandbox sandbox,
        SimulatedLegendaryState state)
    {
        return new FakeLegendaryRunner(arguments =>
        {
            if (arguments.StartsWith("uninstall", StringComparison.Ordinal))
            {
                sandbox.RemoveInstalledEntry(AppId);
                return LegendaryRunResult.Success();
            }

            if (arguments.StartsWith("install", StringComparison.Ordinal))
            {
                if (state.PathOnInstall != null)
                    sandbox.WriteInstalledRegistered(AppId, state.PathOnInstall);

                return LegendaryRunResult.Success();
            }

            if (IsGameImport(arguments))
            {
                if (state.CorruptInstalledJsonOnImport)
                {
                    sandbox.WriteInstalledBroken();
                    return LegendaryRunResult.Success();
                }

                if (state.PathOnImport != null)
                    sandbox.WriteInstalledRegistered(AppId, state.PathOnImport);
            }

            return LegendaryRunResult.Success();
        });
    }

    private static string ExtractQuotedPath(string arguments)
    {
        int start = arguments.IndexOf('"');
        int end = arguments.IndexOf('"', start + 1);
        return arguments.Substring(start + 1, end - start - 1);
    }

    private const string InstallArguments = "install 963137e4c29d4c79a81323b8fab03a40 -y --manifest \"mod.manifest\"";

    private const string MetadataWithBaseUrls =
        """{"app_name":"963137e4c29d4c79a81323b8fab03a40","base_urls":["https://cdn.example.com/Builds/Org/ns/build/default"]}""";

    private const string MetadataWithoutBaseUrls =
        """{"app_name":"963137e4c29d4c79a81323b8fab03a40"}""";

    private sealed class FakeLegendaryRunner
    {
        private readonly Func<string, LegendaryRunResult> _handler;

        public FakeLegendaryRunner(Func<string, LegendaryRunResult> handler)
        {
            _handler = handler;
        }

        public List<string> Commands { get; } = new();

        public Task<LegendaryRunResult> RunAsync(
            string arguments,
            TimeSpan? timeout,
            Action<string, bool>? onOutput)
        {
            Commands.Add(arguments);
            return Task.FromResult(_handler(arguments));
        }
    }

    private sealed class NullDiagnosticsOutput : IDiagnosticsOutput
    {
        public void Write(string line) { }
    }

    private sealed class RecordingDiagnosticsOutput : IDiagnosticsOutput
    {
        public List<string> Lines { get; } = new();

        public void Write(string line) => Lines.Add(line);
    }

    private sealed class NullEpicUserInteraction : IEpicUserInteraction
    {
        public bool Confirm(string message) => false;

        public void ShowError(string message) { }

        public string? Prompt(string message, string title = "") => null;

        public Task<string?> ShowEpicAuthDialogAsync(string browserUrl) => Task.FromResult<string?>(null);
    }

    private sealed class FakeEpicUserInteraction : IEpicUserInteraction
    {
        private readonly string? _authInput;

        public FakeEpicUserInteraction(string? authInput)
        {
            _authInput = authInput;
        }

        public bool Confirm(string message) => false;

        public void ShowError(string message) { }

        public string? Prompt(string message, string title = "") => null;

        public Task<string?> ShowEpicAuthDialogAsync(string browserUrl) => Task.FromResult(_authInput);
    }

    /// <summary>
    /// Manager z podstawionym wynikiem pobierania manifestu - testy nie uderzają w sieć.
    /// </summary>
    private sealed class ManifestStubEpicVersionManager : EpicVersionManager
    {
        private readonly bool _manifestAvailable;

        public ManifestStubEpicVersionManager(
            FakeLegendaryRunner runner,
            LegendaryConfigSandbox sandbox,
            string manifestDirectory,
            bool manifestAvailable)
            : base(
                new NullDiagnosticsOutput(),
                new NullEpicUserInteraction(),
                runner.RunAsync,
                sandbox.Directory,
                manifestDirectory)
        {
            _manifestAvailable = manifestAvailable;
        }

        protected internal override Task<bool> DownloadManifestAsync(string amongVersionFormatted)
            => Task.FromResult(_manifestAvailable);
    }

    private sealed class LegendaryConfigSandbox : IDisposable
    {
        private readonly Dictionary<string, string> _installedEntries = new(StringComparer.Ordinal);

        public LegendaryConfigSandbox()
        {
            Directory = Path.Combine(Path.GetTempPath(), "susmodder-legendary-flow-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, "metadata"));
        }

        public string Directory { get; }

        public void WriteMetadata(string appId, string json)
            => File.WriteAllText(Path.Combine(Directory, "metadata", appId + ".json"), json);

        public void WriteInstalledRegistered(string appId, string installPath = @"C:\Games\AmongUs")
        {
            _installedEntries[appId] =
                "{\"install_path\":\"" + installPath.Replace("\\", "\\\\") + "\",\"title\":\"Among Us\"}";
            FlushInstalled();
        }

        /// <summary>Usuwa wpis aplikacji - symulacja uninstall.</summary>
        public void RemoveInstalledEntry(string appId)
        {
            _installedEntries.Remove(appId);
            FlushInstalled();
        }

        /// <summary>Uszkodzony JSON w installed.json - stan wpisu jest wtedy niepewny.</summary>
        public void WriteInstalledBroken()
        {
            _installedEntries.Clear();
            File.WriteAllText(Path.Combine(Directory, "installed.json"), "{ not a json ]");
        }

        private void FlushInstalled()
        {
            string entries = string.Join(",", _installedEntries.Select(e => "\"" + e.Key + "\":" + e.Value));
            File.WriteAllText(Path.Combine(Directory, "installed.json"), "{" + entries + "}");
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "susmodder-legendary-manifest-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void WriteFile(string name, string content)
            => File.WriteAllText(System.IO.Path.Combine(Path, name), content);

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Path))
                    System.IO.Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
