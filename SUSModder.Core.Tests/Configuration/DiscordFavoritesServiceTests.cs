using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Moq;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Tests.Configuration;

public class DiscordFavoritesServiceTests
{
    [Theory]
    [InlineData("""[{"id":1,"name":"A","packCode":"PACK-123","installable":true,"vtStatus":"clean"}]""")]
    [InlineData("""{"success":true,"discordFavs":[{"id":1,"name":"A","packCode":"PACK-123","installable":true,"vtStatus":"clean"}]}""")]
    public async Task FavoritesShapes_PreservePackFields(string json)
    {
        var service = CreateService(json);
        var result = await service.GetDiscordFavoritesAsync();
        var item = Assert.Single(result);
        Assert.Equal("PACK-123", item.PackCode);
        Assert.True(item.Installable);
        Assert.Equal("clean", item.VtStatus);
    }

    [Theory]
    [InlineData("""[{"id":1,"name":"A"}]""")]
    [InlineData("""{"success":true,"discordFavs":[{"id":1,"name":"A"}]}""")]
    public async Task OldBackendPayloadDefaultsToUnavailable(string json)
    {
        var item = Assert.Single(await CreateService(json).GetDiscordFavoritesAsync());
        Assert.Null(item.PackCode);
        Assert.Null(item.Installable);
    }

    [Theory]
    [InlineData("""[{"id":1,"name":"A","installable":null}]""")]
    [InlineData("""{"success":true,"discordFavs":[{"id":1,"name":"A","installable":null}]}""")]
    public async Task NullInstallable_PreservesServerAndDefaultsToUnavailable(string json)
    {
        var item = Assert.Single(await CreateService(json).GetDiscordFavoritesAsync());
        Assert.Null(item.Installable);
    }

    private static DiscordFavoritesService CreateService(string json)
    {
        var api = new Mock<ISUSModderApiClient>();
        api.Setup(x => x.GetDiscordFavoritesPublicAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SusModderApiResult<System.Text.Json.JsonElement>
            {
                StatusCode = (int)HttpStatusCode.OK,
                Data = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone()
            });
        return new DiscordFavoritesService(new ConfigurationBuilder().Build(),
            new Mock<IDiagnosticsOutput>().Object, api.Object);
    }
}
