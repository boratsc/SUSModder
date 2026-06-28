using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels;

/// <summary>
/// Reprezentacja paczki udostępnionej przez użytkownika na serwerze.
/// Nie jest tożsama z lokalną instancją — pokazuje tylko metadane
/// paczki/kodu z <c>GET /api/v2/modpacks?creatorHash=...</c>.
/// </summary>
public sealed class SharedModPackItem : ViewModelBase
{
    public SharedModPackItem(ModPackListEntry entry, ILocalizationService loc)
    {
        Entry = entry;
        _modName = string.IsNullOrEmpty(entry.ModName)
            ? loc.Get("ModPacks.Shared.UnnamedPack")
            : entry.ModName;
        _packCode = entry.PackCode;
        _shareUrl = $"https://susmodder.app/pack/{entry.PackCode}";
        _statusText = ComputeStatusText(entry, loc);
        _expiresAtText = entry.ExpiresAt.HasValue
            ? entry.ExpiresAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : loc.Get("ModPacks.Shared.UnknownExpiry");
    }

    public ModPackListEntry Entry { get; }

    public string PackCode => _packCode;
    public string ShareUrl => _shareUrl;
    public string ModName => _modName;
    public string StatusText => _statusText;
    public string ExpiresAtText => _expiresAtText;
    public int TtlDays => Entry.TtlDays;
    public int DllCount => Entry.DllCount;
    public int ExternalDllCount => Entry.ExternalDllCount;
    public string VtStatus => Entry.VtStatus;
    public string FullModVersion => Entry.FullModVersion;

    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public event EventHandler<string>? CopyRequested;
    public event EventHandler? DeleteRequested;

    public void RaiseCopy() => CopyRequested?.Invoke(this, PackCode);
    public void RaiseCopyLink() => CopyRequested?.Invoke(this, ShareUrl);
    public void RaiseDelete() => DeleteRequested?.Invoke(this, EventArgs.Empty);

    private static string ComputeStatusText(ModPackListEntry entry, ILocalizationService loc)
    {
        var vtKey = entry.VtStatus?.ToLowerInvariant() switch
        {
            "clean" => "ModPacks.Shared.VtClean",
            "pending" => "ModPacks.Shared.VtPending",
            "suspicious" => "ModPacks.Shared.VtSuspicious",
            _ => "ModPacks.Shared.VtUnknown"
        };
        return loc.Get(vtKey);
    }

    private readonly string _packCode;
    private readonly string _shareUrl;
    private readonly string _modName;
    private readonly string _statusText;
    private readonly string _expiresAtText;
    private bool _isBusy;
}

/// <summary>
/// ViewModel odpowiedzialny za zarządzanie paczkami udostępnionymi przez użytkownika
/// (lista, kopiowanie, usuwanie). Wykorzystywany przez <c>MainWindowViewModel</c>
/// jako źródło dla zakładki "Udostępnione" oraz CTA z błędu <c>PACK_LIMIT_REACHED</c>.
/// </summary>
public sealed class SharedModPacksViewModel : ViewModelBase
{
    private readonly IModPackService _modPackService;
    private readonly ILocalizationService _loc;
    private readonly Func<string, string, string, string?, string?, Task<bool>> _confirmAsync;
    private readonly Func<string, string, Task> _showMessageAsync;
    private readonly Func<string, string, Task> _showErrorAsync;
    private readonly Action<Action> _runOnUi;
    private readonly Action? _onClose;

    public SharedModPacksViewModel(
        IModPackService modPackService,
        ILocalizationService loc,
        Func<string, string, string, string?, string?, Task<bool>> confirmAsync,
        Func<string, string, Task> showMessageAsync,
        Func<string, string, Task> showErrorAsync,
        Action<Action> runOnUi,
        Action? onClose = null)
    {
        _modPackService = modPackService;
        _loc = loc;
        _confirmAsync = confirmAsync;
        _showMessageAsync = showMessageAsync;
        _showErrorAsync = showErrorAsync;
        _runOnUi = runOnUi;
        _onClose = onClose;

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        CopyCodeCommand = ReactiveCommand.Create<SharedModPackItem>(item => item?.RaiseCopy());
        CopyLinkCommand = ReactiveCommand.Create<SharedModPackItem>(item => item?.RaiseCopyLink());
        DeleteCommand = ReactiveCommand.CreateFromTask<SharedModPackItem>(DeleteAsync);
        CloseCommand = ReactiveCommand.Create(() => _onClose?.Invoke());
    }

    public ObservableCollection<SharedModPackItem> Packs { get; } = new();

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isLoading, value);
            this.RaisePropertyChanged(nameof(IsNotLoading));
        }
    }

    public bool IsNotLoading => !IsLoading;

    private int _activeCount;
    public int ActiveCount
    {
        get => _activeCount;
        private set
        {
            this.RaiseAndSetIfChanged(ref _activeCount, value);
            this.RaisePropertyChanged(nameof(HasPacks));
            this.RaisePropertyChanged(nameof(HeaderText));
            this.RaisePropertyChanged(nameof(ShowSharedPacksEmptyState));
            this.RaisePropertyChanged(nameof(IsSharedPacksGridVisible));
        }
    }

    private int _maxAllowed = 10;
    public int MaxAllowed
    {
        get => _maxAllowed;
        private set
        {
            this.RaiseAndSetIfChanged(ref _maxAllowed, value);
            this.RaisePropertyChanged(nameof(HeaderText));
        }
    }

    public bool HasPacks => ActiveCount > 0;
    public bool IsAtLimit => MaxAllowed > 0 && ActiveCount >= MaxAllowed;

    public bool IsSharedPacksGridVisible => !IsLoading;
    public bool ShowSharedPacksEmptyState => !IsLoading && ActiveCount == 0;

    public string HeaderText =>
        string.Format(_loc.Get("ModPacks.Shared.HeaderCount"), ActiveCount, MaxAllowed);

    private string? _errorText;
    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorText, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorText);

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<SharedModPackItem, Unit> CopyCodeCommand { get; }
    public ReactiveCommand<SharedModPackItem, Unit> CopyLinkCommand { get; }
    public ReactiveCommand<SharedModPackItem, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public async Task RefreshAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        ErrorText = null;
        try
        {
            var result = await _modPackService.ListOwnPacksDetailedAsync();
            _runOnUi(() =>
            {
                Packs.Clear();
                if (!result.Success)
                {
                    ErrorText = result.ErrorCode switch
                    {
                        "NOT_FOUND" => _loc.Get("ModPacks.Shared.ListNotAvailable"),
                        "NETWORK_ERROR" => _loc.Get("ModPacks.Shared.NetworkError"),
                        _ => string.Format(
                            _loc.Get("ModPacks.Shared.LoadFailed"),
                            result.ErrorMessage ?? result.ErrorCode ?? _loc.Get("ModPacks.Shared.UnknownError"))
                    };
                    return;
                }

                foreach (var entry in result.Packs)
                    Packs.Add(new SharedModPackItem(entry, _loc));

                ActiveCount = result.ActiveCount > 0 ? result.ActiveCount : Packs.Count;
                MaxAllowed = result.MaxAllowed > 0 ? result.MaxAllowed : 10;
            });
        }
        catch (OperationCanceledException)
        {
            // OK — zdarza się przy szybkim zamykaniu / przełączaniu
        }
        catch (Exception ex)
        {
            _runOnUi(() =>
            {
                ErrorText = string.Format(_loc.Get("ModPacks.Shared.LoadFailed"), ex.Message);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DeleteAsync(SharedModPackItem? item)
    {
        if (item == null || item.IsBusy)
            return;

        var title = _loc.Get("ModPacks.Shared.DeleteTitle");
        var yesText = _loc.Get("UI.Buttons.Delete");
        var noText = _loc.Get("UI.Buttons.Cancel");

        var confirmed = await _confirmAsync(
            title,
            string.Format(_loc.Get("ModPacks.Shared.DeleteConfirm"), item.PackCode, item.ModName),
            yesText, noText, null);

        if (!confirmed)
            return;

        _runOnUi(() => item.IsBusy = true);
        try
        {
            var result = await _modPackService.DeletePackDetailedAsync(item.PackCode);
            if (result.Success)
            {
                _runOnUi(() =>
                {
                    Packs.Remove(item);
                    ActiveCount = Math.Max(0, ActiveCount - 1);
                });
                await _showMessageAsync(title, _loc.Get("ModPacks.Shared.DeleteSuccess"));
            }
            else
            {
                var msg = result.ErrorCode switch
                {
                    "NOT_PACK_OWNER" => _loc.Get("ModPacks.Shared.DeleteNotOwner"),
                    "PACK_NOT_FOUND" => _loc.Get("ModPacks.Shared.DeleteNotFound"),
                    "NETWORK_ERROR" => _loc.Get("ModPacks.Shared.NetworkError"),
                    _ => string.Format(
                        _loc.Get("ModPacks.Shared.DeleteFailed"),
                        result.ErrorMessage ?? result.ErrorCode ?? _loc.Get("ModPacks.Shared.UnknownError"))
                };
                await _showErrorAsync(msg, title);
            }
        }
        catch (OperationCanceledException)
        {
            // OK — zdarza się przy szybkim zamykaniu dialogu
        }
        catch (Exception ex)
        {
            await _showErrorAsync(ex.Message, title);
        }
        finally
        {
            _runOnUi(() => item.IsBusy = false);
        }
    }
}
