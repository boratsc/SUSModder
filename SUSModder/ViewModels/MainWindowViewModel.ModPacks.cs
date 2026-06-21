using System;

using System.Linq;

using System.Threading.Tasks;

using Avalonia.Controls;

using Avalonia.Threading;

using SUSModder.Core.Configuration;

using SUSModder.Core.Data;

using SUSModder.Core.GameIntegration;

using SUSModder.Core.Models;

using SUSModder.Core.Services;

using SUSModder.Views;



namespace SUSModder.ViewModels;



public partial class MainWindowViewModel

{

    private MainWindowViewModel? _modPackDeepLinkTarget;



    public void StartModPackDeepLinkServer()

    {

        _ = EnsureModPackProtocolRegisteredAsync();

        _modPackDeepLinkTarget = this;

        DeepLinkIpc.StartServer(

            onDeepLinkReceived: (code, auto) =>

            {

                var vm = _modPackDeepLinkTarget;

                if (vm == null) return;



                Dispatcher.UIThread.Post(async () =>

                {

                    await vm.HandlePendingModPackDeepLinkAsync(code, auto);

                });

            },

            onActivateRequested: () =>

            {

                var vm = _modPackDeepLinkTarget;

                if (vm == null) return;



                Dispatcher.UIThread.Post(() =>

                {

                    var mainWindow = vm.GetMainWindow() as Views.MainWindow;

                    mainWindow?.RestoreAndActivate();

                });

            });

    }



    public async Task HandlePendingModPackDeepLinkAsync(string packCode, bool autoInstall = false)

    {

        for (var i = 0; i < 50; i++)

        {

            if (GetMainWindow()?.IsVisible == true && Mods.Count > 0)

                break;

            await Task.Delay(100);

        }



        await Dispatcher.UIThread.InvokeAsync(async () =>

        {

            IsPaneOpen = false;

            GetMainWindow()?.Activate();

            await OpenModPackFlowAsync(packCode, autoInstall);

        });

    }



    private async Task ShowModPackCodeEntryAsync()

    {

        IsPaneOpen = false;

        var code = await ShowModPackCodeEntryModalAsync();

        if (!string.IsNullOrEmpty(code))

            await OpenModPackFlowAsync(code, false);

    }



    private async Task ShowCreateLocalPackAsync() =>

        await ShowModPackCreatorDialogAsync(ModPackCreatorMode.InstallLocal);



    private async Task ShowShareExistingPackAsync() =>
    await ShowModPackCreatorDialogAsync(ModPackCreatorMode.ShareExisting);

private async Task ShowCreateAndSharePackAsync() =>
    await ShowModPackCreatorDialogAsync(ModPackCreatorMode.CreateAndShare);

private async Task ShowModPackCreatorDialogAsync(ModPackCreatorMode mode)

    {

        IsPaneOpen = false;

        var settings = _userSettingsService.LoadUserSettings();



        if ((mode == ModPackCreatorMode.ShareExisting || mode == ModPackCreatorMode.CreateAndShare) &&
            !settings.ModPacksEnabled)

        {

            await ShowMessageAsync(_localizationService.Get("ModPacks.Disabled"), _localizationService.Get("ModPacks.CreatorTitle"));

            return;

        }



        var result = await ShowModPackCreatorModalAsync(mode);

        if (result == null)

            return;



        if (result.Mode == ModPackCreatorMode.CreateAndShare && !string.IsNullOrEmpty(result.CreatedInstanceId))

        {

            await RefreshPackInstancesAsync();

            ActiveBrowserTab = ModBrowserTab.MyPacks;

            SelectedPackInstance = PackInstances.FirstOrDefault(p => p.InstanceId == result.CreatedInstanceId);

        }



        if ((result.Mode == ModPackCreatorMode.ShareExisting || result.Mode == ModPackCreatorMode.CreateAndShare) &&

            result.ShareResult?.Success == true &&

            !string.IsNullOrEmpty(result.ShareResult.PackCode))

        {

            await ShowModPackResultModalAsync(result.ShareResult.PackCode, result.ShareResult.ShareUrl);

            await EnsureModPackProtocolRegisteredAsync();

            return;

        }



        if (result.Mode == ModPackCreatorMode.InstallLocal && !string.IsNullOrEmpty(result.CreatedInstanceId))

        {

            await RefreshPackInstancesAsync();

            ActiveBrowserTab = ModBrowserTab.MyPacks;

            SelectedPackInstance = PackInstances.FirstOrDefault(p => p.InstanceId == result.CreatedInstanceId);

            var message = result.FailedDllNames.Count > 0

                ? string.Format(

                    _localizationService.Get("UI.Packs.CreateLocalPartial"),

                    string.Join(", ", result.FailedDllNames))

                : _localizationService.Get("UI.Packs.CreateLocalSuccess");

            await ShowMessageAsync(

                message,

                _localizationService.Get("UI.Packs.CreateLocalTitle"));

        }

    }



    private async Task OpenModPackFlowAsync(string packCode, bool autoInstall)

    {

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

                var catalog = ConfigManager.LoadConfig();

                var defaultName = !string.IsNullOrWhiteSpace(pack.ModName)

                    ? pack.ModName

                    : catalog.FirstOrDefault(c => c.Id == pack.FullMod?.Id)?.ModName ?? pack.PackCode;

                SetPendingModPackDisplayName(defaultName);

                await InstallModPackAsync(pack);

                SetPendingModPackDisplayName(null);

                return;

            }

        }



        var (confirmed, displayName) = await ShowModPackPreviewModalAsync(pack, modPackService);

        if (confirmed)

        {

            SetPendingModPackDisplayName(displayName);

            await InstallModPackAsync(pack);

            SetPendingModPackDisplayName(null);

        }

    }



    private async Task InstallModPackAsync(ModPack pack)

    {

        var settings = _userSettingsService.LoadUserSettings();

        var platform = string.IsNullOrEmpty(settings.Mode) ? "steam" : settings.Mode;

        var mainWindow = GetMainWindow();



        var packTitle = string.IsNullOrWhiteSpace(pack.ModName) ? pack.PackCode : pack.ModName;

        ModPackInstallResult? installResult = null;

        UpdateProgressDialog? progressDialog = null;



        if (mainWindow != null)

        {

            await Dispatcher.UIThread.InvokeAsync(() =>

            {

                progressDialog = new UpdateProgressDialog(packTitle);

                progressDialog.Show(mainWindow);

            });

        }



        var modManagerCallbacks = CreateModManagerCallbacks();



        try

        {

            installResult = await Task.Run(async () =>

            {

                var configService = new ConfigService();

                var installer = new ModPackInstaller(

                    _configuration ?? App.GetService<Microsoft.Extensions.Configuration.IConfiguration>(),

                    configService,

                    _dllModificationService,

                    _diagnosticsOutput ?? throw new InvalidOperationException("Diagnostics output not initialized."),

                    App.GetService<ModInstanceInstaller>(),

                    App.GetService<IModInstanceRepository>());



                var progress = new Progress<(int percent, string message)>(p =>

                {

                    if (progressDialog == null) return;

                    progressDialog.UpdateProgress(p.percent, p.message);

                });



                return await installer.InstallPackAsync(

                    pack,

                    platform,

                    progress,

                    modManagerCallbacks,

                    displayName: _pendingModPackDisplayName);

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

        await RefreshPackInstancesAsync();



        if (!string.IsNullOrEmpty(result.InstanceId))

            ActiveBrowserTab = ModBrowserTab.MyPacks;

    }



    private string? _pendingModPackDisplayName;



    internal void SetPendingModPackDisplayName(string? displayName) =>

        _pendingModPackDisplayName = displayName;



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

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)

            return desktop.MainWindow;

        return null;

    }

}


