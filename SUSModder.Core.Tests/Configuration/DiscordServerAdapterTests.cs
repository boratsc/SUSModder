using System.Text.Json;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;

namespace SUSModder.Core.Tests.Configuration;

public class DiscordServerAdapterTests
{
    [Fact]
    public void FromServerData_MapsPackFields_AndDefaultsWhenMissing()
    {
        var data = JsonSerializer.Deserialize<DiscordServerData>("""
            {"packCode":"PACK-123","packModName":"Town of Us","fullModVersion":"6.0","installable":true,"vtStatus":"clean"}
            """)!;

        var server = DiscordServerAdapter.FromServerData(data);
        Assert.Equal("PACK-123", server.PackCode);
        Assert.Equal("Town of Us", server.PackModName);
        Assert.Equal("6.0", server.FullModVersion);
        Assert.True(server.Installable);
        Assert.Equal("clean", server.VtStatus);

        server = DiscordServerAdapter.FromServerData(new DiscordServerData());
        Assert.Null(server.PackCode);
        Assert.False(server.Installable);

        data = JsonSerializer.Deserialize<DiscordServerData>("""{"installable":null}""")!;
        Assert.False(DiscordServerAdapter.FromServerData(data).Installable);
    }
}
