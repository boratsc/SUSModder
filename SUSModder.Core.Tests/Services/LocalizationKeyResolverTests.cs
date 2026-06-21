using System.Collections.Generic;
using System.Text.Json;
using SUSModder.Core.Services.Localization;
using Xunit;

namespace SUSModder.Core.Tests.Services;

public class LocalizationKeyResolverTests
{
    [Fact]
    public void Resolve_NestedObjectPath_ReturnsValue()
    {
        // Arrange - drzewo jak w UI: UI.Buttons.Install
        var tree = new Dictionary<string, object>
        {
            ["UI"] = new Dictionary<string, object>
            {
                ["Buttons"] = new Dictionary<string, object>
                {
                    ["Install"] = "Instaluj"
                }
            }
        };

        // Act
        var result = LocalizationKeyResolver.Resolve(tree, "UI.Buttons.Install");

        // Assert
        Assert.Equal("Instaluj", result);
    }

    [Fact]
    public void Resolve_FlatKeyWithDotsInName_ReturnsValue()
    {
        // Arrange - drzewo jak w LaunchDiagnostics: "Severity.Info" jest płaskim kluczem
        var tree = new Dictionary<string, object>
        {
            ["LaunchDiagnostics"] = new Dictionary<string, object>
            {
                ["Severity.Critical"] = "Krytyczny",
                ["Severity.Warning"] = "Ostrzeżenie",
                ["Severity.Info"] = "Informacja"
            }
        };

        // Act
        var result = LocalizationKeyResolver.Resolve(tree, "LaunchDiagnostics.Severity.Info");

        // Assert
        Assert.Equal("Informacja", result);
    }

    [Fact]
    public void Resolve_MultipleSegmentsInFlatKey_ReturnsValue()
    {
        // Arrange - klucz "Summary.BepInExMissing" wewnątrz LaunchDiagnostics
        var tree = new Dictionary<string, object>
        {
            ["LaunchDiagnostics"] = new Dictionary<string, object>
            {
                ["Summary.BepInExMissing"] = "Nie znaleziono logów BepInEx",
                ["Actions.Close"] = "Zamknij"
            }
        };

        // Act
        var result = LocalizationKeyResolver.Resolve(tree, "LaunchDiagnostics.Summary.BepInExMissing");
        var close = LocalizationKeyResolver.Resolve(tree, "LaunchDiagnostics.Actions.Close");

        // Assert
        Assert.Equal("Nie znaleziono logów BepInEx", result);
        Assert.Equal("Zamknij", close);
    }

    [Fact]
    public void Resolve_MissingKey_ReturnsNull()
    {
        // Arrange
        var tree = new Dictionary<string, object>
        {
            ["UI"] = new Dictionary<string, object>
            {
                ["Buttons"] = new Dictionary<string, object>
                {
                    ["Install"] = "Instaluj"
                }
            }
        };

        // Act
        var result = LocalizationKeyResolver.Resolve(tree, "NonExistent.Path");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_TopLevelKeyMissingPrefix_ReturnsNull()
    {
        // Arrange - "Diagnostic" nie istnieje, ale "DiagnosticX" istnieje
        var tree = new Dictionary<string, object>
        {
            ["DiagnosticX"] = "foo"
        };

        // Act
        var result = LocalizationKeyResolver.Resolve(tree, "Diagnostic.SubKey");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_NullOrEmptyKey_ReturnsNull()
    {
        var tree = new Dictionary<string, object> { ["X"] = "y" };
        Assert.Null(LocalizationKeyResolver.Resolve(tree, ""));
        Assert.Null(LocalizationKeyResolver.Resolve(tree, "   "));
        Assert.Null(LocalizationKeyResolver.Resolve(null, "X"));
    }

    [Fact]
    public void Resolve_JsonElementRoot_NavigatesCorrectly()
    {
        // Arrange - ten sam kształt, ale z deserializacji System.Text.Json
        // (zagnieżdżone obiekty są JsonElement, nie Dictionary)
        var json = "{\"UI\":{\"Buttons\":{\"Install\":\"Install\"}},\"LaunchDiagnostics\":{\"Severity.Info\":\"Info\"}}";
        using var doc = JsonDocument.Parse(json);
        var tree = doc.RootElement.Clone();

        // Act
        var install = LocalizationKeyResolver.Resolve(tree, "UI.Buttons.Install");
        var info = LocalizationKeyResolver.Resolve(tree, "LaunchDiagnostics.Severity.Info");

        // Assert
        Assert.Equal("Install", install);
        Assert.Equal("Info", info);
    }
}
