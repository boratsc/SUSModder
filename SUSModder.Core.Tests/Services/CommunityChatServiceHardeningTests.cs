using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Tests.Support;
using SUSModder.Core.Utilities;
using Xunit;

namespace SUSModder.Core.Tests.Services;

/// <summary>
/// Regresje utrwalone po przeglądzie bezpieczeństwa kontraktu komunikatora:
/// 1) publikacja wymaga dodatniej umowy odpowiedzi (brak fałszywego "sukces" przy niepewnej odpowiedzi),
/// 2) DELETE z 200 + success=false jest porażką,
/// 3) sygnał porażki w odpowiedzi jest rozpoznawany w korzeniu i w zagnieżdżonym "data",
/// 4) kod błędu z odpowiedzi serwera jest traktowany jako dane nieufne (biała lista w logach),
/// 5) limit historii i odsiewanie wygasłych wiadomości egzekwowane po stronie klienta.
/// </summary>
public class CommunityChatServiceHardeningTests
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

    private static HttpResponseMessage Ok(string body) =>
        FakeHttpMessageHandler.Json(HttpStatusCode.OK, CommunityChatTestPayload.WithAuthors(body));

    // ═══════════════════════════════════════════════════════════════
    // (1) Publikacja wymaga dodatniej umowy odpowiedzi
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("{ to nie json")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"foo":"bar"}""")]
    [InlineData("""{"data":{}}""")]
    public async Task SendMessageAsync_UncertainResponse_ReturnsProtocolError(string body)
    {
        var (service, _, _) = CreateService(_ => Ok(body));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
        Assert.Equal(CommunityChatErrorCodes.ProtocolError, result.ErrorCode);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task SendMessageAsync_ProtocolError_DoesNotRetryRequest()
    {
        var (service, handler, _) = CreateService(_ => Ok("{}"));

        await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SendMessageAsync_ExplicitSuccessWithoutId_IsUncertain()
    {
        // A success flag alone cannot replace the persisted message snapshot.
        var (service, _, _) = CreateService(_ => Ok("""{"success":true}"""));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task SendMessageAsync_IdWithoutMessage_IsUncertain()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"id":"e1"}"""));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task SendMessageAsync_NestedIdWithoutMessage_IsUncertain()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"data":{"id":"e1"}}"""));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Fact]
    public async Task SendMessageAsync_SuccessFalseWithId_IsFailure()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"success":false,"id":"e1"}"""));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.RequestFailed, result.Status);
        Assert.Null(result.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    // (2) DELETE: 200 + success=false to porażka
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteMessageAsync_SuccessFalseBody_ReturnsFailure()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"success":false,"error":{"code":"NOT_OWNER"}}"""));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.RequestFailed, result.Status);
        Assert.Equal("NOT_OWNER", result.ErrorCode);
    }

    [Fact]
    public async Task DeleteMessageAsync_NestedSuccessFalse_ReturnsFailure()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"success":true,"data":{"success":false}}"""));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.RequestFailed, result.Status);
    }

    [Fact]
    public async Task DeleteMessageAsync_SuccessTrueBody_ReturnsSuccess()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"success":true}"""));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteMessageAsync_EmptyBody_ReturnsProtocolError()
    {
        // 2xx bez treści nie potwierdza wykonania operacji — UI dostanie niepewny wynik.
        var (service, _, _) = CreateService(_ => Ok(string.Empty));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Fact]
    public async Task DeleteMessageAsync_NoContentResponse_ReturnsSuccess()
    {
        // 204 No Content jest jednoznacznym potwierdzeniem usunięcia.
        var (service, _, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ReportMessageAsync_MalformedBody_ReturnsProtocolError()
    {
        var (service, _, _) = CreateService(_ => Ok("<html>error</html>"));

        var result = await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Fact]
    public async Task ReportMessageAsync_EmptyJsonObject_ReturnsProtocolError()
    {
        var (service, _, _) = CreateService(_ => Ok("{}"));

        var result = await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Fact]
    public async Task ReportMessageAsync_ErrorMessageOnly_ReturnsProtocolError()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"message":"coś poszło nie tak"}"""));

        var result = await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Fact]
    public async Task DeleteMessageAsync_ErrorMessageOnly_ReturnsProtocolError()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"message":"coś poszło nie tak"}"""));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Fact]
    public async Task GetMessagesAsync_ErrorMessageOnly_ReturnsProtocolError()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"message":"coś poszło nie tak"}"""));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Fact]
    public async Task SendMessageAsync_ErrorMessageOnly_ReturnsProtocolError()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"message":"coś poszło nie tak"}"""));

        var result = await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    // ═══════════════════════════════════════════════════════════════
    // (3) Sygnał porażki w korzeniu i w zagnieżdżonym "data"
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMessagesAsync_RootSuccessFalse_WithValidEntries_ReturnsFailure()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":false,"entries":[{"id":"e1","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.RequestFailed, result.Status);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task GetMessagesAsync_NestedSuccessFalse_WithValidEntries_ReturnsFailure()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"data":{"success":false,"entries":[{"id":"e1","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}]}}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.RequestFailed, result.Status);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task GetMessagesAsync_RootSuccessTrue_NestedSuccessFalse_ReturnsFailure()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"data":{"success":false,"entries":[{"id":"e1","content":"abc"}]}}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.RequestFailed, result.Status);
    }

    [Fact]
    public async Task ReportMessageAsync_NestedSuccessFalse_ReturnsFailure()
    {
        var (service, _, _) = CreateService(_ => Ok("""{"data":{"success":false,"error":{"code":"ALREADY_REPORTED"}}}"""));

        var result = await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("ALREADY_REPORTED", result.ErrorCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // (4) Kod błędu z odpowiedzi serwera jest danymi nieufnymi
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Diagnostics_UntrustedErrorCode_IsNotWrittenToLog()
    {
        const string injected = "EVIL\n[CommunityChat] GET failed: HTTP 200 status=Success code=OK";
        var body = "{\"error\":{\"code\":" + JsonSerializer.Serialize(injected) + "}}";
        var (service, _, log) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, body));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(log.Messages, line => line.Contains("EVIL", StringComparison.Ordinal));
        Assert.Contains(log.Messages, line => line.Contains("code=UNKNOWN", StringComparison.Ordinal));
        Assert.Equal(CommunityChatErrorCodes.RequestFailed, result.ErrorCode);
    }

    [Fact]
    public async Task Diagnostics_UnknownButSafeErrorCode_IsNotWrittenToLog()
    {
        var (service, _, log) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"error":{"code":"SOME_CODE_FROM_BACKEND"}}"""));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(log.Messages, line => line.Contains("SOME_CODE_FROM_BACKEND", StringComparison.Ordinal));
        Assert.Contains(log.Messages, line => line.Contains("code=UNKNOWN", StringComparison.Ordinal));

        // Kod spoza białej listy nie jest przekazywany do UI jako kod domenowy.
        Assert.Equal(CommunityChatErrorCodes.RequestFailed, result.ErrorCode);
    }

    [Fact]
    public async Task Diagnostics_KnownBackendErrorCode_IsKeptAndLogged()
    {
        var (service, _, log) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"RATE_LIMITED"}}"""));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal("RATE_LIMITED", result.ErrorCode);
        Assert.Contains(log.Messages, line => line.Contains("code=RATE_LIMITED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Diagnostics_UntrustedErrorMessage_IsSanitized()
    {
        var (service, _, log) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"error":{"code":"RATE_LIMITED","message":"linia1\nlinia2 z dziwnymi \u0007 znakami"}}"""));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain("\n", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0007", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Messages, line => line.Contains("linia1", StringComparison.Ordinal));
    }

    // ═══════════════════════════════════════════════════════════════
    // (5) Limit historii i odsiewanie wygasłych wiadomości
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMessagesAsync_EnforcesMaximumOfFiftyMessages()
    {
        var (service, _, _) = CreateService(_ => Ok(BuildEntries(60)));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatLimits.MaxHistorySize, result.Messages.Count);
        Assert.Equal("e60", result.Messages[^1].Id);
    }

    [Fact]
    public async Task GetMessagesAsync_ClampsToRequestedLimitKeepingNewest()
    {
        var (service, handler, _) = CreateService(_ => Ok(BuildEntries(30)));

        var result = await service.GetMessagesAsync(limit: 10, ct: TestContext.Current.CancellationToken);

        Assert.Contains("limit=10", handler.LastRequest.Uri.Query, StringComparison.Ordinal);
        Assert.Equal(10, result.Messages.Count);
        Assert.Equal("e21", result.Messages[0].Id);
        Assert.Equal("e30", result.Messages[^1].Id);
    }

    [Fact]
    public async Task GetMessagesAsync_FiltersExpiredMessages()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[
              {"id":"expired","content":"abc","publishedAt":"2026-10-23T10:00:00Z","expiresAt":"2020-01-01T00:00:00Z"},
              {"id":"active","content":"abc","publishedAt":"2026-10-23T10:01:00Z","expiresAt":"2099-01-01T00:00:00Z"},
              {"id":"without-ttl","content":"abc","publishedAt":"2026-10-23T10:02:00Z"}
            ]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Messages.Count);
        Assert.DoesNotContain(result.Messages, m => m.Id == "expired");
        Assert.Contains(result.Messages, m => m.Id == "active");
        Assert.Contains(result.Messages, m => m.Id == "without-ttl");
    }

    [Fact]
    public async Task GetMessagesAsync_AllEntriesExpired_ReturnsEmpty()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[
              {"id":"e1","content":"abc","publishedAt":"2020-01-01T10:00:00Z","expiresAt":"2020-02-01T00:00:00Z"}
            ]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.Empty, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Messages);
    }

    // ═══════════════════════════════════════════════════════════════
    // (6) Własność: jawne isOwn/own ma pierwszeństwo, fallback tylko gdy pola brak
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMessagesAsync_ExplicitIsOwnFalse_WinsOverSessionFallback()
    {
        var (service, _, _) = CreateService(request =>
            request.Method == HttpMethod.Post
                ? Ok("""{"success":true,"id":"e1"}""")
                : Ok("""{"success":true,"entries":[{"id":"e1","content":"abc","isOwn":false,"publishedAt":"2026-10-23T10:00:00Z"}]}"""));

        await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);
        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
        Assert.False(result.Messages[0].IsOwn);
        Assert.False(result.Messages[0].CanDelete);
    }

    [Fact]
    public async Task GetMessagesAsync_ExplicitOwnFalse_WinsOverSessionFallback()
    {
        var (service, _, _) = CreateService(request =>
            request.Method == HttpMethod.Post
                ? Ok("""{"success":true,"id":"e1"}""")
                : Ok("""{"success":true,"entries":[{"id":"e1","content":"abc","own":false,"publishedAt":"2026-10-23T10:00:00Z"}]}"""));

        await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);
        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
        Assert.False(result.Messages[0].IsOwn);
    }

    [Fact]
    public async Task GetMessagesAsync_AbsentOwnership_NeverUsesSessionFallback()
    {
        var (service, _, _) = CreateService(request =>
            request.Method == HttpMethod.Post
                ? Ok("""{"success":true,"id":"e1"}""")
                : Ok("""{"success":true,"entries":[{"id":"e1","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}]}"""));

        await service.SendMessageAsync(ValidContent, TestContext.Current.CancellationToken);
        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
        Assert.False(result.Messages[0].IsOwn);
        Assert.False(result.Messages[0].CanDelete);
    }

    [Fact]
    public async Task GetMessagesAsync_ExplicitCanDeleteFalse_Wins()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[{"id":"e1","content":"abc","isOwn":true,"canDelete":false,"publishedAt":"2026-10-23T10:00:00Z"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
        Assert.True(result.Messages[0].IsOwn);
        Assert.False(result.Messages[0].CanDelete);
    }

    [Fact]
    public async Task GetMessagesAsync_ExplicitCanDeleteTrue_Wins()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[{"id":"e1","content":"abc","isOwn":false,"canDelete":true,"publishedAt":"2026-10-23T10:00:00Z"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
        Assert.False(result.Messages[0].IsOwn);
        Assert.True(result.Messages[0].CanDelete);
    }

    [Fact]
    public async Task GetMessagesAsync_ForeignMessageWithoutOwnershipFlag_IsNotOwn()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[{"id":"other","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
        Assert.False(result.Messages[0].IsOwn);
        Assert.False(result.Messages[0].CanDelete);
    }

    // ═══════════════════════════════════════════════════════════════
    // (7) Potwierdzenie specyficzne dla operacji (DELETE / REPORT)
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"id":"e1","deleted":false}""")]
    [InlineData("""{"deleted":false}""")]
    [InlineData("""{"success":true,"deleted":false}""")]
    [InlineData("""{"data":{"deleted":false}}""")]
    [InlineData("""{"data":{"deleted":false},"success":true}""")]
    [InlineData("""{"removed":false}""")]
    public async Task DeleteMessageAsync_ExplicitNegativeOperationFlag_IsNotSuccess(string body)
    {
        var (service, _, _) = CreateService(_ => Ok(body));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.RequestFailed, result.Status);
    }

    [Theory]
    [InlineData("""{"id":"e1","reported":false}""")]
    [InlineData("""{"reported":false}""")]
    [InlineData("""{"success":true,"reported":false}""")]
    [InlineData("""{"data":{"reported":false}}""")]
    public async Task ReportMessageAsync_ExplicitNegativeOperationFlag_IsNotSuccess(string body)
    {
        var (service, _, _) = CreateService(_ => Ok(body));

        var result = await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.RequestFailed, result.Status);
    }

    [Theory]
    [InlineData("""{"id":"e1"}""")]
    [InlineData("""{"id":"e1","content":"abc"}""")]
    [InlineData("""{"reported":true}""")]
    [InlineData("""{"data":{"reported":true}}""")]
    public async Task DeleteMessageAsync_WithoutDeleteConfirmation_IsNotSuccess(string body)
    {
        // Sam identyfikator wpisu ani flaga innej operacji nie potwierdzają usunięcia.
        var (service, _, _) = CreateService(_ => Ok(body));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Theory]
    [InlineData("""{"id":"e1"}""")]
    [InlineData("""{"deleted":true}""")]
    [InlineData("""{"removed":true}""")]
    [InlineData("""{"data":{"deleted":true}}""")]
    public async Task ReportMessageAsync_WithoutReportConfirmation_IsNotSuccess(string body)
    {
        var (service, _, _) = CreateService(_ => Ok(body));

        var result = await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Theory]
    [InlineData("""{"success":true}""")]
    [InlineData("""{"deleted":true}""")]
    [InlineData("""{"removed":true}""")]
    [InlineData("""{"data":{"deleted":true}}""")]
    public async Task DeleteMessageAsync_WithDeleteConfirmation_IsSuccess(string body)
    {
        var (service, _, _) = CreateService(_ => Ok(body));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.Success, result.Status);
    }

    [Theory]
    [InlineData("""{"success":true}""")]
    [InlineData("""{"reported":true}""")]
    [InlineData("""{"data":{"reported":true}}""")]
    public async Task ReportMessageAsync_WithReportConfirmation_IsSuccess(string body)
    {
        var (service, _, _) = CreateService(_ => Ok(body));

        var result = await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.Success, result.Status);
    }

    [Fact]
    public async Task DeleteMessageAsync_ForeignOperationFlagDoesNotBlockSuccess()
    {
        // "reported": false nie dotyczy DELETE — nie może zablokować sukcesu.
        var (service, _, _) = CreateService(_ => Ok("""{"success":true,"reported":false}"""));

        var result = await service.DeleteMessageAsync("e1", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ReportMessageAsync_ForeignOperationFlagDoesNotBlockSuccess()
    {
        // "deleted": false nie dotyczy REPORT — nie może zablokować sukcesu.
        var (service, _, _) = CreateService(_ => Ok("""{"success":true,"deleted":false}"""));

        var result = await service.ReportMessageAsync("e1", ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    // ═══════════════════════════════════════════════════════════════
    // (8) Zgodność z legacy: odczyt pól bez rozróżniania wielkości liter
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("""{"Success":true,"Entries":[{"Id":"e1","Content":"abc","CreatedAt":"2026-10-23T10:00:00Z"}]}""")]
    [InlineData("""{"success":true,"entries":[{"id":"e1","content":"abc","createdAt":"2026-10-23T10:00:00Z"}]}""")]
    [InlineData("""{"SUCCESS":true,"ENTRIES":[{"ID":"e1","CONTENT":"abc","PUBLISHEDAT":"2026-10-23T10:00:00Z"}]}""")]
    [InlineData("""{"data":{"entries":[{"Id":"e1","Content":"abc"}]}}""")]
    public async Task GetMessagesAsync_ReadsEntriesCaseInsensitively(string body)
    {
        var (service, _, _) = CreateService(_ => Ok(body));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.Success, result.Status);
        Assert.Single(result.Messages);
        Assert.Equal("e1", result.Messages[0].Id);
        Assert.Equal("abc", result.Messages[0].Content);
    }

    [Fact]
    public async Task GetMessagesAsync_ReadsPascalCaseOwnershipFlags()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"Success":true,"Entries":[{"Id":"e1","Content":"abc","IsOwn":true,"CanDelete":true,"PublishedAt":"2026-10-23T10:00:00Z"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
        Assert.True(result.Messages[0].IsOwn);
        Assert.True(result.Messages[0].CanDelete);
    }

    [Fact]
    public async Task GetMessagesAsync_ReadsPascalCaseNegativeOwnershipFlags()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[{"Id":"e1","Content":"abc","IsOwn":false,"CanDelete":false,"PublishedAt":"2026-10-23T10:00:00Z"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
        Assert.False(result.Messages[0].IsOwn);
        Assert.False(result.Messages[0].CanDelete);
    }

    [Fact]
    public async Task GetMessagesAsync_ReadsPascalCaseTimestamps()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[{"Id":"e1","Content":"abc","CreatedAt":"2026-10-23T10:00:00Z","ExpiresAt":"2099-01-01T00:00:00Z"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Messages);
        Assert.NotNull(result.Messages[0].PublishedAt);
        Assert.NotNull(result.Messages[0].ExpiresAt);
    }

    [Fact]
    public async Task GetMessagesAsync_PascalCaseErrorEnvelope_IsFailure()
    {
        var (service, _, _) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"Error":{"Code":"RATE_LIMITED"}}"""));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("RATE_LIMITED", result.ErrorCode);
    }

    [Fact]
    public async Task GetMessagesAsync_AllEntriesMalformed_ReturnsProtocolError()
    {
        // Niepusta lista, z której nie dało się odczytać wpisów, to błąd protokołu, nie "pusta rozmowa".
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[{"foo":"bar"},{"baz":1}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
    }

    [Fact]
    public async Task GetMessagesAsync_OnlyLegacyCodeEntries_ReturnsEmpty()
    {
        // Odfiltrowane wpisy legacy (type=code) to nie błąd — pusta rozmowa.
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[{"Id":"c1","Type":"code","Code":"ABCDEF"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.Empty, result.Status);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetMessagesAsync_OnlyExpiredEntries_ReturnsEmpty()
    {
        // Wygasłe wpisy są odfiltrowywane po odczytaniu — to nie błąd protokołu.
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[{"id":"e1","content":"abc","publishedAt":"2020-01-01T10:00:00Z","expiresAt":"2020-02-01T00:00:00Z"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.Empty, result.Status);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetMessagesAsync_PartiallyMalformedEntries_ReturnsProtocolError()
    {
        var (service, _, _) = CreateService(_ => Ok("""
            {"success":true,"entries":[{"foo":"bar"},{"id":"e2","content":"abc","publishedAt":"2026-10-23T10:00:00Z"}]}
            """));

        var result = await service.GetMessagesAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(CommunityChatStatus.ProtocolError, result.Status);
        Assert.Empty(result.Messages);
    }

    private static string BuildEntries(int count)
    {
        var builder = new StringBuilder();
        builder.Append("{\"success\":true,\"entries\":[");

        for (var i = 1; i <= count; i++)
        {
            if (i > 1)
                builder.Append(',');

            builder.Append("{\"id\":\"e").Append(i.ToString("00"))
                .Append("\",\"content\":\"abc\",\"publishedAt\":\"2026-10-23T10:00:00Z\"}");
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private sealed class FixedHardwareIdProvider : IHardwareIdProvider
    {
        private readonly string _hash;

        public FixedHardwareIdProvider(string hash) => _hash = hash;

        public string GetAnonymousUserHash() => _hash;
    }
}
