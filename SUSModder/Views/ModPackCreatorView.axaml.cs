using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;
using SUSModder.Core.Utilities;

namespace SUSModder.Views;

public partial class ModPackCreatorView : UserControl
{
    private readonly ModPackCreatorMode _mode;
    private readonly IModPackService _modPackService;
    private readonly DllModificationService _dllService;
    private readonly ModInstanceInstaller? _instanceInstaller;
    private readonly IModInstanceRepository _instances;
    private readonly InstanceToModPackMapper _mapper;
    private readonly ModVersionService _versionService;
    private readonly ILocalizationService _loc;
    private readonly UserSettingsService _userSettingsService = new();
    private readonly string _platform;
    private readonly string? _preselectedInstanceId;
    private readonly int? _preselectedCatalogModId;

    private List<ShareSourceEntry> _shareSources = new();
    private List<ModConfiguration> _catalogFullMods = new();
    private List<ModConfiguration> _dllMods = new();
    private readonly List<CheckBox> _dllCheckBoxes = new();
    private readonly List<string> _versionValues = new();
    private readonly List<CustomDllSelection> _customDllFiles = new();
    private readonly List<GithubDllSelection> _githubDllEntries = new();
    private CancellationTokenSource? _createCts;

    public string ModalTitle { get; private set; } = string.Empty;
    public event Action<ModPackCreatorDialogResult?>? Completed;

    /// <summary>Konstruktor wymagany przez loader XAML (design-time / AVLN3001).</summary>
    public ModPackCreatorView()
    {
        _mode = ModPackCreatorMode.ShareExisting;
        _modPackService = null!;
        _dllService = null!;
        _instanceInstaller = null;
        _instances = null!;
        _mapper = null!;
        _versionService = null!;
        _loc = null!;
        _platform = string.Empty;
        InitializeComponent();
    }

    public ModPackCreatorView(
        ModPackCreatorMode mode,
        IModPackService modPackService,
        DllModificationService dllService,
        IModInstanceRepository instances,
        InstanceToModPackMapper mapper,
        IConfiguration configuration,
        IDiagnosticsOutput diagnostics,
        ILocalizationService loc,
        string platform,
        ModInstanceInstaller? instanceInstaller = null,
        string? preselectedInstanceId = null,
        int? preselectedCatalogModId = null)
    {
        _mode = mode;
        _modPackService = modPackService;
        _dllService = dllService;
        _instances = instances;
        _mapper = mapper;
        _instanceInstaller = instanceInstaller;
        _versionService = new ModVersionService(diagnostics);
        _loc = loc;
        _platform = platform;
        _preselectedInstanceId = preselectedInstanceId;
        _preselectedCatalogModId = preselectedCatalogModId;

        InitializeComponent();

        var createsLocalInstance = _mode is ModPackCreatorMode.InstallLocal or ModPackCreatorMode.CreateAndShare;
        var sharesOnline = _mode is ModPackCreatorMode.ShareExisting or ModPackCreatorMode.CreateAndShare;

        ModalTitle = _loc.Get(_mode == ModPackCreatorMode.InstallLocal
            ? "UI.Packs.CreateLocalTitle" : _mode == ModPackCreatorMode.CreateAndShare ? "UI.Packs.CreateAndShareTitle" : "ModPacks.CreatorTitle");
        CreateButton.Content = _loc.Get(_mode == ModPackCreatorMode.InstallLocal
            ? "UI.Packs.CreateLocalButton"
            : _mode == ModPackCreatorMode.CreateAndShare
                ? "UI.Packs.CreateAndShare"
            : "ModPacks.CreateButton");
        PackNameLabel.Text = _loc.Get(_mode == ModPackCreatorMode.InstallLocal
            ? "UI.Packs.LocalDisplayNameLabel"
            : "ModPacks.PackNameLabel");
        SourceLabel.Text = _loc.Get(createsLocalInstance
            ? "ModPacks.FullModLabel"
            : "UI.Packs.ShareSourceLabel");

        ShareOnlyPanel.IsVisible = sharesOnline;
        CustomDllPanel.IsVisible = true;
        UseGithubDllCheck.IsVisible = sharesOnline;
        CustomGithubDllPanel.IsVisible = UseGithubDllCheck.IsChecked == true;
        UseCustomFullCheck.IsVisible = _mode is ModPackCreatorMode.InstallLocal or ModPackCreatorMode.CreateAndShare;
        CustomFullPanel.IsVisible = UseCustomFullCheck.IsChecked == true;
        VersionPanel.IsVisible = true;

        if (sharesOnline)
            ApplySavedShareProfile();

        // W trybie lokalnym tekst pomocy bez wzmianki o serwerze; CreateAndShare pokazuje oba kroki.
        if (_mode == ModPackCreatorMode.InstallLocal)
        {
            CustomDllNoticeText.Text = _loc.Get("ModPacks.CustomDlls.LocalNotice");
        }
        else if (_mode == ModPackCreatorMode.CreateAndShare)
        {
            CustomDllNoticeText.Text = _loc.Get("ModPacks.CustomDlls.CreateAndShareNotice");
        }

        UseCustomFullCheck.IsCheckedChanged += (_, _) =>
        {
            var useCustomFull = UseCustomFullCheck.IsChecked == true;
            CustomFullPanel.IsVisible = useCustomFull;
            FullModCombo.IsEnabled = !useCustomFull;
            VersionPanel.IsVisible = !useCustomFull;
        };

        UseGithubDllCheck.IsCheckedChanged += (_, _) =>
        {
            CustomGithubDllPanel.IsVisible = UseGithubDllCheck.IsChecked == true;
        };

        Loaded += async (_, _) => await LoadDataAsync();
    }

    private void Complete(ModPackCreatorDialogResult? result) => Completed?.Invoke(result);

    private async Task LoadDataAsync()
    {
        _dllMods = _dllService.GetDllMods();

        if (_mode is ModPackCreatorMode.InstallLocal or ModPackCreatorMode.CreateAndShare)
        {
            if (_instanceInstaller == null)
            {
                ShowError(_loc.Get("UI.Packs.InstallerUnavailable"));
                CreateButton.IsEnabled = false;
                return;
            }

            var configs = ConfigManager.LoadConfig();
            _catalogFullMods = configs
                .Where(m => m.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) && m.Id > 0)
                .OrderBy(m => m.ModName)
                .ToList();

            FullModCombo.ItemsSource = _catalogFullMods.Select(m => $"{m.ModName} (katalog v{m.ModVersion})").ToList();
            if (_preselectedCatalogModId.HasValue)
            {
                var idx = _catalogFullMods.FindIndex(m => m.Id == _preselectedCatalogModId.Value);
                if (idx >= 0) FullModCombo.SelectedIndex = idx;
            }
            else if (_catalogFullMods.Count > 0)
            {
                FullModCombo.SelectedIndex = 0;
            }
        }
        else
        {
            VersionPanel.IsVisible = false;
            _shareSources = BuildShareSources();
            FullModCombo.ItemsSource = _shareSources.Select(s => s.Label).ToList();

            if (!string.IsNullOrEmpty(_preselectedInstanceId))
            {
                var idx = _shareSources.FindIndex(s => s.InstanceId == _preselectedInstanceId);
                if (idx >= 0) FullModCombo.SelectedIndex = idx;
            }
            else if (_preselectedCatalogModId.HasValue)
            {
                var idx = _shareSources.FindIndex(s => s.TargetMod.Id == _preselectedCatalogModId.Value);
                if (idx >= 0) FullModCombo.SelectedIndex = idx;
            }
            else if (_shareSources.Count > 0)
            {
                FullModCombo.SelectedIndex = 0;
            }

            if (_shareSources.Count == 0)
            {
                ShowError(_loc.Get("UI.Packs.NoShareSources"));
                CreateButton.IsEnabled = false;
            }
        }

        FullModCombo.SelectionChanged += async (_, _) =>
        {
            await RefreshDllListAsync();
            if (_mode is ModPackCreatorMode.InstallLocal or ModPackCreatorMode.CreateAndShare)
                await RefreshVersionsAsync();
            if (_mode == ModPackCreatorMode.ShareExisting)
                ApplySelectedShareSourceDefaults(overwrite: true);
        };
        await RefreshDllListAsync();
        if (_mode is ModPackCreatorMode.InstallLocal or ModPackCreatorMode.CreateAndShare)
            await RefreshVersionsAsync();
        if (_mode == ModPackCreatorMode.ShareExisting)
            ApplySelectedShareSourceDefaults(overwrite: false);
    }

    private List<ShareSourceEntry> BuildShareSources()
    {
        var sources = new List<ShareSourceEntry>();
        var catalog = ConfigManager.LoadConfig();
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in _instances.GetPackInstances().OrderBy(i => i.DisplayName))
        {
            if (string.IsNullOrWhiteSpace(instance.InstallPath))
                continue;

            usedPaths.Add(instance.InstallPath);
            var cat = catalog.FirstOrDefault(c => c.Id == instance.BaseModId);
            sources.Add(new ShareSourceEntry
            {
                InstanceId = instance.InstanceId,
                DefaultPackName = instance.DisplayName,
                Label = $"{instance.DisplayName} — {instance.BaseModName} v{instance.FullModVersion}",
                TargetMod = new ModConfiguration
                {
                    Id = instance.BaseModId,
                    ModName = instance.BaseModName,
                    ModType = "full",
                    ModVersion = instance.FullModVersion,
                    AmongVersion = instance.AmongVersion,
                    InstallPath = instance.InstallPath,
                    GitHubRepoOrLink = cat?.GitHubRepoOrLink ?? string.Empty,
                    EpicGitHubRepoOrLink = cat?.EpicGitHubRepoOrLink
                }
            });
        }

        foreach (var legacy in _dllService.GetAvailableFullMods())
        {
            if (string.IsNullOrWhiteSpace(legacy.InstallPath) || usedPaths.Contains(legacy.InstallPath))
                continue;

            sources.Add(new ShareSourceEntry
            {
                InstanceId = null,
                DefaultPackName = legacy.ModName,
                Label = $"{legacy.ModName} ({_loc.Get("UI.Packs.Origin.Legacy")}) v{legacy.ModVersion}",
                TargetMod = legacy
            });
        }

        return sources;
    }

    private async Task RefreshVersionsAsync()
    {
        VersionCombo.Items.Clear();
        _versionValues.Clear();

        var fullMod = GetSelectedCatalogMod();
        if (fullMod == null)
            return;

        VersionCombo.Items.Add(_loc.Get("ModPacks.VersionLatestOption"));
        _versionValues.Add("latest");

        try
        {
            var versions = await _versionService.GetVersionHistoryAsync(fullMod.Id);
            foreach (var v in versions)
            {
                VersionCombo.Items.Add(v.DisplayText);
                _versionValues.Add(v.ModVersion);
            }
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(fullMod.ModVersion))
            {
                VersionCombo.Items.Add($"{fullMod.ModName} v{fullMod.ModVersion}");
                _versionValues.Add(fullMod.ModVersion);
            }
        }

        VersionCombo.SelectedIndex = 0;
    }

    private async Task RefreshDllListAsync()
    {
        DllModsPanel.Children.Clear();
        _dllCheckBoxes.Clear();

        HashSet<int> prechecked;
        if (_mode == ModPackCreatorMode.ShareExisting)
        {
            var source = GetSelectedShareSource();
            if (source == null)
                return;

            if (!string.IsNullOrEmpty(source.InstanceId))
            {
                prechecked = _instances.GetDlls(source.InstanceId)
                    .Where(d => d.DllModId.HasValue)
                    .Select(d => d.DllModId!.Value)
                    .ToHashSet();
            }
            else
            {
                prechecked = (await _dllService.GetInstalledDllIdsAsync(source.TargetMod)).ToHashSet();
            }
        }
        else
        {
            prechecked = new HashSet<int>();
        }

        foreach (var dll in _dllMods)
        {
            var cb = new CheckBox
            {
                Content = $"{dll.ModName} v{dll.ModVersion}",
                IsChecked = prechecked.Contains(dll.Id)
            };
            _dllCheckBoxes.Add(cb);
            DllModsPanel.Children.Add(cb);
        }
    }

    private ShareSourceEntry? GetSelectedShareSource()
    {
        var idx = FullModCombo.SelectedIndex;
        return idx >= 0 && idx < _shareSources.Count ? _shareSources[idx] : null;
    }

    private ModConfiguration? GetSelectedCatalogMod()
    {
        var idx = FullModCombo.SelectedIndex;
        return idx >= 0 && idx < _catalogFullMods.Count ? _catalogFullMods[idx] : null;
    }

    private string GetSelectedVersion()
    {
        var idx = VersionCombo.SelectedIndex;
        if (idx >= 0 && idx < _versionValues.Count)
            return _versionValues[idx];
        return "latest";
    }

    private List<ModConfiguration> GetSelectedDllMods()
    {
        var selected = new List<ModConfiguration>();
        for (var i = 0; i < _dllCheckBoxes.Count; i++)
        {
            if (_dllCheckBoxes[i].IsChecked == true && i < _dllMods.Count)
                selected.Add(_dllMods[i]);
        }

        return selected;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void ApplySavedShareProfile()
    {
        try
        {
            var settings = _userSettingsService.LoadUserSettings();
            CreatorNameBox.Text = settings.ModPackShareCreatorName ?? string.Empty;
            DiscordInviteBox.Text = settings.ModPackShareDiscordInvite ?? string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModPackCreator] Failed to load share profile: {ex.Message}");
        }
    }

    private void SaveShareProfileFromForm()
    {
        try
        {
            var settings = _userSettingsService.LoadUserSettings();
            settings.ModPackShareCreatorName = CreatorNameBox.Text?.Trim() ?? string.Empty;
            settings.ModPackShareDiscordInvite = DiscordInviteBox.Text?.Trim() ?? string.Empty;
            _userSettingsService.SaveUserSettings(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModPackCreator] Failed to save share profile: {ex.Message}");
        }
    }

    private void ApplySelectedShareSourceDefaults(bool overwrite)
    {
        var source = GetSelectedShareSource();
        if (source == null)
            return;

        if (overwrite || string.IsNullOrWhiteSpace(PackNameBox.Text))
            PackNameBox.Text = source.DefaultPackName;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        _createCts?.Cancel();
        Complete(null);
    }

    private async void AddCustomDllFiles_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
        {
            ShowError(_loc.Get("ModPacks.CustomDlls.FilePickerUnavailable"));
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _loc.Get("ModPacks.CustomDlls.AddFiles"),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(_loc.Get("ModPacks.CustomDlls.FileTypeDll"))
                {
                    Patterns = new[] { "*.dll" },
                    MimeTypes = new[] { "application/octet-stream" }
                },
                FilePickerFileTypes.All
            }
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            if (!string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            if (_customDllFiles.Any(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var info = new FileInfo(path);
            _customDllFiles.Add(new CustomDllSelection
            {
                FilePath = path,
                FileName = info.Name,
                FileSize = info.Length,
                Sha256 = ModPackService.ComputeFileSha256(path),
                StatusText = _loc.Get("ModPacks.CustomDlls.ScanPending")
            });
        }

        RefreshCustomDllFilesPanel();
    }

    private void RefreshCustomDllFilesPanel()
    {
        CustomDllFilesPanel.Children.Clear();
        CustomDllStatusText.IsVisible = _customDllFiles.Count > 0;
        CustomDllStatusText.Text = _customDllFiles.Count > 0
            ? string.Format(_loc.Get("ModPacks.CustomDlls.SelectedCount"), _customDllFiles.Count)
            : string.Empty;

        foreach (var entry in _customDllFiles.ToList())
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Avalonia.Thickness(0, 2, 0, 2)
            };

            var text = new TextBlock
            {
                Text = $"{entry.FileName} — {FormatFileSize(entry.FileSize)} — {entry.Sha256[..Math.Min(12, entry.Sha256.Length)]}… — {entry.StatusText}",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 12
            };
            Grid.SetColumn(text, 0);
            row.Children.Add(text);

            var remove = new Button
            {
                Content = _loc.Get("ModPacks.CustomDlls.RemoveFile"),
                Padding = new Avalonia.Thickness(8, 4),
                Tag = entry
            };
            remove.Click += (_, _) =>
            {
                if (remove.Tag is CustomDllSelection selected)
                {
                    _customDllFiles.Remove(selected);
                    RefreshCustomDllFilesPanel();
                }
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);

            CustomDllFilesPanel.Children.Add(row);
        }
    }

    private async void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;

        switch (_mode)
        {
            case ModPackCreatorMode.InstallLocal:
                await CreateLocalInstanceAsync();
                break;
            case ModPackCreatorMode.CreateAndShare:
                await CreateAndShareAsync();
                break;
            case ModPackCreatorMode.ShareExisting:
                await CreateSharedPackAsync();
                break;
        }
    }

    
    private async Task CreateAndShareAsync()
    {
        var useCustomFull = UseCustomFullCheck.IsChecked == true;
        var catalogMod = useCustomFull ? null : GetSelectedCatalogMod();
        if (!useCustomFull && catalogMod == null)
        {
            ShowError(_loc.Get("ModPacks.SelectFullMod"));
            return;
        }

        if (IncludeTouCheck.IsChecked == true)
        {
            ShowError(_loc.Get("ModPacks.TouConfigNotSupportedYet"));
            return;
        }

        var customFullName = CustomFullNameBox.Text?.Trim();
        var displayName = PackNameBox.Text?.Trim();
        if (useCustomFull && string.IsNullOrWhiteSpace(displayName))
            displayName = customFullName;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            ShowError(_loc.Get("UI.Packs.DisplayNameRequired"));
            return;
        }

        ModConfiguration modToInstall;
        if (useCustomFull)
        {
            var customFullMod = BuildCustomFullModForLocalInstall();
            if (customFullMod == null)
                return;

            modToInstall = customFullMod;
        }
        else
        {
            var version = GetSelectedVersion();
            modToInstall = CloneModWithVersion(catalogMod!, version);
        }

        var dllsToInstall = GetSelectedDllMods();
        var ttlItem = TtlCombo.SelectedItem as ComboBoxItem;
        var ttlDays = int.TryParse(ttlItem?.Tag?.ToString(), out var t) ? t : 30;
        var creatorName = string.IsNullOrWhiteSpace(CreatorNameBox.Text) ? null : CreatorNameBox.Text.Trim();
        var discordInvite = string.IsNullOrWhiteSpace(DiscordInviteBox.Text) ? null : DiscordInviteBox.Text.Trim();
        SaveShareProfileFromForm();

        CreateButton.IsEnabled = false;
        _createCts?.Cancel();
        _createCts?.Dispose();
        _createCts = new CancellationTokenSource();
        var ct = _createCts.Token;

        try
        {
            CreateButton.Content = _loc.Get("UI.Packs.CreateLocalButton") + " (0%)";
            var instance = await _instanceInstaller!.InstallFullModInstanceAsync(
                modToInstall,
                displayName,
                _platform,
                new SimpleProgressReporter(p => CreateButton.Content = _loc.Get("UI.Packs.CreateLocalButton") + string.Format(" ({0}%)", p)),
                new SimpleDiagnosticsOutput(),
                new ModManagerUserCallbacks(),
                origin: "manual");

            var installedDllIds = new List<int>();
            var failedDllNames = new List<string>();
            foreach (var dll in dllsToInstall)
            {
                try
                {
                    await _instanceInstaller.InstallDllToInstanceAsync(dll, instance.InstanceId, _platform);
                    installedDllIds.Add(dll.Id);
                }
                catch (Exception ex)
                {
                    failedDllNames.Add(dll.ModName ?? $"DLL#{dll.Id}");
                    System.Diagnostics.Debug.WriteLine($"[ModPackCreator] DLL install failed before share: {dll.ModName} — {ex.Message}");
                }
            }

            foreach (var entry in _customDllFiles)
            {
                try
                {
                    CopyCustomDllToInstance(instance, entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ModPackCreator] Custom DLL copy failed before share: {entry.FileName} — {ex.Message}");
                }
            }

            if (IncludeIntegrationCheck.IsChecked == true)
                TryCopyIntegrationDll(instance.InstallPath);

            CreateButton.Content = _loc.Get("UI.Packs.CreateAndShare");
            var request = useCustomFull
                ? new ModPackCreateRequest
                {
                    CreatorName = creatorName,
                    FullModId = 0,
                    FullModVersion = string.IsNullOrWhiteSpace(CustomFullVersionBox.Text) ? "custom" : CustomFullVersionBox.Text.Trim(),
                    ModName = displayName,
                    DiscordInvite = discordInvite,
                    IncludeIntegrationDll = IncludeIntegrationCheck.IsChecked == true,
                    TtlDays = ttlDays,
                    DllMods = BuildDllModRequests()
                }
                : _mapper.Map(
                    instance.InstanceId,
                    displayName,
                    ttlDays,
                    creatorName,
                    discordInvite,
                    IncludeIntegrationCheck.IsChecked == true);

            var result = await _modPackService.CreatePackAsync(request, ct);
            if (!result.Success)
            {
                var msg = result.ErrorCode switch
                {
                    "PACK_LIMIT_REACHED" => _loc.Get("ModPacks.PackLimitReached"),
                    _ => result.ErrorMessage ?? _loc.Get("ModPacks.CreateFailed")
                };
                ShowError(msg);
                return;
            }

            if (HasCustomContentToSubmit() && !string.IsNullOrWhiteSpace(result.PackCode))
            {
                if (!await SubmitCustomContentAndFinalizeAsync(result, ct))
                    return;
            }

            Complete(new ModPackCreatorDialogResult
            {
                Mode = ModPackCreatorMode.CreateAndShare,
                CreatedInstanceId = instance.InstanceId,
                InstalledDllModIds = installedDllIds,
                FailedDllNames = failedDllNames,
                ShareResult = result
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            CreateButton.IsEnabled = true;
            CreateButton.Content = _loc.Get("UI.Packs.CreateAndShare");
        }
    }
private async Task CreateSharedPackAsync()
    {
        var source = GetSelectedShareSource();
        if (source == null)
        {
            ShowError(_loc.Get("UI.Packs.SelectShareSource"));
            return;
        }

        if (IncludeTouCheck.IsChecked == true)
        {
            ShowError(_loc.Get("ModPacks.TouConfigNotSupportedYet"));
            return;
        }

        var packName = PackNameBox.Text?.Trim();
        var creatorName = CreatorNameBox.Text?.Trim();
        var discordInvite = DiscordInviteBox.Text?.Trim();
        var ttlItem = TtlCombo.SelectedItem as ComboBoxItem;
        var ttlDays = int.TryParse(ttlItem?.Tag?.ToString(), out var t) ? t : 30;
        SaveShareProfileFromForm();

        ModPackCreateRequest request;
        if (!string.IsNullOrEmpty(source.InstanceId))
        {
            request = _mapper.Map(
                source.InstanceId,
                packName,
                ttlDays,
                creatorName,
                discordInvite,
                IncludeIntegrationCheck.IsChecked == true);
        }
        else
        {
            var selectedDlls = BuildDllModRequests();
            request = new ModPackCreateRequest
            {
                CreatorName = string.IsNullOrEmpty(creatorName) ? null : creatorName,
                FullModId = source.TargetMod.Id,
                FullModVersion = GetSelectedFullModVersionFromTarget(source.TargetMod),
                ModName = string.IsNullOrEmpty(packName) ? source.TargetMod.ModName : packName,
                DiscordInvite = string.IsNullOrEmpty(discordInvite) ? null : discordInvite,
                IncludeIntegrationDll = IncludeIntegrationCheck.IsChecked == true,
                TtlDays = ttlDays,
                DllMods = selectedDlls
            };
        }

        CreateButton.IsEnabled = false;
        _createCts?.Cancel();
        _createCts?.Dispose();
        _createCts = new CancellationTokenSource();
        var ct = _createCts.Token;
        try
        {
            var result = await _modPackService.CreatePackAsync(request, ct);
            if (!result.Success)
            {
                var msg = result.ErrorCode switch
                {
                    "PACK_LIMIT_REACHED" => _loc.Get("ModPacks.PackLimitReached"),
                    _ => result.ErrorMessage ?? _loc.Get("ModPacks.CreateFailed")
                };
                ShowError(msg);
                return;
            }

            if (HasCustomContentToSubmit() && !string.IsNullOrWhiteSpace(result.PackCode))
            {
                var customOk = await SubmitCustomContentAndFinalizeAsync(result, ct);
                if (!customOk)
                    return;
            }

            Complete(new ModPackCreatorDialogResult
            {
                Mode = ModPackCreatorMode.ShareExisting,
                ShareResult = result
            });
        }
        catch (OperationCanceledException)
        {
            // User cancelled the dialog; CancelButton already completed the modal.
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private bool HasCustomContentToSubmit() =>
        _customDllFiles.Count > 0 ||
        _githubDllEntries.Count > 0 ||
        UseCustomFullCheck.IsChecked == true;

    private async Task<bool> SubmitCustomContentAndFinalizeAsync(ModPackCreateResult result, CancellationToken ct)
    {
        var packCode = result.PackCode;
        if (string.IsNullOrWhiteSpace(packCode))
            return false;

        foreach (var entry in _customDllFiles)
        {
            ct.ThrowIfCancellationRequested();
            entry.StatusText = _loc.Get("ModPacks.CustomDlls.Uploading");
            RefreshCustomDllFilesPanel();

            var uploaded = await _modPackService.UploadCustomDllAsync(packCode, entry.FilePath, ct);
            if (uploaded == null)
            {
                entry.StatusText = _loc.Get("ModPacks.CustomDlls.UploadFailed");
                RefreshCustomDllFilesPanel();
                ShowError(string.Format(_loc.Get("ModPacks.CustomDlls.UploadFailedForFile"), entry.FileName));
                return false;
            }

            entry.ArtifactId = uploaded.ArtifactId;
            entry.Sha256 = string.IsNullOrWhiteSpace(uploaded.Sha256) ? entry.Sha256 : uploaded.Sha256;
            entry.StatusText = MapCustomArtifactStatus(uploaded.Status);
            RefreshCustomDllFilesPanel();

            var clean = await WaitForCustomDllCleanAsync(packCode, entry, ct);
            if (!clean)
                return false;
        }

        if (!await DeclareAndWaitForGithubDllAsync(packCode, ct))
            return false;

        if (!await DeclareAndWaitForCustomFullAsync(packCode, ct))
            return false;

        CustomDllStatusText.Text = _loc.Get("ModPacks.CustomDlls.Finalizing");
        CustomDllStatusText.IsVisible = true;
        var finalize = await _modPackService.FinalizePackAsync(packCode, ct);
        if (!finalize.Success || !finalize.Installable)
        {
            ShowError(finalize.ErrorMessage ?? _loc.Get("ModPacks.CustomContent.InstallBlockedPendingScan"));
            return false;
        }

        result.Status = finalize.Status;
        result.Installable = finalize.Installable;
        if (!string.IsNullOrWhiteSpace(finalize.ShareUrl))
            result.ShareUrl = finalize.ShareUrl;
        if (!string.IsNullOrWhiteSpace(finalize.DeepLink))
            result.DeepLink = finalize.DeepLink;

        CustomDllStatusText.Text = _loc.Get("ModPacks.CustomDlls.ScanClean");
        return true;
    }

    private async Task<bool> DeclareAndWaitForGithubDllAsync(string packCode, CancellationToken ct)
    {
        if (_githubDllEntries.Count == 0)
            return true;

        GithubDllStatusText.IsVisible = true;

        for (var i = 0; i < _githubDllEntries.Count; i++)
        {
            var entry = _githubDllEntries[i];
            ct.ThrowIfCancellationRequested();

            var githubUrl = (entry.GithubUrl ?? string.Empty).Trim();
            var displayName = (entry.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(githubUrl))
            {
                ShowError(string.Format(_loc.Get("ModPacks.CustomGitHub.UrlRequiredFor"), i + 1));
                return false;
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                ShowError(string.Format(_loc.Get("ModPacks.CustomGitHub.NameRequiredFor"), i + 1));
                return false;
            }
            if (!IsAllowedGithubReleaseAssetUrl(githubUrl))
            {
                ShowError(_loc.Get("ModPacks.CustomGitHub.ReleaseAssetRequired"));
                return false;
            }

            var dllInstallPath = string.IsNullOrWhiteSpace(entry.DllInstallPath)
                ? "BepInEx/plugins"
                : entry.DllInstallPath.Trim();

            if (!IsSafeDllInstallPath(dllInstallPath))
            {
                ShowError(_loc.Get("ModPacks.CustomGitHub.DllInstallPathInvalid"));
                return false;
            }

            entry.StatusText = _loc.Get("ModPacks.CustomGitHub.Declaring");
            RefreshGithubDllEntriesPanel();

            var artifact = await _modPackService.DeclareGitHubCustomModAsync(packCode, new ModPackCustomGithubModRequest
            {
                SourceKind = "github_dll",
                ModType = "dll",
                DisplayName = displayName,
                Version = string.IsNullOrWhiteSpace(entry.Version) ? null : entry.Version.Trim(),
                GithubUrl = githubUrl,
                DllInstallPath = dllInstallPath
            }, ct);

            if (artifact == null || string.IsNullOrWhiteSpace(artifact.ArtifactId))
            {
                entry.StatusText = _loc.Get("ModPacks.CustomGitHub.DeclareFailed");
                RefreshGithubDllEntriesPanel();
                ShowError(_loc.Get("ModPacks.CustomGitHub.DeclareFailed"));
                return false;
            }

            entry.ArtifactId = artifact.ArtifactId;
            entry.StatusText = MapCustomArtifactStatus(artifact.Status);
            RefreshGithubDllEntriesPanel();

            if (!await WaitForGithubDllEntryCleanAsync(packCode, entry, ct))
                return false;
        }

        return true;
    }

    private async Task<bool> WaitForGithubDllEntryCleanAsync(
        string packCode, GithubDllSelection entry, CancellationToken ct,
        Action<string>? updateStatusText = null)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var status = await _modPackService.GetCustomArtifactStatusAsync(packCode, entry.ArtifactId!, ct);
            if (!status.Success)
            {
                entry.StatusText = status.ErrorCode ?? _loc.Get("ModPacks.CustomGitHub.StatusFailed");
                if (updateStatusText != null) updateStatusText(entry.StatusText);
                else RefreshGithubDllEntriesPanel();
                ShowError(status.ErrorMessage ?? _loc.Get("ModPacks.CustomGitHub.StatusFailed"));
                return false;
            }

            entry.StatusText = MapCustomArtifactStatus(status.Status);
            if (updateStatusText != null) updateStatusText(entry.StatusText);
            else RefreshGithubDllEntriesPanel();

            if (string.Equals(status.Status, "clean", StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsBlockingCustomArtifactStatus(status.Status))
            {
                ShowError(string.Format(_loc.Get("ModPacks.CustomGitHub.Blocked"), entry.DisplayName, entry.StatusText));
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        ShowError(_loc.Get("ModPacks.CustomContent.InstallBlockedPendingScan"));
        return false;
    }

    private sealed class GithubDllSelection
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string GithubUrl { get; set; } = string.Empty;
        public string DllInstallPath { get; set; } = "BepInEx/plugins";
        public string? ArtifactId { get; set; }
        public string StatusText { get; set; } = string.Empty;
    }

    private async void AddGithubDll_Click(object? sender, RoutedEventArgs e)
    {
        _githubDllEntries.Add(new GithubDllSelection());
        RefreshGithubDllEntriesPanel();
    }

    private void RefreshGithubDllEntriesPanel()
    {
        GithubDllEntriesPanel.Children.Clear();
        GithubDllStatusText.IsVisible = _githubDllEntries.Count > 0;
        GithubDllStatusText.Text = _githubDllEntries.Count > 0
            ? string.Format(_loc.Get("ModPacks.CustomGitHub.SelectedCount"), _githubDllEntries.Count)
            : string.Empty;

        for (var i = 0; i < _githubDllEntries.Count; i++)
        {
            var entry = _githubDllEntries[i];
            var idx = i;

            var card = new Border
            {
                BorderBrush = Avalonia.Media.Brushes.Gray,
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(8),
                Margin = new Avalonia.Thickness(0, 2, 0, 2)
            };

            var stack = new StackPanel { Spacing = 4 };

            var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(new TextBlock { Text = $"#{idx + 1}", FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 13 });
            var nameBox = new TextBox { Text = entry.DisplayName, PlaceholderText = _loc.Get("ModPacks.CustomGitHub.NamePlaceholder"), FontSize = 12, MinWidth = 200 };
            nameBox.TextChanged += (_, _) => { entry.DisplayName = nameBox.Text ?? string.Empty; };
            header.Children.Add(nameBox);
            stack.Children.Add(header);

            var urlBox = new TextBox { Text = entry.GithubUrl, PlaceholderText = _loc.Get("ModPacks.CustomGitHub.UrlPlaceholder"), FontSize = 12 };
            urlBox.TextChanged += (_, _) => { entry.GithubUrl = urlBox.Text ?? string.Empty; };
            stack.Children.Add(urlBox);

            if (!string.IsNullOrWhiteSpace(entry.StatusText))
            {
                stack.Children.Add(new TextBlock { Text = entry.StatusText, FontSize = 11, Foreground = Avalonia.Media.Brushes.Gray });
            }

            var btnRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            var removeBtn = new Button { Content = _loc.Get("ModPacks.CustomDlls.RemoveFile"), Padding = new Avalonia.Thickness(6, 2), FontSize = 11, Tag = entry };
            removeBtn.Click += (_, _) =>
            {
                if (removeBtn.Tag is GithubDllSelection sel)
                {
                    _githubDllEntries.Remove(sel);
                    RefreshGithubDllEntriesPanel();
                }
            };
            btnRow.Children.Add(removeBtn);
            stack.Children.Add(btnRow);

            card.Child = stack;
            GithubDllEntriesPanel.Children.Add(card);
        }
    }

    private async Task<bool> DeclareAndWaitForCustomFullAsync(string packCode, CancellationToken ct)
    {
        if (UseCustomFullCheck.IsChecked != true)
            return true;

        var githubUrl = CustomFullUrlBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(githubUrl))
        {
            ShowError(_loc.Get("ModPacks.CustomFull.UrlRequired"));
            return false;
        }

        if (!IsAllowedGithubReleaseAssetUrl(githubUrl))
        {
            ShowError(_loc.Get("ModPacks.CustomFull.ReleaseAssetRequired"));
            return false;
        }

        var displayName = CustomFullNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            ShowError(_loc.Get("ModPacks.CustomFull.NameRequired"));
            return false;
        }

        CustomFullStatusText.IsVisible = true;
        CustomFullStatusText.Text = _loc.Get("ModPacks.CustomFull.Declaring");

        var artifact = await _modPackService.DeclareGitHubCustomModAsync(packCode, new ModPackCustomGithubModRequest
        {
            SourceKind = "github_full",
            ModType = "full",
            DisplayName = displayName,
            Version = string.IsNullOrWhiteSpace(CustomFullVersionBox.Text) ? null : CustomFullVersionBox.Text.Trim(),
            GithubUrl = githubUrl
        }, ct);

        if (artifact == null || string.IsNullOrWhiteSpace(artifact.ArtifactId))
        {
            CustomFullStatusText.Text = _loc.Get("ModPacks.CustomFull.DeclareFailed");
            ShowError(_loc.Get("ModPacks.CustomFull.DeclareFailed"));
            return false;
        }

        CustomFullStatusText.Text = MapCustomArtifactStatus(artifact.Status);
        return await WaitForGithubDllEntryCleanAsync(packCode,
            new GithubDllSelection { DisplayName = displayName, ArtifactId = artifact.ArtifactId },
            ct,
            updateStatusText: s => { CustomFullStatusText.Text = s; });
    }

    private static bool IsAllowedGithubReleaseAssetUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 6 &&
               string.Equals(segments[2], "releases", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(segments[3], "download", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(segments[4]) &&
               !string.IsNullOrWhiteSpace(segments[5]);
    }

    private static bool IsSafeDllInstallPath(string dllInstallPath)
    {
        if (string.IsNullOrWhiteSpace(dllInstallPath))
            return true;

        var normalized = dllInstallPath.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal))
            return false;

        var root = Path.GetFullPath(Path.Combine("C:\\SUSModderPathValidation", "BepInEx", "plugins"));
        var candidate = Path.GetFullPath(Path.Combine("C:\\SUSModderPathValidation", normalized));
        var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var trimmedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(trimmedCandidate, trimmedRoot, StringComparison.OrdinalIgnoreCase) ||
               trimmedCandidate.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> WaitForCustomDllCleanAsync(
        string packCode, CustomDllSelection entry, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var status = await _modPackService.GetExternalDllStatusAsync(packCode, entry.Sha256, ct);
            if (!status.Success)
            {
                entry.StatusText = status.ErrorCode ?? _loc.Get("ModPacks.CustomDlls.UploadFailed");
                RefreshCustomDllFilesPanel();
                ShowError(status.ErrorMessage ?? _loc.Get("ModPacks.CustomDlls.UploadFailed"));
                return false;
            }

            entry.StatusText = MapCustomArtifactStatus(status.Status);
            RefreshCustomDllFilesPanel();

            if (string.Equals(status.Status, "clean", StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsBlockingCustomArtifactStatus(status.Status))
            {
                ShowError(string.Format(_loc.Get("ModPacks.CustomDlls.BlockedForFile"), entry.FileName, entry.StatusText));
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        ShowError(_loc.Get("ModPacks.CustomContent.InstallBlockedPendingScan"));
        return false;
    }

    private string MapCustomArtifactStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "clean" => _loc.Get("ModPacks.CustomDlls.ScanClean"),
            "suspicious" => _loc.Get("ModPacks.CustomDlls.ScanSuspicious"),
            "rejected" => _loc.Get("ModPacks.CustomDlls.ScanRejected"),
            "expired" => _loc.Get("ModPacks.CustomDlls.ScanExpired"),
            "scanning" => _loc.Get("ModPacks.CustomDlls.Scanning"),
            "pending" => _loc.Get("ModPacks.CustomDlls.ScanPending"),
            _ => _loc.Get("ModPacks.CustomDlls.ScanPending")
        };
    }

    private static bool IsBlockingCustomArtifactStatus(string? status) =>
        string.Equals(status, "suspicious", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "rejected", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase);

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private void CopyCustomDllToInstance(ModInstance instance, CustomDllSelection entry)
    {
        var actualPath = PathSettings.GetActualModPath(instance.InstallPath);
        var pluginsDir = Path.Combine(actualPath, "BepInEx", "plugins");
        var dest = Path.Combine(pluginsDir, entry.FileName);

        Directory.CreateDirectory(pluginsDir);
        File.Copy(entry.FilePath, dest, overwrite: true);

        var now = DateTime.UtcNow.ToString("O");
        _instances.AddDll(new ModInstanceDll
        {
            InstanceId = instance.InstanceId,
            DllModId = null,
            DllName = entry.FileName,
            DllVersion = string.Empty,
            Source = "external",
            Sha256 = entry.Sha256,
            VtStatus = "unknown",
            InstalledPath = entry.FileName,
            CreatedAt = now
        });
    }

    private string GetSelectedFullModVersionFromTarget(ModConfiguration target) =>
        string.IsNullOrWhiteSpace(target.ModVersion) ? "latest" : target.ModVersion;

    private List<ModPackDllModRequest> BuildDllModRequests()
    {
        var selectedDlls = new List<ModPackDllModRequest>();
        for (var i = 0; i < _dllCheckBoxes.Count; i++)
        {
            if (_dllCheckBoxes[i].IsChecked == true && i < _dllMods.Count)
            {
                var dll = _dllMods[i];
                selectedDlls.Add(new ModPackDllModRequest
                {
                    DllModId = dll.Id,
                    DllModVersion = dll.ModVersion ?? "latest"
                });
            }
        }

        return selectedDlls;
    }

    private async Task CreateLocalInstanceAsync()
    {
        var useCustomFull = UseCustomFullCheck.IsChecked == true;
        var catalogMod = useCustomFull ? null : GetSelectedCatalogMod();
        if (!useCustomFull && catalogMod == null)
        {
            ShowError(_loc.Get("ModPacks.SelectFullMod"));
            return;
        }

        var displayName = PackNameBox.Text?.Trim();
        if (useCustomFull && string.IsNullOrWhiteSpace(displayName))
            displayName = CustomFullNameBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            ShowError(_loc.Get("UI.Packs.DisplayNameRequired"));
            return;
        }

        ModConfiguration modToInstall;
        if (useCustomFull)
        {
            var customFullMod = BuildCustomFullModForLocalInstall();
            if (customFullMod == null)
                return;

            modToInstall = customFullMod;
        }
        else
        {
            var version = GetSelectedVersion();
            modToInstall = CloneModWithVersion(catalogMod!, version);
        }

        var dllsToInstall = GetSelectedDllMods();

        CreateButton.IsEnabled = false;
        try
        {
            var progress = new SimpleProgressReporter(p =>
            {
                CreateButton.Content = $"{_loc.Get("UI.Packs.CreateLocalButton")} ({p}%)";
            });

            var instance = await _instanceInstaller!.InstallFullModInstanceAsync(
                modToInstall,
                displayName,
                _platform,
                progress,
                new SimpleDiagnosticsOutput(),
                new ModManagerUserCallbacks(),
                origin: "manual");

            var installedDllIds = new List<int>();
            var failedDllNames = new List<string>();
            foreach (var dll in dllsToInstall)
            {
                try
                {
                    await _instanceInstaller.InstallDllToInstanceAsync(
                        dll,
                        instance.InstanceId,
                        _platform);
                    installedDllIds.Add(dll.Id);
                }
                catch (Exception ex)
                {
                    failedDllNames.Add(dll.ModName ?? $"DLL#{dll.Id}");
                    System.Diagnostics.Debug.WriteLine($"[ModPackCreator] DLL install failed: {dll.ModName} — {ex.Message}");
                }
            }

            // Kopiowanie własnych DLL do instancji lokalnej
            foreach (var entry in _customDllFiles)
            {
                try
                {
                    CopyCustomDllToInstance(instance, entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ModPackCreator] Custom DLL copy failed: {entry.FileName} — {ex.Message}");
                }
            }

            if (IncludeIntegrationCheck.IsChecked == true)
                TryCopyIntegrationDll(instance.InstallPath);

            Complete(new ModPackCreatorDialogResult
            {
                Mode = ModPackCreatorMode.InstallLocal,
                CreatedInstanceId = instance.InstanceId,
                InstalledDllModIds = installedDllIds,
                FailedDllNames = failedDllNames
            });
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            CreateButton.IsEnabled = true;
            CreateButton.Content = _loc.Get("UI.Packs.CreateLocalButton");
        }
    }

    private static ModConfiguration CloneModWithVersion(ModConfiguration source, string version)
    {
        var clone = new ModConfiguration
        {
            Id = source.Id,
            ModName = source.ModName,
            ModType = source.ModType,
            ModVersion = source.ModVersion,
            AmongVersion = source.AmongVersion,
            GitHubRepoOrLink = source.GitHubRepoOrLink,
            EpicGitHubRepoOrLink = source.EpicGitHubRepoOrLink,
            Description = source.Description,
            PngFileName = source.PngFileName,
            DllInstallPath = source.DllInstallPath
        };

        if (!string.IsNullOrWhiteSpace(version) &&
            !string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
        {
            clone.ModVersion = version;
        }

        return clone;
    }

    private ModConfiguration? BuildCustomFullModForLocalInstall()
    {
        var githubUrl = CustomFullUrlBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(githubUrl))
        {
            ShowError(_loc.Get("ModPacks.CustomFull.UrlRequired"));
            return null;
        }

        if (!IsAllowedGithubReleaseAssetUrl(githubUrl))
        {
            ShowError(_loc.Get("ModPacks.CustomFull.ReleaseAssetRequired"));
            return null;
        }

        var displayName = CustomFullNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            ShowError(_loc.Get("ModPacks.CustomFull.NameRequired"));
            return null;
        }

        return new ModConfiguration
        {
            Id = 0,
            ModName = displayName,
            ModType = "full",
            ModVersion = string.IsNullOrWhiteSpace(CustomFullVersionBox.Text)
                ? "custom"
                : CustomFullVersionBox.Text.Trim(),
            GitHubRepoOrLink = githubUrl,
            EpicGitHubRepoOrLink = githubUrl,
            DllInstallPath = "BepInEx/plugins"
        };
    }

    private static bool TryCopyIntegrationDll(string installPath)
    {
        var candidates = new[]
        {
            Path.Combine(installPath, "BepInEx", "plugins", "integration.dll"),
            Path.Combine(PathSettings.ModsInstallPath, "integration.dll")
        };

        var source = candidates.FirstOrDefault(File.Exists);
        if (source == null)
            return false;

        var dest = Path.Combine(
            PathSettings.GetActualModPath(installPath),
            "BepInEx", "plugins", "integration.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, overwrite: true);
        return true;
    }

    private sealed class ShareSourceEntry
    {
        public string? InstanceId { get; init; }
        public required string DefaultPackName { get; init; }
        public required string Label { get; init; }
        public required ModConfiguration TargetMod { get; init; }
    }

    private sealed class CustomDllSelection
    {
        public required string FilePath { get; init; }
        public required string FileName { get; init; }
        public long FileSize { get; init; }
        public required string Sha256 { get; set; }
        public string? ArtifactId { get; set; }
        public string StatusText { get; set; } = string.Empty;
    }

    private sealed class SimpleProgressReporter : IProgressReporter
    {
        private readonly Action<int> _onProgress;
        public SimpleProgressReporter(Action<int> onProgress) => _onProgress = onProgress;
        public void Report(int percent, string? message = null) => _onProgress(percent);
    }

    private sealed class SimpleDiagnosticsOutput : IDiagnosticsOutput
    {
        public void Write(string message) =>
            System.Diagnostics.Debug.WriteLine($"[ModPackCreator] {message}");
    }
}
