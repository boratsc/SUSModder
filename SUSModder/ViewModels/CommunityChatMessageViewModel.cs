using System;
using ReactiveUI;
using SUSModder.Core.Models;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels;

/// <summary>
/// Pojedyncza wiadomość we wspólnym kanale globalnego komunikatora.
/// Instancje są utrwalane między odświeżeniami (deduplikacja po ID),
/// dzięki czemu zaznaczony w TextBlocku tekst nie znika przy pollingu.
/// </summary>
public sealed class CommunityChatMessageViewModel : ReactiveObject
{
    private readonly ILocalizationService _loc;
    private CommunityChatAuthor? _author;

    public string AuthorDisplay => _author is { IsComplete: true } author
        ? (string.Equals(_loc.CurrentCulture, "en", StringComparison.OrdinalIgnoreCase)
            ? author.Nickname.En : author.Nickname.Pl)
        : string.Empty;

    public CommunityChatMessageViewModel(CommunityChatMessage message, ILocalizationService loc)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        Id = message.Id;
        UpdateFrom(message);
    }

    /// <summary>Identyfikator nadany przez serwer (klucz deduplikacji).</summary>
    public string Id { get; }

    private string _content = string.Empty;
    /// <summary>Treść wyświetlana jako zwykły tekst (bez HTML/Markdown).</summary>
    public string Content
    {
        get => _content;
        private set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    private DateTimeOffset? _publishedAt;
    public DateTimeOffset? PublishedAt
    {
        get => _publishedAt;
        private set
        {
            this.RaiseAndSetIfChanged(ref _publishedAt, value);
            this.RaisePropertyChanged(nameof(TimeAgoDisplay));
        }
    }

    private bool _isOwn;
    /// <summary>Wiadomość należy do bieżącego użytkownika (potwierdzone przez serwer).</summary>
    public bool IsOwn
    {
        get => _isOwn;
        private set => this.RaiseAndSetIfChanged(ref _isOwn, value);
    }

    private bool _canDelete;
    /// <summary>Serwer potwierdził możliwość usunięcia tej wiadomości.</summary>
    public bool CanDelete
    {
        get => _canDelete;
        private set => this.RaiseAndSetIfChanged(ref _canDelete, value);
    }

    /// <summary>Względny czas publikacji — aktualizowany przy każdym udanym odświeżeniu.</summary>
    public string TimeAgoDisplay => FormatTimeAgo(PublishedAt);

    /// <summary>Aktualizuje pola z nowego snapshotu (serwer może uzupełnić isOwn/canDelete/czas).</summary>
    public void UpdateFrom(CommunityChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_author != message.Author)
        {
            _author = message.Author;
            this.RaisePropertyChanged(nameof(AuthorDisplay));
        }

        if (!string.Equals(Content, message.Content, StringComparison.Ordinal))
            Content = message.Content;

        if (PublishedAt != message.PublishedAt)
            PublishedAt = message.PublishedAt;

        if (IsOwn != message.IsOwn)
            IsOwn = message.IsOwn;

        if (CanDelete != message.CanDelete)
            CanDelete = message.CanDelete;

        // Względny czas "ile temu" odświeża się przy każdym odczycie historii.
        this.RaisePropertyChanged(nameof(TimeAgoDisplay));
    }

    /// <summary>
    /// Aktualizuje wyświetlany względny czas po zmianie języka lub udanym odświeżeniu
    /// (bez naruszania treści — zaznaczony tekst pozostaje).
    /// </summary>
    public void RefreshLocalizedDisplay()
    {
        this.RaisePropertyChanged(nameof(TimeAgoDisplay));
        this.RaisePropertyChanged(nameof(AuthorDisplay));
    }

    private string FormatTimeAgo(DateTimeOffset? publishedAt)
    {
        if (publishedAt is null)
            return _loc.Get("UI.CommunityChat.Time.NoDate");

        var age = DateTimeOffset.UtcNow - publishedAt.Value.ToUniversalTime();
        if (age.TotalMinutes < 1)
            return _loc.Get("UI.CommunityChat.Time.JustNow");

        if (age.TotalHours < 1)
            return _loc.GetFormatted("UI.CommunityChat.Time.MinutesAgo", (int)age.TotalMinutes);

        if (age.TotalDays < 1)
            return _loc.GetFormatted("UI.CommunityChat.Time.HoursAgo", (int)age.TotalHours);

        return _loc.GetFormatted("UI.CommunityChat.Time.DaysAgo", (int)age.TotalDays);
    }
}
