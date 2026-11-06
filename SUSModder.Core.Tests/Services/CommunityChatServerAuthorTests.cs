using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using SUSModder.Core.Models;
using SUSModder.Core.Tests.Support;

namespace SUSModder.Core.Tests.Services;

public partial class CommunityChatServiceTests
{
    private const string ServerMessage = """
        {"id":"22222222-2222-4222-8222-222222222222","type":"message",
         "content":"server normalized content","publishedAt":"2026-10-25T18:00:00Z",
         "expiresAt":"2099-11-01T18:00:00Z","isOwn":true,"canDelete":false,
         "authorId":"compatibility-id-must-not-be-used",
         "author":{"id":"chat-author","nickname":{"pl":"Kosmiczna Kapibara · A7F2C9","en":"Cosmic Capybara · A7F2C9"}},
         "moderationWarning":true}
        """;

    [Fact]
    public async Task CanonicalGetAndPostUseIdenticalServerSnapshot()
    {
        var (service, handler, log) = CreateService(request => FakeHttpMessageHandler.Json(
            request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK,
            request.Method == HttpMethod.Post ? "{\"data\":" + ServerMessage + "}" :
                "{\"data\":[" + ServerMessage + "],\"meta\":{\"total\":1,\"limit\":50}}"));
        var sent = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);
        var history = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);
        Assert.True(sent.IsSuccess);
        Assert.True(sent.ModerationWarning);
        Assert.True(history.IsSuccess);
        Assert.Equal(sent.Message, Assert.Single(history.Messages));
        Assert.Equal("chat-author", sent.Message!.Author!.Id);
        Assert.NotEqual(sent.Message.Id, sent.Message.Author.Id);
        Assert.False(sent.Message.CanDelete);
        Assert.Equal("/v2/chat", handler.LastRequest.Uri.AbsolutePath);
        Assert.DoesNotContain(log.Messages, line => line.Contains(UserHash) || line.Contains("server normalized content") || line.Contains("chat-author"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"id\":\"a\"}")]
    [InlineData("{\"id\":\"\",\"nickname\":{\"pl\":\"PL\",\"en\":\"EN\"}}")]
    [InlineData("{\"id\":42,\"nickname\":{\"pl\":\"PL\",\"en\":\"EN\"}}")]
    [InlineData("{\"id\":\"a\",\"nickname\":null}")]
    [InlineData("{\"id\":\"a\",\"nickname\":{\"pl\":\"PL\"}}")]
    [InlineData("{\"id\":\"a\",\"nickname\":{\"pl\":\"PL\",\"en\":\" \"}}")]
    [InlineData("{\"id\":\"a\",\"nickname\":{\"pl\":42,\"en\":\"EN\"}}")]
    [InlineData("{\"id\":\"a\",\"nickname\":{\"pl\":\"PL\\n\",\"en\":\"EN\"}}")]
    [InlineData("{\"id\":\"a\",\"nickname\":{\"pl\":\"PL\\u202e\",\"en\":\"EN\"}}")]
    public async Task IncompleteAuthorIsProtocolErrorForBothGetAndPost(string author)
    {
        var message = JsonNode.Parse(ServerMessage)!;
        message["author"] = JsonNode.Parse(author);
        var (service, handler, _) = CreateService(request => FakeHttpMessageHandler.Json(HttpStatusCode.OK,
            request.Method == HttpMethod.Post ? "{\"data\":" + message.ToJsonString() + "}" :
                "{\"data\":[" + message.ToJsonString() + "]}"));
        var history = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);
        var sent = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);
        Assert.Equal(CommunityChatStatus.ProtocolError, history.Status);
        Assert.Empty(history.Messages);
        Assert.Equal(CommunityChatStatus.ProtocolError, sent.Status);
        Assert.Null(sent.Message);
        Assert.Equal(2, handler.RequestCount); // one GET, one POST, never retry POST
    }

    [Theory]
    [InlineData("id")]
    [InlineData("author")]
    [InlineData("content")]
    [InlineData("publishedAt")]
    public async Task MissingPersistedMessageFieldCannotBeSynthesized(string field)
    {
        var message = JsonNode.Parse(ServerMessage)!.AsObject();
        message.Remove(field);
        var (service, _, _) = CreateService(_ => FakeHttpMessageHandler.Json(HttpStatusCode.Created,
            "{\"data\":" + message.ToJsonString() + "}"));
        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData(401, "{\"error\":\"Unauthorized\"}", CommunityChatStatus.Unauthorized)]
    [InlineData(401, "{\"error\":{\"code\":\"UNAUTHORIZED\"}}", CommunityChatStatus.Unauthorized)]
    [InlineData(409, "{\"error\":{\"code\":\"DUPLICATE_MESSAGE\"}}", CommunityChatStatus.RequestFailed)]
    [InlineData(429, "{\"error\":{\"code\":\"RATE_LIMITED\"}}", CommunityChatStatus.RateLimited)]
    [InlineData(503, "{\"error\":{\"code\":\"CHAT_UNAVAILABLE\"}}", CommunityChatStatus.ServerError)]
    [InlineData(503, "{\"error\":{\"code\":\"CHAT_AUTHOR_UNAVAILABLE\"}}", CommunityChatStatus.ServerError)]
    public async Task CanonicalErrorEnvelopesRemainFailures(int status, string body, CommunityChatStatus expected)
    {
        var (service, _, _) = CreateService(_ => FakeHttpMessageHandler.Json((HttpStatusCode)status, body,
            new Dictionary<string, string> { ["Retry-After"] = "42" }));
        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Message);
        if (status == 429) Assert.Equal(42, result.RetryAfterSeconds);
    }
}
