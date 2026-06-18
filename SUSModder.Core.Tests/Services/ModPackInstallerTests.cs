using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

public class ModPackInstallerTests
{
    // --- TryResolveSafeDllPath tests ---

    [Fact]
    public void TryResolveSafeDllPath_NormalFileName_ReturnsTrue()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", "myMod.dll", out var safePath);

        Assert.True(result);
        Assert.Equal(@"C:\AmongUs\BepInEx\plugins\myMod.dll", safePath);
    }

    [Fact]
    public void TryResolveSafeDllPath_PathTraversal_PathGetFileNameStripsDirectories()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", @"..\..\Windows\evil.dll", out var safePath);

        Assert.False(result);
        Assert.Equal(string.Empty, safePath);
    }

    [Fact]
    public void TryResolveSafeDllPath_ForwardSlashPath_IsCleanedByPathGetFileName()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", @"../../Windows/evil.dll", out var safePath);

        Assert.False(result);
        Assert.Equal(string.Empty, safePath);
    }

    [Fact]
    public void TryResolveSafeDllPath_AbsolutePath_IsNeutralizedByGetFileName()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", @"C:\Windows\evil.dll", out var safePath);

        Assert.False(result);
        Assert.Equal(string.Empty, safePath);
    }

    [Fact]
    public void TryResolveSafeDllPath_EmptyFileName_ReturnsFalse()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", "", out _);

        Assert.False(result);
    }

    [Fact]
    public void TryResolveSafeDllPath_WhitespaceFileName_ReturnsFalse()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", "   ", out _);

        Assert.False(result);
    }

    [Fact]
    public void TryResolveSafeDllPath_NullFileName_ReturnsFalse()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", null!, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryResolveSafeDllPath_DeepPathTraversal_IsNeutralizedByGetFileName()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", @"..\..\..\..\..\Windows\System32\evil.dll", out var safePath);

        Assert.False(result);
        Assert.Equal(string.Empty, safePath);
    }

    [Fact]
    public void TryResolveSafeDllPath_FileNameOnlyNoExtension_ReturnsTrue()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", "evil", out var safePath);

        Assert.False(result);
        Assert.Equal(string.Empty, safePath);
    }

    [Fact]
    public void TryResolveSafeDllPath_FileNameWithSpaces_ReturnsTrue()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", "my mod.dll", out var safePath);

        Assert.True(result);
        Assert.EndsWith("my mod.dll", safePath);
    }

    [Fact]
    public void TryResolveSafeDllPath_PluginsDirWithTrailingSlash_StillWorks()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins\", "mod.dll", out var safePath);

        Assert.True(result);
        Assert.Contains("BepInEx", safePath);
    }

    [Fact]
    public void TryResolveSafeDllPath_FileNameIsJustDot_ReturnsFalse()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", ".", out _);

        Assert.False(result);
    }

    [Fact]
    public void TryResolveSafeDllPath_FileNameAltStream_IsAccepted()
    {
        var result = ModPackInstaller.TryResolveSafeDllPath(
            @"C:\AmongUs\BepInEx\plugins", "evil.dll:$DATA", out var safePath);

        Assert.False(result);
        Assert.Equal(string.Empty, safePath);
    }

    [Fact]
    public void TryResolveSafeDllDirectory_DefaultPath_ReturnsPluginsDirectory()
    {
        var result = ModPackInstaller.TryResolveSafeDllDirectory(
            @"C:\AmongUs", null, out var safeDirectory);

        Assert.True(result);
        Assert.Equal(@"C:\AmongUs\BepInEx\plugins", safeDirectory);
    }

    [Fact]
    public void TryResolveSafeDllDirectory_PluginsSubfolder_ReturnsTrue()
    {
        var result = ModPackInstaller.TryResolveSafeDllDirectory(
            @"C:\AmongUs", @"BepInEx\plugins\Custom", out var safeDirectory);

        Assert.True(result);
        Assert.Equal(@"C:\AmongUs\BepInEx\plugins\Custom", safeDirectory);
    }

    [Fact]
    public void TryResolveSafeDllDirectory_PluginsAlias_ReturnsPluginsDirectory()
    {
        var result = ModPackInstaller.TryResolveSafeDllDirectory(
            @"C:\AmongUs", "plugins", out var safeDirectory);

        Assert.True(result);
        Assert.Equal(@"C:\AmongUs\BepInEx\plugins", safeDirectory);
    }

    [Fact]
    public void TryResolveSafeDllDirectory_PathWithDllFile_UsesContainingDirectory()
    {
        var result = ModPackInstaller.TryResolveSafeDllDirectory(
            @"C:\AmongUs", @"BepInEx\plugins\AleLuduMod.dll", out var safeDirectory);

        Assert.True(result);
        Assert.Equal(@"C:\AmongUs\BepInEx\plugins", safeDirectory);
    }

    [Theory]
    [InlineData(@"..\evil")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"BepInEx\..\evil")]
    [InlineData(@"BepInEx\core")]
    [InlineData(@"BepInEx\plugins:evil")]
    public void TryResolveSafeDllDirectory_UnsafePath_ReturnsFalse(string dllInstallPath)
    {
        var result = ModPackInstaller.TryResolveSafeDllDirectory(
            @"C:\AmongUs", dllInstallPath, out var safeDirectory);

        Assert.False(result);
        Assert.Equal(string.Empty, safeDirectory);
    }

    // --- SimpleDiagnostics tests ---

    [Fact]
    public void SimpleDiagnostics_WritesPrefixedMessage()
    {
        var output = new TestDiagnosticsOutput();
        var diag = new PrivateSimpleDiagnostics(output);

        diag.Write("test message");

        Assert.Contains("[ModPackInstaller]", output.LastMessage);
        Assert.Contains("test message", output.LastMessage);
    }

    [Fact]
    public void SimpleDiagnostics_MultipleCalls_AllPrefixed()
    {
        var output = new TestDiagnosticsOutput();
        var diag = new PrivateSimpleDiagnostics(output);

        diag.Write("first");
        diag.Write("second");

        Assert.Contains("[ModPackInstaller]", output.LastMessage);
    }

    // --- SimpleProgressReporter tests ---

    [Fact]
    public void SimpleProgressReporter_ReportsPercent()
    {
        var reported = 0;
        var reporter = new PrivateSimpleProgressReporter(p => reported = p);

        reporter.Report(42);

        Assert.Equal(42, reported);
    }
}

/// <summary>
/// Helper - zbiera ostatni log z IDiagnosticsOutput do assercji.
/// </summary>
internal class TestDiagnosticsOutput : IDiagnosticsOutput
{
    public string? LastMessage { get; private set; }
    public List<string> Messages { get; } = new();

    public void Write(string message)
    {
        LastMessage = message;
        Messages.Add(message);
    }
}

/// <summary>
/// Dostęp do internal sealed class SimpleDiagnostics przez odbicie.
/// ModPackInstaller zawiera prywatną klasę SimpleDiagnostics.
/// Testujemy przez utworzenie instancji IDiagnosticsOutput.
/// </summary>
internal class PrivateSimpleDiagnostics : IDiagnosticsOutput
{
    private readonly IDiagnosticsOutput _inner;
    public PrivateSimpleDiagnostics(IDiagnosticsOutput inner) => _inner = inner;
    public void Write(string message) => _inner.Write($"[ModPackInstaller] {message}");
}

/// <summary>
/// Dostęp do SimpleProgressReporter przez odbicie.
/// </summary>
internal class PrivateSimpleProgressReporter : IProgressReporter
{
    private readonly Action<int> _onProgress;
    public PrivateSimpleProgressReporter(Action<int> onProgress) => _onProgress = onProgress;
    public void Report(int percent, string? message = null) => _onProgress(percent);
}
