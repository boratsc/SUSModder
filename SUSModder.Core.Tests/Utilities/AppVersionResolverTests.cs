using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class AppVersionResolverTests : IDisposable
{
    private readonly string _tempDir;

    public AppVersionResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Resolve_WhenVersionJsonContainsBetaVersion_ReturnsBetaVersion()
    {
        WriteVersionFile(_tempDir, "3.0.2-beta");

        var version = AppVersionResolver.Resolve(candidateDirectories: new[] { _tempDir });

        Assert.Equal("3.0.2-beta", version);
    }

    [Fact]
    public void Resolve_WhenVersionJsonIsInCurrentDirectory_ReturnsBetaVersion()
    {
        var currentDir = Path.Combine(_tempDir, "current");
        Directory.CreateDirectory(currentDir);
        WriteVersionFile(currentDir, "3.0.3-beta");

        var version = AppVersionResolver.Resolve(candidateDirectories: new[] { _tempDir, currentDir });

        Assert.Equal("3.0.3-beta", version);
    }

    [Fact]
    public void Resolve_WhenOnlyDefaultAssemblyVersionExists_DoesNotReturnCoreAssemblyVersionOneZeroZero()
    {
        var assembly = BuildAssemblyWithVersion("1.0.0");

        var version = AppVersionResolver.Resolve(
            candidateDirectories: new[] { _tempDir },
            entryAssembly: assembly);

        Assert.Equal(AppVersionResolver.UnknownVersion, version);
        Assert.NotEqual("1.0.0", version);
    }

    [Fact]
    public void Resolve_WhenEntryAssemblyHasInformationalVersion_ReturnsItAsFallback()
    {
        var assembly = BuildAssemblyWithVersion("4.5.6-beta");

        var version = AppVersionResolver.Resolve(
            candidateDirectories: new[] { _tempDir },
            entryAssembly: assembly);

        Assert.Equal("4.5.6-beta", version);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures on Windows test hosts.
        }
    }

    private static void WriteVersionFile(string directory, string version)
    {
        var payload = JsonSerializer.Serialize(new AppVersion { CurrentVersion = version });
        File.WriteAllText(Path.Combine(directory, "version.json"), payload);
    }

    private static Assembly BuildAssemblyWithVersion(string informationalVersion)
    {
        var assemblyName = new AssemblyName($"SUSModderTestAssembly_{Guid.NewGuid():N}")
        {
            Version = new Version(1, 0, 0)
        };

        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var attributeConstructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor(new[] { typeof(string) })!;
        var attributeBuilder = new CustomAttributeBuilder(attributeConstructor, new object[] { informationalVersion });
        assemblyBuilder.SetCustomAttribute(attributeBuilder);
        return assemblyBuilder;
    }
}
