using System;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views;

public partial class ModPackPreviewDialog : Window
{
    private readonly ModPack _pack;
    private readonly IModPackService _modPackService;
    private readonly ILocalizationService _loc;

    public bool InstallConfirmed { get; private set; }

    public ModPackPreviewDialog()
    {
        _pack = null!;
        _modPackService = null!;
        _loc = null!;
        InitializeComponent();
    }

    public ModPackPreviewDialog(ModPack pack, IModPackService modPackService, ILocalizationService loc)
        : this()
    {
        _pack = pack;
        _modPackService = modPackService;
        _loc = loc;
        PopulateUi();
    }

    private void PopulateUi()
    {
        var catalog = ConfigManager.LoadConfig();
        var fullModName = _pack.FullMod != null ? ResolveModName(_pack.FullMod.Id, catalog) : null;
        var displayName = !string.IsNullOrWhiteSpace(_pack.ModName)
            ? _pack.ModName
            : fullModName ?? _pack.PackCode;

        HeaderText.Text = string.IsNullOrWhiteSpace(_pack.PackCode)
            ? displayName
            : $"{displayName} ({_pack.PackCode})";

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(_pack.CreatorName))
            sb.AppendLine($"{_loc.Get("ModPacks.Author")}: {_pack.CreatorName}");
        if (_pack.FullMod != null)
        {
            var name = fullModName ?? $"#{_pack.FullMod.Id}";
            sb.AppendLine($"{_loc.Get("ModPacks.FullModLabel")}: {name} v{_pack.FullMod.Version}");
        }
        if (_pack.DllMods.Count > 0)
        {
            sb.AppendLine($"{_loc.Get("ModPacks.DllModsLabel")}:");
            foreach (var d in _pack.DllMods)
            {
                var dllName = ResolveModName(d.DllModId, catalog) ?? $"#{d.DllModId}";
                sb.AppendLine($"  • {dllName} v{d.DllModVersion}");
            }
        }
        if (_pack.TouConfig.HasValue)
            sb.AppendLine($"{_loc.Get("ModPacks.TouConfigLabel")}: ✓");
        if (_pack.IncludeIntegrationDll)
            sb.AppendLine($"{_loc.Get("ModPacks.IntegrationLabel")}: ✓");
        if (!string.IsNullOrWhiteSpace(_pack.DiscordInvite))
            sb.AppendLine($"{_loc.Get("ModPacks.DiscordLabel")}: {_pack.DiscordInvite}");
        if (_pack.ExpiresAt.HasValue)
            sb.AppendLine($"{_loc.Get("ModPacks.ExpiresAt")}: {_pack.ExpiresAt.Value.LocalDateTime:g}");
        if (_pack.TtlDays > 0)
            sb.AppendLine($"{_loc.Get("ModPacks.TtlLabel")}: {_pack.TtlDays} {_loc.Get("ModPacks.TtlDaysSuffix")}");

        if (sb.Length == 0)
            sb.AppendLine(_loc.Get("ModPacks.PreviewEmptyFallback"));

        DetailsText.Text = sb.ToString().TrimEnd();

        if (_pack.HasExternalDlls)
        {
            ExternalWarningPanel.IsVisible = true;
            RiskConsentCheckBox.IsVisible = true;

            var vtKey = _pack.HasSuspiciousExternalDll
                ? "ModPacks.VirusTotalSuspicious"
                : _pack.ExternalDlls.Any(d => d.VtStatus is "pending" or "unknown")
                    ? "ModPacks.VirusTotalUnknown"
                    : "ModPacks.VirusTotalClean";
            VtStatusText.Text = _loc.Get(vtKey);

            foreach (var ext in _pack.ExternalDlls)
                DetailsText.Text += $"\n  • {ext.FileName} ({ext.VtStatus})";

            if (_pack.HasSuspiciousExternalDll)
            {
                BlockedText.IsVisible = true;
                BlockedText.Text = _loc.Get("ModPacks.VirusTotalSuspicious");
                InstallButton.IsEnabled = false;
            }
            else
            {
                RiskConsentCheckBox.IsCheckedChanged += (_, _) => UpdateInstallButton();
                UpdateInstallButton();
            }
        }
        else
        {
            InstallButton.IsEnabled = true;
        }
    }

    private static string? ResolveModName(int modId, System.Collections.Generic.List<ModConfiguration> catalog)
    {
        if (modId <= 0) return null;
        return catalog.FirstOrDefault(m => m.Id == modId)?.ModName;
    }

    private void UpdateInstallButton()
    {
        if (!_pack.HasExternalDlls || _pack.HasSuspiciousExternalDll)
            return;
        InstallButton.IsEnabled = RiskConsentCheckBox.IsChecked == true;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void InstallButton_Click(object? sender, RoutedEventArgs e)
    {
        var validation = _modPackService.ValidatePack(_pack, RiskConsentCheckBox.IsChecked == true);
        if (!validation.IsValid)
        {
            BlockedText.IsVisible = true;
            BlockedText.Text = validation.ErrorMessage ?? _loc.Get("ModPacks.InstallFailed");
            return;
        }

        InstallConfirmed = true;
        Close(true);
    }
}
