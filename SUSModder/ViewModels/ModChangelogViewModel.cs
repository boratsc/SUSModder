using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels;

public enum ModChangelogState
{
    Loading,
    Success,
    Empty,
    Error
}

public sealed class ChangelogEntryItem
{
    public string Version { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string? ReleaseUrl { get; init; }
    public string? PublishedAtFormatted { get; init; }

    public bool HasReleaseUrl => !string.IsNullOrWhiteSpace(ReleaseUrl);
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
}

public class ModChangelogViewModel : ViewModelBase
{
    private ModChangelogState _state = ModChangelogState.Loading;
    private string _statusMessage = string.Empty;
    private string _fallbackLanguageNotice = string.Empty;
    private bool _hasFallbackLanguage;

    private readonly ModChangelogService _changelogService;
    private readonly ILocalizationService _localizationService;
    private readonly int _modId;
    private readonly string _lang;
    private readonly int _limit;

    public event EventHandler? CloseRequested;

    public ModChangelogState State
    {
        get => _state;
        set => this.RaiseAndSetIfChanged(ref _state, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public bool ShowFallbackNotice
    {
        get => _hasFallbackLanguage;
        set => this.RaiseAndSetIfChanged(ref _hasFallbackLanguage, value);
    }

    public string FallbackLanguageNotice
    {
        get => _fallbackLanguageNotice;
        set => this.RaiseAndSetIfChanged(ref _fallbackLanguageNotice, value);
    }

    public string WindowTitle { get; }
    public string OpenReleaseText { get; }
    public ObservableCollection<ChangelogEntryItem> Entries { get; } = [];

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public ModChangelogViewModel(
        ModChangelogService changelogService,
        ILocalizationService localizationService,
        int modId,
        string modName,
        string lang,
        int limit = 5)
    {
        _changelogService = changelogService;
        _localizationService = localizationService;
        _modId = modId;
        _lang = lang;
        _limit = limit;

        WindowTitle = localizationService.GetFormatted("ModChangelog.WindowTitle", modName);
        OpenReleaseText = localizationService.Get("ModChangelog.OpenRelease");
        StatusMessage = localizationService.Get("ModChangelog.Loading");

        CloseCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public async Task LoadChangelogAsync(CancellationToken cancellationToken = default)
    {
        State = ModChangelogState.Loading;
        StatusMessage = _localizationService.Get("ModChangelog.Loading");

        try
        {
            var result = await _changelogService.GetChangelogAsync(
                _modId, _lang, _limit, cancellationToken);

            if (result.ErrorCode is not null)
            {
                State = ModChangelogState.Error;
                StatusMessage = _localizationService.Get("ModChangelog.Error");
                return;
            }

            var entryList = result.Entries
                .Select(e => new ChangelogEntryItem
                {
                    Version = e.Version,
                    ReleaseName = e.ReleaseName,
                    Body = e.Body,
                    ReleaseUrl = e.ReleaseUrl,
                    PublishedAtFormatted = e.PublishedAt?.ToString("d")
                })
                .ToList();

            Entries.Clear();
            foreach (var entry in entryList)
            {
                Entries.Add(entry);
            }

            if (entryList.Count == 0)
            {
                State = ModChangelogState.Empty;
                StatusMessage = _localizationService.Get("ModChangelog.Empty");
            }
            else
            {
                State = ModChangelogState.Success;

                var hasFallback = result.Entries.Any(e => !string.IsNullOrWhiteSpace(e.FallbackLanguage));
                if (hasFallback)
                {
                    FallbackLanguageNotice = _localizationService.Get(
                        "ModChangelog.FallbackLanguageNotice");
                    ShowFallbackNotice = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // cancelled
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModChangelogVM] Unexpected error: {ex.Message}");
            State = ModChangelogState.Error;
            StatusMessage = _localizationService.Get("ModChangelog.Error");
        }
    }
}
