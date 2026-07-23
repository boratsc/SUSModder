using System.Text.Json;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public class ModPackCreateRequestSerializerTests
{
    [Fact]
    public void ToJson_ExcludesExternalDlls_EvenWhenPresentOnRequest()
    {
        var request = new ModPackCreateRequest
        {
            CreatorHash = new string('a', 64),
            FullModId = 10,
            FullModVersion = "5.4.0",
            DllMods =
            [
                new ModPackDllModRequest { DllModId = 42, DllModVersion = "2.0" }
            ],
            ExternalDlls =
            [
                new ModPackExternalDllDeclaration
                {
                    FileName = "custom.dll",
                    FileSha256 = new string('b', 64),
                    FileSize = 1024
                }
            ]
        };

        var json = ModPackCreateRequestSerializer.ToJson(request);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("externalDlls", out _));
        Assert.Equal(10, root.GetProperty("fullModId").GetInt32());
        Assert.Equal("5.4.0", root.GetProperty("fullModVersion").GetString());
        var dllMods = root.GetProperty("dllMods");
        Assert.Equal(JsonValueKind.Array, dllMods.ValueKind);
        Assert.Single(dllMods.EnumerateArray());
        Assert.Equal(42, dllMods[0].GetProperty("dllModId").GetInt32());
        Assert.Equal("2.0", dllMods[0].GetProperty("dllModVersion").GetString());
    }

    [Fact]
    public void ToJson_IncludesMetadata_WhenProvided()
    {
        var request = new ModPackCreateRequest
        {
            CreatorHash = new string('a', 64),
            FullModId = 0,
            FullModVersion = "custom",
            Metadata = JsonSerializer.SerializeToElement(new { amongVersion = "2024-6-18" })
        };

        var json = ModPackCreateRequestSerializer.ToJson(request);

        using var doc = JsonDocument.Parse(json);
        var metadata = doc.RootElement.GetProperty("metadata");
        Assert.Equal("2024-6-18", metadata.GetProperty("amongVersion").GetString());
    }
}
