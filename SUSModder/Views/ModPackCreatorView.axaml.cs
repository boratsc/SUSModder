using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
    private readonly string _platform;
    private readonly string? _preselectedInstanceId;
    private readonly int? _preselectedCatalogModId;

    private List<ShareSourceEntry> _shareSources = new();
    private List<ModConfiguration> _catalogFullMods = new();
    private List<ModConfiguration> _dllMods = new();
    private readonly List<CheckBox> _dllCheckBoxes = new();
    private readonly List<string> _versionValues = new();

    public string ModalTitle { get; private set; } = string.Empty;
    public event Action<ModPackCreatorDialogResult?>? Completed;

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
        _versionService = new ModVersionService(configuration, diagnostics);
        _loc = loc;
        _platform = platform;
        _preselectedInstanceId = preselectedInstanceId;
        _preselectedCatalogModId = preselectedCatalogModId;

        InitializeComponent();

        ModalTitle = _loc.Get(_mode == ModPackCreatorMode.InstallLocal
            ? "UI.Packs.CreateLocalTitle"
            : "ModPacks.CreatorTitle");
        CreateButton.Content = _loc.Get(_mode == ModPackCreatorMode.InstallLocal
            ? "UI.Packs.CreateLocalButton"
            : "ModPacks.CreateButton");
        PackNameLabel.Text = _loc.Get(_mode == ModPackCreatorMode.InstallLocal
            ? "UI.Packs.LocalDisplayNameLabel"
            : "ModPacks.PackNameLabel");
        SourceLabel.Text = _loc.Get(_mode == ModPackCreatorMode.InstallLocal
            ? "ModPacks.FullModLabel"
            : "UI.Packs.ShareSourceLabel");

        ShareOnlyPanel.IsVisible = _mode == ModPackCreatorMode.ShareOnline;
        VersionPanel.IsVisible = true;

        Loaded += async (_, _) => await LoadDataAsync();
    }

    private void Complete(ModPackCreatorDialogResult? result) => Completed?.Invoke(result);

    private async Task LoadDataAsync()
    {
        _dllMods = _dllService.GetDllMods();

        if (_mode == ModPackCreatorMode.InstallLocal)
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
            if (_mode == ModPackCreatorMode.InstallLocal)
                await RefreshVersionsAsync();
        };
        await RefreshDllListAsync();
        if (_mode == ModPackCreatorMode.InstallLocal)
            await RefreshVersionsAsync();
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
        if (_mode == ModPackCreatorMode.ShareOnline)
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

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Complete(null);

    private async void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;

        if (_mode == ModPackCreatorMode.InstallLocal)
            await CreateLocalInstanceAsync();
        else
            await CreateSharedPackAsync();
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
        try
        {
            var result = await _modPackService.CreatePackAsync(request);
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

            Complete(new ModPackCreatorDialogResult
            {
                Mode = ModPackCreatorMode.ShareOnline,
                ShareResult = result
            });
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
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
        var catalogMod = GetSelectedCatalogMod();
        if (catalogMod == null)
        {
            ShowError(_loc.Get("ModPacks.SelectFullMod"));
            return;
        }

        var displayName = PackNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            ShowError(_loc.Get("UI.Packs.DisplayNameRequired"));
            return;
        }

        var version = GetSelectedVersion();
        var modToInstall = CloneModWithVersion(catalogMod, version);
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
                catch
                {
                    // Częściowa instalacja DLL — kontynuuj
                }
            }

            if (IncludeIntegrationCheck.IsChecked == true)
                TryCopyIntegrationDll(instance.InstallPath);

            Complete(new ModPackCreatorDialogResult
            {
                Mode = ModPackCreatorMode.InstallLocal,
                CreatedInstanceId = instance.InstanceId,
                InstalledDllModIds = installedDllIds
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
        public required string Label { get; init; }
        public required ModConfiguration TargetMod { get; init; }
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
