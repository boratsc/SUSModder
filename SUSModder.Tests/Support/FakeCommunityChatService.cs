using System;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Tests.Support;

/// <summary>
/// Deterministyczna fake usługa komunikatora dla testów VM.
/// Liczniki operacji (Interlocked) i konfigurowalne fabryki wyników.
/// </summary>
internal sealed class FakeCommunityChatService : ICommunityChatService
{
    private int _getCalls;
    private int _sendCalls;
    private int _deleteCalls;
    private int _reportCalls;

    public Func<int, CommunityChatMessagesResult>? GetFactory { get; set; }
    public Func<string, CommunityChatSendResult>? SendFactory { get; set; }
    public Func<string, CommunityChatModerationResult>? DeleteFactory { get; set; }
    public Func<string, CommunityChatModerationResult>? ReportFactory { get; set; }
    public Func<Task>? SendDelay { get; set; }

    public int GetCalls => Volatile.Read(ref _getCalls);
    public int SendCalls => Volatile.Read(ref _sendCalls);
    public int DeleteCalls => Volatile.Read(ref _deleteCalls);
    public int ReportCalls => Volatile.Read(ref _reportCalls);

    public string? LastSentContent { get; private set; }
    public string? LastDeletedId { get; private set; }
    public string? LastReportedId { get; private set; }

    public FakeCommunityChatService()
    {
        GetFactory = _ => Success();
        SendFactory = content => new CommunityChatSendResult
            { Status = CommunityChatStatus.Success, Message = Msg("sent-id", content, own: true) };
        DeleteFactory = _ => new CommunityChatModerationResult { Status = CommunityChatStatus.Success };
        ReportFactory = _ => new CommunityChatModerationResult { Status = CommunityChatStatus.Success };
    }

    public int ChannelModId => CommunityChatConstants.ChannelModId;

    public Task<CommunityChatMessagesResult> GetMessagesAsync(
        int limit = CommunityChatLimits.MaxHistorySize,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _getCalls);
        return Task.FromResult((GetFactory ?? (_ => Success()))(Volatile.Read(ref _getCalls)));
    }

    public async Task<CommunityChatSendResult> SendMessageAsync(string content, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _sendCalls);
        LastSentContent = content;

        if (SendDelay is not null)
            await SendDelay();

        return (SendFactory ?? (_ => new CommunityChatSendResult { Status = CommunityChatStatus.Success }))(content);
    }

    public Task<CommunityChatModerationResult> DeleteMessageAsync(string messageId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _deleteCalls);
        LastDeletedId = messageId;
        return Task.FromResult((DeleteFactory ?? (_ => new CommunityChatModerationResult { Status = CommunityChatStatus.Success }))(messageId));
    }

    public Task<CommunityChatModerationResult> ReportMessageAsync(
        string messageId,
        string? reason = null,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _reportCalls);
        LastReportedId = messageId;
        return Task.FromResult((ReportFactory ?? (_ => new CommunityChatModerationResult { Status = CommunityChatStatus.Success }))(messageId));
    }

    private static CommunityChatMessagesResult Success(params CommunityChatMessage[] messages) =>
        new()
        {
            Status = messages.Length == 0 ? CommunityChatStatus.Empty : CommunityChatStatus.Success,
            Messages = messages
        };

    public static CommunityChatMessage Msg(string id, string content = "tresc testowa 1234567890", bool own = false) =>
        new()
        {
            Id = id,
            Content = content,
            PublishedAt = DateTimeOffset.UtcNow,
            IsOwn = own,
            CanDelete = own,
            Author = new CommunityChatAuthor("test-author",
                new CommunityChatNickname("Kosmiczna Kapibara · ABC123", "Cosmic Capybara · ABC123"))
        };
}
