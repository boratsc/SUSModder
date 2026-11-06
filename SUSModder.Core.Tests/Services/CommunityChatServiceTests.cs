using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SUSModder.Core;
using SUSModder.Core.Api;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Tests.Support;
using SUSModder.Core.Utilities;
using Xunit;

namespace SUSModder.Core.Tests.Services;

/// <summary>
/// Testy transportu globalnego komunikatora na fake HTTP (bez ruchu sieciowego,
/// bez zgłoszeń do produkcyjnego API).
/// </summary>
public partial class CommunityChatServiceTests
{
    private const string BaseUrl = "https://api.test.invalid/v2";
    private const string UserHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ValidContent = "abcdefghij";

    private static (CommunityChatService Service, FakeHttpMessageHandler Handler, TestDiagnosticsOutput Log) CreateService(
        Func<RecordedRequest, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = BaseUrl
            })
            .Build();

        var log = new TestDiagnosticsOutput();
        var apiClient = new SUSModderApiClient(configuration, log, httpClient);
        var service = new CommunityChatService(configuration, log, new FixedHardwareIdProvider(UserHash), apiClient);

        return (service, handler, log);
    }

    private static HttpResponseMessage Ok(string json) =>
        FakeHttpMessageHandler.Json(HttpStatusCode.OK, CommunityChatTestPayload.WithAuthors(json));

    // ═══════════════════════════════════════════════════════════════
    // Odczyt — stały kanał, envelope, awarie
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("authorId")]
    [InlineData("author_id")]
    [InlineData("userHash")]
    [InlineData("user_hash")]
    public async Task GetMessagesAsync_FlatIdentityNeverGeneratesNickname(string field)
    {
        var json = "{\"success\":true,\"entries\":[{\"id\":\"m1\",\"content\":\"Hello there\",\"" +
            field + "\":\"author-a\",\"isOwn\":false,\"canDelete\":false}]}";
        var (service, _, _) = CreateService(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, json));
        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task GetMessagesAsync_LegacyMessagesHaveNoInventedAuthor()
    {
        var (service, _, _) = CreateService(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK,
            """{"success":true,"entries":[{"id":"m1","content":"Hello there"}]}"""));
        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task SendMessageAsync_UsesResponseAuthorNotLocalHardwareIdentity()
    {
        var (service, _, _) = CreateService(_ => Ok(
            """{"data":{"id":"m1","content":"server content","publishedAt":"2026-10-25T18:00:00Z","isOwn":false,"canDelete":false,"author":{"id":"server-author","nickname":{"pl":"Nazwa serwera","en":"Server name"}}}}"""));
        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("server-author", result.Message?.Author?.Id);
        Assert.Equal("Nazwa serwera", result.Message?.Author?.Nickname.Pl);
        Assert.Equal("server content", result.Message?.Content);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T18:00:00Z"), result.Message?.PublishedAt);
        Assert.False(result.Message?.IsOwn);
        Assert.False(result.Message?.CanDelete);
    }

    [Fact]
    public async Task GetMessagesAsync_UsesCommunityChannelAndMessageType()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true,"entries":[]}"""));

        await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v2/chat", request.Uri.AbsolutePath);
        Assert.Contains("modId=1", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("type=message", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("limit=50", request.Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMessagesAsync_ClampsLimitToHistorySize()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true,"entries":[]}"""));

        await service.GetMessagesAsync(limit: 500, ct: TestContext.Current.CancellationToken);

        Assert.Contains("limit=50", handler.LastRequest.Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMessagesAsync_SendsUserHashAndAuthorizationHeader()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true,"entries":[]}"""));

        await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(UserHash, handler.LastRequest.GetHeader("X-User-Hash"));

        // Wartość tokenu transportowego nie jest ujawniana ani zapisywana w logach testu.
        var hasToken = !string.IsNullOrEmpty(SecretProvider.GetDownloadToken());
        Assert.True(
            !hasToken || handler.LastRequest.HasHeader("Authorization"),
            "Klient API powinien dołączać nagłówek Authorization, gdy token transportowy jest skonfigurowany.");
    }

    [Fact]
    public async Task GetMessagesAsync_EmptyConversation_IsSuccessNotFailure()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"success":true,"entries":[]}"""));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.Empty, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Messages);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task GetMessagesAsync_NetworkFailure_ReturnsNetworkError()
    {
        var (service, _, _) = CreateService(_ => throw new HttpRequestException("offline"));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.NetworkError, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatErrorCodes.NetworkError, result.ErrorCode);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task GetMessagesAsync_MalformedJson_ReturnsProtocolError()
    {
        var (service, _, _) = CreateService(_ => Ok("{ this is not json"));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
        Assert.Equal(CommunityChatErrorCodes.ProtocolError, result.ErrorCode);
    }

    [Fact]
    public async Task GetMessagesAsync_UnexpectedEnvelope_ReturnsProtocolError()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"success":true}"""));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Fact]
    public async Task GetMessagesAsync_Unauthorized_ReturnsUnauthorized()
    {
        var (service, _, _) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, """{"error":{"code":"UNAUTHORIZED"}}"""));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.Unauthorized, result.Status);
        Assert.Equal("UNAUTHORIZED", result.ErrorCode);
        Assert.Equal(401, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetMessagesAsync_Forbidden_ReturnsForbidden()
    {
        var (service, _, _) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, """{"error":{"code":"FORBIDDEN"}}"""));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.Forbidden, result.Status);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }

    [Fact]
    public async Task GetMessagesAsync_RateLimited_ReturnsRetryAfter()
    {
        var (service, _, _) = CreateService(_ =>
            FakeHttpMessageHandler.Json(
                HttpStatusCode.TooManyRequests,
                """{"error":{"code":"RATE_LIMITED"}}""",
                new Dictionary<string, string> { ["Retry-After"] = "42" }));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.RateLimited, result.Status);
        Assert.Equal(42, result.RetryAfterSeconds);
        Assert.Equal("RATE_LIMITED", result.ErrorCode);
    }

    [Fact]
    public async Task GetMessagesAsync_ServerError_ReturnsServerError()
    {
        var (service, _, _) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, """{"error":{"code":"INTERNAL_ERROR"}}"""));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.ServerError, result.Status);
        Assert.Equal("INTERNAL_ERROR", result.ErrorCode);
    }

    [Fact]
    public async Task GetMessagesAsync_Cancelled_ReturnsCancelled()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true,"entries":[]}"""));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.GetMessagesAsync(ct: cts.Token);

        Assert.Equal(CommunityChatStatus.Cancelled, result.Status);
        Assert.Equal(CommunityChatErrorCodes.Cancelled, result.ErrorCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("""[{"id":"e1","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}]""")]
    [InlineData("""{"data":[{"id":"e1","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}]}""")]
    [InlineData("""{"data":{"entries":[{"id":"e1","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}]}}""")]
    [InlineData("""{"success":true,"entries":[{"id":"e1","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}]}""")]
    public async Task GetMessagesAsync_ParsesEnvelopeVariants(string body)
    {
        var (service, _, _) = CreateService(_ => Ok(body));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.Success, result.Status);
        Assert.Single(result.Messages);
        Assert.Equal("e1", result.Messages[0].Id);
        Assert.Equal("abc", result.Messages[0].Content);
    }

    [Fact]
    public async Task GetMessagesAsync_DeduplicatesById()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[
              {"id":"e1","content":"abc","publishedAt":"2026-10-23T10:00:00Z"},
              {"id":"e1","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}
            ]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
    }

    [Fact]
    public async Task GetMessagesAsync_SortsChronologically()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[
              {"id":"e3","content":"c","publishedAt":"2026-10-23T12:00:00Z"},
              {"id":"e1","content":"a","publishedAt":"2026-10-23T10:00:00Z"},
              {"id":"e2","content":"b","publishedAt":"2026-10-23T11:00:00Z"}
            ]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "e1", "e2", "e3" }, result.Messages.Select(m => m.Id).ToArray());
    }

    [Fact]
    public async Task GetMessagesAsync_IgnoresEntriesOfOtherType()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[
              {"id":"c1","type":"code","code":"ABCDEF"},
              {"id":"e1","type":"message","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}
            ]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
        Assert.Equal("e1", result.Messages[0].Id);
    }

    [Fact]
    public async Task GetMessagesAsync_DoesNotInferOwnershipFromSentId()
    {
        var (service, handler, _) = CreateService(request =>
            request.Method == HttpMethod.Post
                ? Ok(CommunityChatTestPayload.SuccessfulPost())
                : Ok("""{"success":true,"entries":[{"id":"e1","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}]}"""));

        var sendResult = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);
        Assert.True(sendResult.IsSuccess);

        var readResult = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(readResult.Messages);
        Assert.False(readResult.Messages[0].IsOwn);
        Assert.False(readResult.Messages[0].CanDelete);
    }

    [Fact]
    public async Task GetMessagesAsync_ServerOwnershipIsRespected()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[{"id":"e1","content":"abc","isOwn":true,"publishedAt":"2026-10-23T10:00:00Z"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.True(result.Messages[0].IsOwn);
    }

    // ═══════════════════════════════════════════════════════════════
    // Publikacja
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SendMessageAsync_PostsToCommunityChannel()
    {
        var (service, handler, _) = CreateService(_ => Ok(CommunityChatTestPayload.SuccessfulPost()));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.Success, result.Status);
        Assert.Equal("e1", result.Message?.Id);
        Assert.True(result.Message?.IsOwn);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v2/chat", request.Uri.AbsolutePath);

        using var document = JsonDocument.Parse(request.Body!);
        var root = document.RootElement;
        Assert.Equal("message", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("modId").GetInt32());
        Assert.Equal(ValidContent, root.GetProperty("content").GetString());
    }

    [Fact]
    public async Task SendMessageAsync_SendsTrimmedContent()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true,"id":"e1"}"""));

        await service.SendMessageAsync("   " + ValidContent + "   ", TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(handler.LastRequest.Body!);
        Assert.Equal(ValidContent, document.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task SendMessageAsync_InvalidContent_DoesNotCallApi()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true,"id":"e1"}"""));

        var result = await service.SendMessageAsync("abc", TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.ValidationFailed, result.Status);
        Assert.Equal(CommunityChatErrorCodes.ContentTooShort, result.ErrorCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SendMessageAsync_DisallowedLink_IsRejectedLocally()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true,"id":"e1"}"""));

        var result = await service.SendMessageAsync("https://example.com/abc", TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.ValidationFailed, result.Status);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, result.ErrorCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SendMessageAsync_ServerRejection_ReturnsServerCode()
    {
        var (service, _, _) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"error":{"code":"DUPLICATE_MESSAGE"}}"""));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.RequestFailed, result.Status);
        Assert.Equal("DUPLICATE_MESSAGE", result.ErrorCode);
    }

    [Fact]
    public async Task SendMessageAsync_RateLimited_ReturnsRetryAfter()
    {
        var (service, _, _) = CreateService(_ =>
            FakeHttpMessageHandler.Json(
                HttpStatusCode.TooManyRequests,
                """{"error":{"code":"RATE_LIMITED"}}""",
                new Dictionary<string, string> { ["Retry-After"] = "30" }));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.RateLimited, result.Status);
        Assert.Equal(30, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task SendMessageAsync_NetworkFailure_ReturnsNetworkError()
    {
        var (service, _, _) = CreateService(_ => throw new HttpRequestException("offline"));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.NetworkError, result.Status);
        Assert.Equal(CommunityChatErrorCodes.NetworkError, result.ErrorCode);
    }

    [Fact]
    public async Task SendMessageAsync_Cancelled_ReturnsCancelled()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"success":true,"id":"e1"}"""));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.SendMessageAsync(ValidContent, cts.Token);

        Assert.Equal(CommunityChatStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task SendMessageAsync_ReportsModerationWarning()
    {
        var (service, _, _) = CreateService(_ => Ok(CommunityChatTestPayload.SuccessfulPost(moderationWarning: true)));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.ModerationWarning);
    }

    // ═══════════════════════════════════════════════════════════════
    // Moderacja: usuwanie i zgłaszanie
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteMessageAsync_DeletesEntryById()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true}"""));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest.Method);
        Assert.Equal("/v2/chat/e1", handler.LastRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task DeleteMessageAsync_Forbidden_ReturnsForbidden()
    {
        var (service, _, _) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, """{"error":{"code":"NOT_OWNER"}}"""));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.Forbidden, result.Status);
        Assert.Equal("NOT_OWNER", result.ErrorCode);
    }

    [Fact]
    public async Task DeleteMessageAsync_NotFound_ReturnsNotFound()
    {
        var (service, _, _) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"error":{"code":"NOT_FOUND"}}"""));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task DeleteMessageAsync_EmptyId_IsRejected()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true}"""));

        var result = await service.DeleteMessageAsync("  ", TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.ValidationFailed, result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ReportMessageAsync_PostsReportWithReason()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true}"""));

        var result = await service.ReportMessageAsync("e1", "spam", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal("/v2/chat/e1/report", handler.LastRequest.Uri.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastRequest.Body!);
        Assert.Equal("spam", document.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ReportMessageAsync_UsesDefaultReason()
    {
        var (service, handler, _) = CreateService(_ => Ok("""{"success":true}"""));

        await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(handler.LastRequest.Body!);
        Assert.Equal(CommunityChatConstants.DefaultReportReason, document.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task SendMessageAsync_ServerReturnsSuccessFalse_IsTreatedAsFailure()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"success":false,"error":{"code":"CONTENT_BLOCKED"}}"""));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.RequestFailed, result.Status);
        Assert.Equal("CONTENT_BLOCKED", result.ErrorCode);
    }

    [Fact]
    public async Task ReportMessageAsync_ServerReturnsSuccessFalse_IsTreatedAsFailure()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"success":false,"error":{"code":"ALREADY_REPORTED"}}"""));

        var result = await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("ALREADY_REPORTED", result.ErrorCode);
    }

    [Fact]
    public async Task ReportMessageAsync_ServerRejection_ReturnsServerCode()
    {
        var (service, _, _) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"error":{"code":"ALREADY_REPORTED"}}"""));

        var result = await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("ALREADY_REPORTED", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // Diagnostyka
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Diagnostics_DoNotContainMessageContent()
    {
        const string secretContent = "TajnaTreśćWiadomości123";
        var (service, _, log) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, """{"error":{"code":"INTERNAL_ERROR"}}"""));

        await service.SendMessageAsync(secretContent, TestContext.Current.CancellationToken);

        Assert.NotEmpty(log.Messages);
        Assert.DoesNotContain(log.Messages, line => line.Contains(secretContent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Diagnostics_DoNotContainResponseBody()
    {
        var (service, _, log) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"error":{"code":"X","message":"tresc-odpowiedzi"}}"""));

        await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(log.Messages, line => line.Contains("tresc-odpowiedzi", StringComparison.Ordinal));
    }

    private sealed class FixedHardwareIdProvider : IHardwareIdProvider
    {
        private readonly string _hash;

        public FixedHardwareIdProvider(string hash) => _hash = hash;

        public string GetAnonymousUserHash() => _hash;
    }
}
