using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;
using SUSModder.Views;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    private MainWindowViewModel? _modPackDeepLinkTarget;

    /// <summary>
    /// Uruchamia nasłuch IPC (druga instancja z linkiem) — wywołaj po inicjalizacji VM.
    /// </summary>
    public void StartModPackDeepLinkServer()
    {
        _ = EnsureModPackProtocolRegisteredAsync();
        _modPackDeepLinkTarget = this;
        DeepLinkIpc.StartServer((code, auto) =>
        {
            var vm = _modPackDeepLinkTarget;
            if (vm == null) return;

            Dispatcher.UIThread.Post(async () =>
            {
                await vm.HandlePendingModPackDeepLinkAsync(code, auto);
            });
        });
    }

    /// <summary>
    /// Wywoływane po starcie aplikacji (deep link susmodder://pack/...).
    /// </summary>
    public async Task HandlePendingModPackDeepLinkAsync(string packCode, bool autoInstall = false)
    {
        // Poczekaj aż główne okno i lista modów będą gotowe
        for (var i = 0; i < 50; i++)
        {
            if (GetMainWindow()?.IsVisible == true && Mods.Count > 0)
                break;
            await Task.Delay(100);
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            IsPaneOpen = false;
            var window = GetMainWindow();
            window?.Activate();
            await OpenModPackFlowAsync(packCode, autoInstall);
        });
    }

    private async Task ShowModPackCodeEntryAsync()
    {
        IsPaneOpen = false;
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var dialog = new ModPackCodeEntryDialog();
        var code = await dialog.ShowDialog<string?>(mainWindow);
        if (!string.IsNullOrEmpty(code))
            await OpenModPackFlowAsync(code, false);
    }

    private async Task ShowModPackCreatorAsync()
    {
        IsPaneOpen = false;
        var settings = _userSettingsService.LoadUserSettings();
        if (!settings.ModPacksEnabled)
        {
            await ShowMessageAsync(_localizationService.Get("ModPacks.Disabled"), _localizationService.Get("ModPacks.CreatorTitle"));
            return;
        }

        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var modPackService = App.GetService<IModPackService>();
        var platform = settings.Mode ?? "steam";
        int? preselect = SelectedMod?.Id;

        var dialog = new ModPackCreatorDialog(
            modPackService,
            _dllModificationService,
            _configuration ?? App.GetService<Microsoft.Extensions.Configuration.IConfiguration>(),
            _diagnosticsOutput ?? throw new InvalidOperationException("Diagnostics output not initialized."),
            _localizationService,
            platform,
            preselect);

        var result = await dialog.ShowDialog<ModPackCreateResult?>(mainWindow);
        if (result?.Success == true && !string.IsNullOrEmpty(result.PackCode))
        {
            var resultDialog = new ModPackResultDialog(result.PackCode, result.ShareUrl);
            await resultDialog.ShowDialog(mainWindow);

            await EnsureModPackProtocolRegisteredAsync();
        }
    }

    private async Task OpenModPackFlowAsync(string packCode, bool autoInstall)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var modPackService = App.GetService<IModPackService>();
        ModPack? pack;
        try
        {
            pack = await modPackService.GetPackAsync(packCode);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(ex.Message, _localizationService.Get("ModPacks.PreviewTitle"));
            return;
        }

        if (pack == null)
        {
            await ShowMessageAsync(
                _localizationService.Get("ModPacks.PackNotFound"),
                _localizationService.Get("ModPacks.PreviewTitle"));
            return;
        }

        var settings = _userSettingsService.LoadUserSettings();
        if (autoInstall && settings.ModPacksAutoInstall)
        {
            var quickValidation = modPackService.ValidatePack(pack, !pack.HasExternalDlls);
            if (quickValidation.IsValid)
            {
                await InstallModPackAsync(pack, mainWindow);
                return;
            }
        }

        var preview = new ModPackPreviewDialog(pack, modPackService, _localizationService);
        var confirmed = await preview.ShowDialog<bool>(mainWindow);
        if (confirmed)
            await InstallModPackAsync(pack, mainWindow);
    }

    private async Task InstallModPackAsync(ModPack pack, Window mainWindow)
    {
        var settings = _userSettingsService.LoadUserSettings();
        var platform = string.IsNullOrEmpty(settings.Mode) ? "steam" : settings.Mode;

        var packTitle = string.IsNullOrWhiteSpace(pack.ModName) ? pack.PackCode : pack.ModName;
        ModPackInstallResult? installResult = null;
        UpdateProgressDialog? progressDialog = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            progressDialog = new UpdateProgressDialog(packTitle);
            progressDialog.Show(mainWindow);
        });

        try
        {
            installResult = await Task.Run(async () =>
            {
                var configService = new ConfigService();
                var installer = new ModPackInstaller(
                    _configuration ?? App.GetService<Microsoft.Extensions.Configuration.IConfiguration>(),
                    configService,
                    _dllModificationService,
                    _diagnosticsOutput ?? throw new InvalidOperationException("Diagnostics output not initialized."));

                var progress = new Progress<(int percent, string message)>(p =>
                {
                    if (progressDialog == null) return;
                    progressDialog.UpdateProgress(p.percent, p.message);
                });

                return await installer.InstallPackAsync(pack, platform, progress);
            });
        }
        finally
        {
            if (progressDialog != null && progressDialog.IsVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() => progressDialog.Close());
            }
        }

        var result = installResult ?? new ModPackInstallResult { Success = false, ErrorMessage = _localizationService.Get("ModPacks.InstallFailed") };

        string message;
        if (!result.Success)
            message = result.ErrorMessage ?? _localizationService.Get("ModPacks.InstallFailed");
        else if (result.IsPartial)
            message = _localizationService.Get("ModPacks.InstallPartial");
        else
            message = _localizationService.Get("ModPacks.InstallSuccess");

        await ShowMessageAsync(_localizationService.Get("ModPacks.PreviewTitle"), message);
        await RefreshModsListAsync();
    }

    private static async Task EnsureModPackProtocolRegisteredAsync()
    {
        if (!OperatingSystem.IsWindows() || DeepLinkService.IsProtocolRegistered())
            return;

        try
        {
            var exe = System.IO.Path.Combine(
                SUSModder.Core.Utilities.ApplicationPaths.GetApplicationDirectory(),
                "SUSModder.exe");
            if (System.IO.File.Exists(exe))
                await new DeepLinkService().RegisterProtocolHandlerAsync(exe);
        }
        catch
        {
            // Rejestracja HKCU jest opcjonalna
        }
    }

    private Window? GetMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
