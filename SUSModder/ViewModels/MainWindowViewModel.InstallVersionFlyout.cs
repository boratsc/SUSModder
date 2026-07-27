using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    private readonly ObservableCollection<ModVersionHistory> _installVersionFlyoutVersions = new();
    private CancellationTokenSource? _installVersionFlyoutCts;
    private int _installVersionFlyoutLoadedModId = -1;
    private bool _isInstallVersionFlyoutLoading;
    private bool _installVersionFlyoutHasError;
    private string _installVersionFlyoutError = string.Empty;

    public ObservableCollection<ModVersionHistory> InstallVersionFlyoutVersions => _installVersionFlyoutVersions;

    public bool IsInstallVersionFlyoutLoading
    {
        get => _isInstallVersionFlyoutLoading;
        private set => this.RaiseAndSetIfChanged(ref _isInstallVersionFlyoutLoading, value);
    }

    public bool InstallVersionFlyoutHasError
    {
        get => _installVersionFlyoutHasError;
        private set => this.RaiseAndSetIfChanged(ref _installVersionFlyoutHasError, value);
    }

    public string InstallVersionFlyoutError
    {
        get => _installVersionFlyoutError;
        private set => this.RaiseAndSetIfChanged(ref _installVersionFlyoutError, value);
    }

    public bool HasInstallVersionFlyoutVersions => _installVersionFlyoutVersions.Count > 0;

    public ModVersionHistory? InstallVersionFlyoutLatest =>
        _installVersionFlyoutVersions.Count > 0 ? _installVersionFlyoutVersions[0] : null;

    /// <summary>
    /// Ładuje historię wersji do flyoutu SplitButton (cache per mod Id).
    /// </summary>
    public async Task EnsureInstallVersionFlyoutLoadedAsync()
    {
        var mod = SelectedMod;
        if (mod == null || mod.IsVanilla || !string.IsNullOrEmpty(mod.InstallPath))
            return;

        if (_installVersionFlyoutLoadedModId == mod.Id
            && (_installVersionFlyoutVersions.Count > 0 || InstallVersionFlyoutHasError)
            && !IsInstallVersionFlyoutLoading)
        {
            return;
        }

        _installVersionFlyoutCts?.Cancel();
        _installVersionFlyoutCts = new CancellationTokenSource();
        var token = _installVersionFlyoutCts.Token;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _installVersionFlyoutVersions.Clear();
            _installVersionFlyoutLoadedModId = mod.Id;
            IsInstallVersionFlyoutLoading = true;
            InstallVersionFlyoutHasError = false;
            InstallVersionFlyoutError = string.Empty;
            this.RaisePropertyChanged(nameof(HasInstallVersionFlyoutVersions));
        });

        try
        {
            var diagnostics = new UIDiagnosticsOutput(msg => System.Diagnostics.Debug.WriteLine(msg));
            var versionService = new ModVersionService(diagnostics);
            var versions = await versionService.GetVersionHistoryAsync(mod.Id);

            if (token.IsCancellationRequested || SelectedMod?.Id != mod.Id)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _installVersionFlyoutVersions.Clear();
                foreach (var v in versions)
                    _installVersionFlyoutVersions.Add(v);

                if (versions.Count == 0)
                {
                    InstallVersionFlyoutHasError = true;
                    InstallVersionFlyoutError = _localizationService.Get("VersionSelection.NoVersionsAvailable");
                }

                this.RaisePropertyChanged(nameof(HasInstallVersionFlyoutVersions));
                this.RaisePropertyChanged(nameof(InstallVersionFlyoutLatest));
            });
        }
        catch (OperationCanceledException)
        {
            // przełączenie moda
        }
        catch (TimeoutException)
        {
            if (token.IsCancellationRequested || SelectedMod?.Id != mod.Id)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                InstallVersionFlyoutHasError = true;
                InstallVersionFlyoutError = _localizationService.Get("VersionSelection.TimeoutError");
            });
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || SelectedMod?.Id != mod.Id)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                InstallVersionFlyoutHasError = true;
                InstallVersionFlyoutError = _localizationService.GetFormatted("VersionSelection.FetchError", ex.Message);
            });
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsInstallVersionFlyoutLoading = false;
                });
            }
        }
    }

    public void ClearInstallVersionFlyout()
    {
        _installVersionFlyoutCts?.Cancel();
        _installVersionFlyoutLoadedModId = -1;
        _installVersionFlyoutVersions.Clear();
        IsInstallVersionFlyoutLoading = false;
        InstallVersionFlyoutHasError = false;
        InstallVersionFlyoutError = string.Empty;
        this.RaisePropertyChanged(nameof(HasInstallVersionFlyoutVersions));
        this.RaisePropertyChanged(nameof(InstallVersionFlyoutLatest));
    }

    /// <summary>
    /// Instaluje wybraną wersję z flyoutu (bez modala).
    /// </summary>
    public async Task InstallVersionFromFlyoutAsync(ModVersionHistory selectedVersion)
    {
        if (_isInitializing || SelectedMod == null || SelectedMod.IsInstalling || selectedVersion == null)
            return;

        try
        {
            var configService = new ConfigService();
            var allConfigs = configService.LoadConfig();
            var modConfig = allConfigs.FirstOrDefault(c => c.ModName == SelectedMod.Name);

            if (modConfig == null)
            {
                await _userInteractionService.ShowErrorAsync(
                    _localizationService.Get("ModOperations.ConfigNotFound"),
                    _localizationService.Get("MainWindow.ErrorTitle"));
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[InstallVersionFlyout] Wybrano wersję: {selectedVersion.ModVersion}");

            await InstallSpecificVersionAsync(SelectedMod, modConfig, selectedVersion, allConfigs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InstallVersionFlyout] Błąd: {ex.Message}");
            await _userInteractionService.ShowErrorAsync(
                _localizationService.GetFormatted("ModOperations.InstallError", ex.Message),
                _localizationService.Get("MainWindow.ErrorTitle"));
        }
    }
}
