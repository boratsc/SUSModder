using System;
using System.IO;
using System.Text.Json;
using SUSModder.Core.GameIntegration;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

public class LegendaryBaseUrlStateTests
{
    private const string AppId = "963137e4c29d4c79a81323b8fab03a40";
    private const string BaseUrl = "https://cdn.example.com/Builds/Org/o-example/buildhash/default";
    private const string MetadataWithoutBaseUrls = """{"app_name":"963137e4c29d4c79a81323b8fab03a40"}""";
    private const string MetadataWithBaseUrls =
        """{"app_name":"963137e4c29d4c79a81323b8fab03a40","base_urls":["https://cdn.example.com/Builds/Org/o-example/buildhash/default"]}""";

    [Fact]
    public void PreserveInstalledBaseUrls_PreservesMetadataAndInstalledEntry()
    {
        using var sandbox = new ConfigSandbox();
        string original = $$$"""{"app_name":"{{{AppId}}}","app_title":"Among Us","metadata":{"nested":[1,2]},"asset_infos":{"Windows":{}},"base_urls":[]}""";
        string installed = $$$"""{"{{{AppId}}}":{"base_urls":["{{{BaseUrl}}}"],"install_path":"game"},"other":{"value":1}}""";
        sandbox.WriteMetadata(AppId, original);
        sandbox.WriteInstalled(installed);

        Assert.True(LegendaryBaseUrlState.PreserveInstalledBaseUrls(sandbox.Directory, AppId));
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(sandbox.Directory, "metadata", AppId + ".json")));
        Assert.Equal(BaseUrl, metadata.RootElement.GetProperty("base_urls")[0].GetString());
        Assert.Equal(2, metadata.RootElement.GetProperty("metadata").GetProperty("nested")[1].GetInt32());
        Assert.Equal("Among Us", metadata.RootElement.GetProperty("app_title").GetString());
        Assert.Equal(installed, File.ReadAllText(Path.Combine(sandbox.Directory, "installed.json")));
    }

    [Theory]
    [InlineData("{broken", "[\"https://cdn.example.com\"]")]
    [InlineData("{\"app_name\":\"other\"}", "[\"https://cdn.example.com\"]")]
    [InlineData("{\"app_name\":\"963137e4c29d4c79a81323b8fab03a40\"}", "[null]")]
    [InlineData("{\"app_name\":\"963137e4c29d4c79a81323b8fab03a40\",\"base_urls\":\"bad\"}", "[\"https://cdn.example.com\"]")]
    public void PreserveInstalledBaseUrls_UncertainInputDoesNotChangeMetadata(string metadata, string urls)
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, metadata);
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"base_urls\":" + urls + "}}");

        Assert.False(LegendaryBaseUrlState.PreserveInstalledBaseUrls(sandbox.Directory, AppId));
        Assert.Equal(metadata, File.ReadAllText(Path.Combine(sandbox.Directory, "metadata", AppId + ".json")));
    }

    [Fact]
    public void ResolveConfigDirectory_PrefersLegendaryConfigPath()
    {
        var directory = LegendaryBaseUrlState.ResolveConfigDirectory(
            @"C:\legendary-config", @"C:\xdg", @"C:\Users\tester");

        Assert.Equal(@"C:\legendary-config", directory);
    }

    [Fact]
    public void ResolveConfigDirectory_UsesXdgConfigHome()
    {
        var directory = LegendaryBaseUrlState.ResolveConfigDirectory(
            null, @"C:\xdg", @"C:\Users\tester");

        Assert.Equal(Path.Combine(@"C:\xdg", "legendary"), directory);
    }

    [Fact]
    public void ResolveConfigDirectory_FallsBackToUserProfile()
    {
        var directory = LegendaryBaseUrlState.ResolveConfigDirectory(
            "  ", null, @"C:\Users\tester");

        Assert.Equal(Path.Combine(@"C:\Users\tester", ".config", "legendary"), directory);
    }

    [Fact]
    public void HasBaseUrls_ReturnsTrue_WhenMetadataContainsBaseUrls()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, $$"""{"app_name":"{{AppId}}","base_urls":["{{BaseUrl}}"]}""");

        Assert.True(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsFalse_WhenMetadataBaseUrlsAreEmpty()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, $$"""{"app_name":"{{AppId}}","base_urls":[]}""");

        Assert.False(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsFalse_WhenMetadataHasNoBaseUrlsProperty()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, $$"""{"app_name":"{{AppId}}"}""");

        Assert.False(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsTrue_WhenInstalledJsonContainsBaseUrls()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, $$"""{"app_name":"{{AppId}}","base_urls":[]}""");
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"base_urls\":[\"" + BaseUrl + "\"]}}");

        Assert.True(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsFalse_WhenInstalledJsonBelongsToOtherApp()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("""{"other-app":{"base_urls":["https://cdn.example.com"]}}""");

        Assert.False(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsFalse_WhenConfigDirectoryDoesNotExist()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.False(LegendaryBaseUrlState.HasBaseUrls(missingDirectory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsFalse_WhenMetadataIsMalformed()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, "{ not a json ]");

        Assert.False(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void IsRegistered_ReturnsFalse_WhenInstalledFileIsMissing()
    {
        using var sandbox = new ConfigSandbox();

        Assert.False(LegendaryBaseUrlState.IsRegistered(sandbox.Directory, AppId));
    }

    [Fact]
    public void IsRegistered_ReturnsTrue_WhenAppEntryExists()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"install_path\":\"C:\\\\Games\\\\AmongUs\"}}");

        Assert.True(LegendaryBaseUrlState.IsRegistered(sandbox.Directory, AppId));
    }

    [Fact]
    public void IsRegistered_ReturnsFalse_WhenOnlyOtherAppRegistered()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("""{"other-app":{"install_path":"C:\\Games\\Other"}}""");

        Assert.False(LegendaryBaseUrlState.IsRegistered(sandbox.Directory, AppId));
    }

    [Fact]
    public void IsRegistered_ReturnsTrue_WhenJsonIsMalformed()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("{ not a json ]");

        // Fail-closed: niepewny stan traktujemy jak rejestrację.
        Assert.True(LegendaryBaseUrlState.IsRegistered(sandbox.Directory, AppId));
    }

    [Fact]
    public void IsRegistered_ReturnsTrue_WhenFileCannotBeRead()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"install_path\":\"C:\\\\Games\\\\AmongUs\"}}");

        // Blokada pliku = brak możliwości odczytu = stan niepewny (fail-closed).
        using var exclusiveLock = new FileStream(
            Path.Combine(sandbox.Directory, "installed.json"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        Assert.True(LegendaryBaseUrlState.IsRegistered(sandbox.Directory, AppId));
    }

    [Fact]
    public void IsRegistered_ReturnsTrue_WhenConfigDirectoryIsUnknown()
    {
        Assert.True(LegendaryBaseUrlState.IsRegistered(string.Empty, AppId));
    }

    [Fact]
    public void IsRegistered_ReturnsTrue_WhenAppEntryHasUnexpectedType()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("{\"" + AppId + "\":\"cokolwiek\"}");

        // Fail-closed: wpis istnieje, jego typ jest nieoczekiwany - stan traktujemy jako rejestrację.
        Assert.True(LegendaryBaseUrlState.IsRegistered(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsOnlyInInstalledJson_ReturnsTrue_WhenUrlsOnlyInInstalledJson()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"base_urls\":[\"" + BaseUrl + "\"]}}");

        // Uninstall usunie ten wpis - guard musi przerwać przed usunięciem.
        Assert.True(LegendaryBaseUrlState.HasBaseUrlsOnlyInInstalledJson(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsOnlyInInstalledJson_ReturnsFalse_WhenMetadataAlsoHasUrls()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"base_urls\":[\"" + BaseUrl + "\"]}}");

        // Metadane zawierają kopię adresów - uninstall nie zabierze jedynego źródła.
        Assert.False(LegendaryBaseUrlState.HasBaseUrlsOnlyInInstalledJson(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsOnlyInInstalledJson_ReturnsFalse_WhenNoUrlsAnywhere()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);

        Assert.False(LegendaryBaseUrlState.HasBaseUrlsOnlyInInstalledJson(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsOnlyInInstalledJson_ReturnsTrue_WhenMetadataCannotBeRead()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, "{ not a json ]");
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"base_urls\":[\"" + BaseUrl + "\"]}}");

        // Metadane nieczytelne = stan nieznany - blokujemy przed uninstall (fail-closed).
        Assert.True(LegendaryBaseUrlState.HasBaseUrlsOnlyInInstalledJson(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsOnlyInInstalledJson_ReturnsTrue_WhenConfigDirectoryIsUnknown()
    {
        Assert.True(LegendaryBaseUrlState.HasBaseUrlsOnlyInInstalledJson(string.Empty, AppId));
    }

    [Fact]
    public void HasBaseUrlsOnlyInInstalledJson_ReturnsFalse_WhenFilesDoNotExist()
    {
        using var sandbox = new ConfigSandbox();

        // Brak plików = brak adresów = przebieg bez zmian.
        Assert.False(LegendaryBaseUrlState.HasBaseUrlsOnlyInInstalledJson(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsOnlyInInstalledJson_ReturnsTrue_WhenMetadataFileIsLocked()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"base_urls\":[\"" + BaseUrl + "\"]}}");

        // Blokada pliku = brak możliwości odczytu = stan niepewny (fail-closed).
        using var exclusiveLock = new FileStream(
            Path.Combine(sandbox.Directory, "metadata", AppId + ".json"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        Assert.True(LegendaryBaseUrlState.HasBaseUrlsOnlyInInstalledJson(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsOnlyInInstalledJson_ReturnsTrue_WhenInstalledFileIsLocked()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"base_urls\":[\"" + BaseUrl + "\"]}}");

        using var exclusiveLock = new FileStream(
            Path.Combine(sandbox.Directory, "installed.json"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        // Nie da się odczytać installed.json - stan niepewny (fail-closed).
        Assert.True(LegendaryBaseUrlState.HasBaseUrlsOnlyInInstalledJson(sandbox.Directory, AppId));
    }

    [Fact]
    public void GetRegisteredInstallPath_ReturnsFound_WithRegisteredPath()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"install_path\":\"C:\\\\Games\\\\AmongUs\"}}");

        var state = LegendaryBaseUrlState.GetRegisteredInstallPath(sandbox.Directory, AppId, out string? path);

        Assert.Equal(LegendaryInstallPathState.Found, state);
        Assert.Equal(@"C:\Games\AmongUs", path);
    }

    [Fact]
    public void GetRegisteredInstallPath_ReturnsMissing_WhenInstalledFileDoesNotExist()
    {
        using var sandbox = new ConfigSandbox();

        var state = LegendaryBaseUrlState.GetRegisteredInstallPath(sandbox.Directory, AppId, out string? path);

        Assert.Equal(LegendaryInstallPathState.Missing, state);
        Assert.Null(path);
    }

    [Fact]
    public void GetRegisteredInstallPath_ReturnsMissing_WhenOnlyOtherAppRegistered()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("""{"other-app":{"install_path":"C:\\Games\\Other"}}""");

        var state = LegendaryBaseUrlState.GetRegisteredInstallPath(sandbox.Directory, AppId, out string? path);

        Assert.Equal(LegendaryInstallPathState.Missing, state);
        Assert.Null(path);
    }

    [Fact]
    public void GetRegisteredInstallPath_ReturnsUnknown_WhenJsonIsMalformed()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("{ not a json ]");

        // Fail-closed: stan niepewny = nie wolno kasować.
        var state = LegendaryBaseUrlState.GetRegisteredInstallPath(sandbox.Directory, AppId, out string? path);

        Assert.Equal(LegendaryInstallPathState.Unknown, state);
        Assert.Null(path);
    }

    [Fact]
    public void GetRegisteredInstallPath_ReturnsUnknown_WhenEntryHasNoInstallPath()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"title\":\"Among Us\"}}");

        var state = LegendaryBaseUrlState.GetRegisteredInstallPath(sandbox.Directory, AppId, out string? path);

        Assert.Equal(LegendaryInstallPathState.Unknown, state);
        Assert.Null(path);
    }

    [Fact]
    public void GetRegisteredInstallPath_ReturnsUnknown_WhenConfigDirectoryIsUnknown()
    {
        var state = LegendaryBaseUrlState.GetRegisteredInstallPath(string.Empty, AppId, out string? path);

        Assert.Equal(LegendaryInstallPathState.Unknown, state);
        Assert.Null(path);
    }

    [Fact]
    public void GetRegisteredInstallPath_ReturnsUnknown_WhenFileCannotBeRead()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"install_path\":\"C:\\\\Games\\\\AmongUs\"}}");

        // Blokada pliku = brak możliwości odczytu = stan niepewny (fail-closed).
        using var exclusiveLock = new FileStream(
            Path.Combine(sandbox.Directory, "installed.json"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var state = LegendaryBaseUrlState.GetRegisteredInstallPath(sandbox.Directory, AppId, out string? path);

        Assert.Equal(LegendaryInstallPathState.Unknown, state);
        Assert.Null(path);
    }

    [Fact]
    public void HasBaseUrlsInMetadata_ReturnsTrue_WhenMetadataContainsBaseUrls()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);

        // Tylko metadane przetrwają destrukcyjny uninstall.
        Assert.True(LegendaryBaseUrlState.HasBaseUrlsInMetadata(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsInMetadata_ReturnsFalse_WhenOnlyInstalledJsonHasUrls()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithoutBaseUrls);
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"base_urls\":[\"" + BaseUrl + "\"]}}");

        // Uninstall usunie ten wpis - adresów nie da się odtworzyć.
        Assert.False(LegendaryBaseUrlState.HasBaseUrlsInMetadata(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsInMetadata_ReturnsFalse_WhenMetadataIsMalformed()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, "{ not a json ]");

        // Fail-closed: adresów nie da się potwierdzić.
        Assert.False(LegendaryBaseUrlState.HasBaseUrlsInMetadata(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsInMetadata_ReturnsFalse_WhenMetadataFileIsLocked()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, MetadataWithBaseUrls);

        using var exclusiveLock = new FileStream(
            Path.Combine(sandbox.Directory, "metadata", AppId + ".json"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        // Blokada pliku = brak możliwości odczytu = stan niepewny (fail-closed).
        Assert.False(LegendaryBaseUrlState.HasBaseUrlsInMetadata(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrlsInMetadata_ReturnsFalse_WhenConfigDirectoryIsUnknown()
    {
        Assert.False(LegendaryBaseUrlState.HasBaseUrlsInMetadata(string.Empty, AppId));
    }

    private sealed class ConfigSandbox : IDisposable
    {
        public ConfigSandbox()
        {
            Directory = Path.Combine(Path.GetTempPath(), "susmodder-legendary-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, "metadata"));
        }

        public string Directory { get; }

        public void WriteMetadata(string appId, string json)
            => File.WriteAllText(Path.Combine(Directory, "metadata", appId + ".json"), json);

        public void WriteInstalled(string json)
            => File.WriteAllText(Path.Combine(Directory, "installed.json"), json);

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
}
