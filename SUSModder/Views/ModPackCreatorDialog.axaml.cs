using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views;

public partial class ModPackCreatorDialog : Window
{
    private readonly IModPackService _modPackService;
    private readonly DllModificationService _dllService;
    private readonly ModVersionService _versionService;
    private readonly ILocalizationService _loc;
    private readonly string _platform;
    private readonly int? _preselectedModId;
    private List<ModConfiguration> _fullMods = new();
    private List<ModConfiguration> _dllMods = new();
    private readonly List<CheckBox> _dllCheckBoxes = new();
    private readonly List<string> _versionValues = new();

    public ModPackCreateResult? CreateResult { get; private set; }

    public ModPackCreatorDialog()
    {
        _modPackService = null!;
        _dllService = null!;
        _versionService = null!;
        _loc = null!;
        _platform = string.Empty;
        InitializeComponent();
    }

    public ModPackCreatorDialog(
        IModPackService modPackService,
        DllModificationService dllService,
        IConfiguration configuration,
        IDiagnosticsOutput diagnostics,
        ILocalizationService loc,
        string platform,
        int? preselectedModId = null)
        : this()
    {
        _modPackService = modPackService;
        _dllService = dllService;
        _versionService = new ModVersionService(configuration, diagnostics);
        _loc = loc;
        _platform = platform;
        _preselectedModId = preselectedModId;
        Loaded += async (_, _) => await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _fullMods = _dllService.GetAvailableFullMods();
        _dllMods = _dllService.GetDllMods();

        FullModCombo.ItemsSource = _fullMods.Select(m => $"{m.ModName} (v{m.ModVersion})").ToList();
        if (_preselectedModId.HasValue)
        {
            var idx = _fullMods.FindIndex(m => m.Id == _preselectedModId.Value);
            if (idx >= 0) FullModCombo.SelectedIndex = idx;
        }
        else if (_fullMods.Count > 0)
        {
            FullModCombo.SelectedIndex = 0;
        }

        FullModCombo.SelectionChanged += async (_, _) =>
        {
            await RefreshDllListAsync();
            await RefreshVersionsAsync();
        };
        await RefreshDllListAsync();
        await RefreshVersionsAsync();
    }

    private async Task RefreshVersionsAsync()
    {
        VersionCombo.Items.Clear();
        _versionValues.Clear();

        var fullMod = GetSelectedFullMod();
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
            // Fallback: zainstalowana wersja + latest
            if (!string.IsNullOrWhiteSpace(fullMod.ModVersion))
            {
                VersionCombo.Items.Add($"{fullMod.ModName} v{fullMod.ModVersion} (zainstalowana)");
                _versionValues.Add(fullMod.ModVersion);
            }
        }

        VersionCombo.SelectedIndex = 0;
    }

    private async Task RefreshDllListAsync()
    {
        DllModsPanel.Children.Clear();
        _dllCheckBoxes.Clear();

        var fullMod = GetSelectedFullMod();
        if (fullMod == null) return;

        var installedIds = await _dllService.GetInstalledDllIdsAsync(fullMod);
        foreach (var dll in _dllMods)
        {
            var cb = new CheckBox
            {
                Content = $"{dll.ModName} v{dll.ModVersion}",
                IsChecked = installedIds.Contains(dll.Id)
            };
            _dllCheckBoxes.Add(cb);
            DllModsPanel.Children.Add(cb);
        }
    }

    private ModConfiguration? GetSelectedFullMod()
    {
        var idx = FullModCombo.SelectedIndex;
        return idx >= 0 && idx < _fullMods.Count ? _fullMods[idx] : null;
    }

    private string GetSelectedFullModVersion()
    {
        var idx = VersionCombo.SelectedIndex;
        if (idx >= 0 && idx < _versionValues.Count)
            return _versionValues[idx];
        return "latest";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);

    private async void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        var fullMod = GetSelectedFullMod();
        if (fullMod == null)
        {
            ShowError(_loc.Get("ModPacks.SelectFullMod"));
            return;
        }

        var ttlItem = TtlCombo.SelectedItem as ComboBoxItem;
        var ttlDays = int.TryParse(ttlItem?.Tag?.ToString(), out var t) ? t : 30;

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

        JsonElement? touConfig = null;
        if (IncludeTouCheck.IsChecked == true)
        {
            ShowError(_loc.Get("ModPacks.TouConfigNotSupportedYet"));
            return;
        }

        var creatorName = CreatorNameBox.Text?.Trim();
        var discordInvite = DiscordInviteBox.Text?.Trim();
        var packName = PackNameBox.Text?.Trim();

        var request = new ModPackCreateRequest
        {
            CreatorName = string.IsNullOrEmpty(creatorName) ? null : creatorName,
            FullModId = fullMod.Id,
            FullModVersion = GetSelectedFullModVersion(),
            ModName = string.IsNullOrEmpty(packName) ? null : packName,
            DiscordInvite = string.IsNullOrEmpty(discordInvite) ? null : discordInvite,
            IncludeIntegrationDll = IncludeIntegrationCheck.IsChecked == true,
            TtlDays = ttlDays,
            DllMods = selectedDlls,
            TouConfig = touConfig
        };

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

            CreateResult = result;
            Close(result);
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }
}
