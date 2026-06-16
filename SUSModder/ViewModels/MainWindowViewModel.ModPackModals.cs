using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Views;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    private bool _isModPackCodeEntryVisible;
    private ModPackCodeEntryViewModel? _modPackCodeEntryViewModel;
    private TaskCompletionSource<string?>? _modPackCodeEntryCompletionSource;

    private bool _isModPackResultVisible;
    private ModPackResultViewModel? _modPackResultViewModel;
    private TaskCompletionSource<bool>? _modPackResultCompletionSource;

    private bool _isModPackPreviewVisible;
    private ModPackPreviewViewModel? _modPackPreviewViewModel;
    private TaskCompletionSource<bool>? _modPackPreviewCompletionSource;

    private bool _isModPackCreatorVisible;
    private Control? _modPackCreatorContent;
    private string _modPackCreatorTitle = string.Empty;
    private TaskCompletionSource<ModPackCreatorDialogResult?>? _modPackCreatorCompletionSource;

    public bool IsModPackCodeEntryVisible
    {
        get => _isModPackCodeEntryVisible;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isModPackCodeEntryVisible, value);
            NotifyToolModalStateChanged();
        }
    }

    public ModPackCodeEntryViewModel? ModPackCodeEntryViewModel
    {
        get => _modPackCodeEntryViewModel;
        private set => this.RaiseAndSetIfChanged(ref _modPackCodeEntryViewModel, value);
    }

    public bool IsModPackResultVisible
    {
        get => _isModPackResultVisible;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isModPackResultVisible, value);
            NotifyToolModalStateChanged();
        }
    }

    public ModPackResultViewModel? ModPackResultViewModel
    {
        get => _modPackResultViewModel;
        private set => this.RaiseAndSetIfChanged(ref _modPackResultViewModel, value);
    }

    public bool IsModPackPreviewVisible
    {
        get => _isModPackPreviewVisible;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isModPackPreviewVisible, value);
            NotifyToolModalStateChanged();
        }
    }

    public ModPackPreviewViewModel? ModPackPreviewViewModel
    {
        get => _modPackPreviewViewModel;
        private set => this.RaiseAndSetIfChanged(ref _modPackPreviewViewModel, value);
    }

    public bool IsModPackCreatorVisible
    {
        get => _isModPackCreatorVisible;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isModPackCreatorVisible, value);
            NotifyToolModalStateChanged();
        }
    }

    public Control? ModPackCreatorContent
    {
        get => _modPackCreatorContent;
        private set => this.RaiseAndSetIfChanged(ref _modPackCreatorContent, value);
    }

    public string ModPackCreatorTitle
    {
        get => _modPackCreatorTitle;
        private set => this.RaiseAndSetIfChanged(ref _modPackCreatorTitle, value);
    }

    private async Task<string?> ShowModPackCodeEntryModalAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(ShowModPackCodeEntryModalAsync);

        var completionSource = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _modPackCodeEntryCompletionSource = completionSource;

        var vm = new ModPackCodeEntryViewModel(_localizationService);
        vm.Completed += OnModPackCodeEntryCompleted;
        ModPackCodeEntryViewModel = vm;
        IsModPackCodeEntryVisible = true;

        return await completionSource.Task;
    }

    private void OnModPackCodeEntryCompleted(object? sender, string? code)
    {
        if (ModPackCodeEntryViewModel != null)
            ModPackCodeEntryViewModel.Completed -= OnModPackCodeEntryCompleted;

        ModPackCodeEntryViewModel = null;
        IsModPackCodeEntryVisible = false;
        _modPackCodeEntryCompletionSource?.TrySetResult(code);
        _modPackCodeEntryCompletionSource = null;
    }

    private async Task ShowModPackResultModalAsync(string packCode, string? shareUrl)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => ShowModPackResultModalAsync(packCode, shareUrl));
            return;
        }

        var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _modPackResultCompletionSource = completionSource;

        var vm = new ModPackResultViewModel(
            packCode,
            shareUrl,
            _localizationService.Get("ModPacks.PackCodeLabel"),
            _localizationService.Get("ModPacks.ShareLinkLabel"),
            _localizationService.Get("ModPacks.CopyCode"),
            _localizationService.Get("ModPacks.CopyLink"),
            _localizationService.Get("UI.Buttons.Close"));
        vm.CloseRequested += OnModPackResultCloseRequested;
        ModPackResultViewModel = vm;
        IsModPackResultVisible = true;

        await completionSource.Task;
    }

    private void OnModPackResultCloseRequested(object? sender, EventArgs e)
    {
        if (ModPackResultViewModel != null)
            ModPackResultViewModel.CloseRequested -= OnModPackResultCloseRequested;

        ModPackResultViewModel = null;
        IsModPackResultVisible = false;
        _modPackResultCompletionSource?.TrySetResult(true);
        _modPackResultCompletionSource = null;
    }

    private async Task<(bool confirmed, string? displayName)> ShowModPackPreviewModalAsync(
        ModPack pack,
        IModPackService modPackService)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ShowModPackPreviewModalAsync(pack, modPackService));

        var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _modPackPreviewCompletionSource = completionSource;

        var vm = new ModPackPreviewViewModel(pack, modPackService, _localizationService);
        vm.Completed += OnModPackPreviewCompleted;
        ModPackPreviewViewModel = vm;
        IsModPackPreviewVisible = true;

        var confirmed = await completionSource.Task;
        var displayName = confirmed ? vm.ResolvedLocalDisplayName : null;
        return (confirmed, displayName);
    }

    private void OnModPackPreviewCompleted(object? sender, bool confirmed)
    {
        if (ModPackPreviewViewModel != null)
            ModPackPreviewViewModel.Completed -= OnModPackPreviewCompleted;

        ModPackPreviewViewModel = null;
        IsModPackPreviewVisible = false;
        _modPackPreviewCompletionSource?.TrySetResult(confirmed);
        _modPackPreviewCompletionSource = null;
    }

    private async Task<ModPackCreatorDialogResult?> ShowModPackCreatorModalAsync(ModPackCreatorMode mode)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ShowModPackCreatorModalAsync(mode));

        var settings = _userSettingsService.LoadUserSettings();
        var platform = settings.Mode ?? "steam";
        string? preselectedInstance = null;
        int? preselectedCatalogMod = null;

        if (mode == ModPackCreatorMode.ShareExisting)
        {
            preselectedInstance = SelectedPackInstance?.InstanceId;
            preselectedCatalogMod = preselectedInstance == null ? SelectedMod?.Id : null;
        }
        else
        {
            preselectedCatalogMod = SelectedMod?.Id;
        }

        var completionSource = new TaskCompletionSource<ModPackCreatorDialogResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _modPackCreatorCompletionSource = completionSource;

        var view = new ModPackCreatorView(
            mode,
            App.GetService<IModPackService>(),
            _dllModificationService,
            App.GetService<IModInstanceRepository>(),
            new InstanceToModPackMapper(App.GetService<IModInstanceRepository>()),
            _configuration ?? App.GetService<Microsoft.Extensions.Configuration.IConfiguration>(),
            _diagnosticsOutput ?? throw new InvalidOperationException("Diagnostics output not initialized."),
            _localizationService,
            platform,
            App.GetService<ModInstanceInstaller>(),
            preselectedInstance,
            preselectedCatalogMod);

        view.Completed += OnModPackCreatorCompleted;
        ModPackCreatorTitle = view.ModalTitle;
        ModPackCreatorContent = view;
        IsModPackCreatorVisible = true;

        return await completionSource.Task;
    }

    private void OnModPackCreatorCompleted(ModPackCreatorDialogResult? result)
    {
        if (ModPackCreatorContent is ModPackCreatorView view)
            view.Completed -= OnModPackCreatorCompleted;

        ModPackCreatorContent = null;
        ModPackCreatorTitle = string.Empty;
        IsModPackCreatorVisible = false;
        _modPackCreatorCompletionSource?.TrySetResult(result);
        _modPackCreatorCompletionSource = null;
    }

    private void DismissActiveModPackModal()
    {
        if (IsModPackCodeEntryVisible)
            OnModPackCodeEntryCompleted(null, null);
        if (IsModPackResultVisible)
            OnModPackResultCloseRequested(null, EventArgs.Empty);
        if (IsModPackPreviewVisible)
            OnModPackPreviewCompleted(null, false);
        if (IsModPackCreatorVisible)
            OnModPackCreatorCompleted(null);
    }
}
