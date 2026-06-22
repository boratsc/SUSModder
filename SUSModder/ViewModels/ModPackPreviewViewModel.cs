using System;
using System.Linq;
using System.Reactive;
using System.Text;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels;

public sealed class ModPackPreviewViewModel : ViewModelBase
{
    private readonly ModPack _pack;
    private readonly IModPackService _modPackService;
    private readonly ILocalizationService _loc;

    private string _localDisplayName = string.Empty;
    private bool _riskConsent;
    private bool _isExternalWarningVisible;
    private bool _isRiskConsentVisible;
    private bool _isInstallEnabled;
    private bool _isBlockedVisible;
    private string _blockedText = string.Empty;

    public event EventHandler<bool>? Completed;

    public string HeaderText { get; }
    public string InstallAsNewLabel { get; }
    public string LocalNamePlaceholder { get; }
    public string DetailsText { get; }
    public string ExternalDllWarning { get; }
    public string ExternalDllCaution { get; }
    public string VtStatusText { get; }
    public string RiskConsentLabel { get; }
    public string CancelButton { get; }
    public string InstallButton { get; }

    public string LocalDisplayName
    {
        get => _localDisplayName;
        set => this.RaiseAndSetIfChanged(ref _localDisplayName, value);
    }

    public bool RiskConsent
    {
        get => _riskConsent;
        set
        {
            this.RaiseAndSetIfChanged(ref _riskConsent, value);
            UpdateInstallButton();
        }
    }

    public bool IsExternalWarningVisible
    {
        get => _isExternalWarningVisible;
        private set => this.RaiseAndSetIfChanged(ref _isExternalWarningVisible, value);
    }

    public bool IsRiskConsentVisible
    {
        get => _isRiskConsentVisible;
        private set => this.RaiseAndSetIfChanged(ref _isRiskConsentVisible, value);
    }

    public bool IsInstallEnabled
    {
        get => _isInstallEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isInstallEnabled, value);
    }

    public bool IsBlockedVisible
    {
        get => _isBlockedVisible;
        private set => this.RaiseAndSetIfChanged(ref _isBlockedVisible, value);
    }

    public string BlockedText
    {
        get => _blockedText;
        private set => this.RaiseAndSetIfChanged(ref _blockedText, value);
    }

    public string? ResolvedLocalDisplayName =>
        string.IsNullOrWhiteSpace(LocalDisplayName) ? null : LocalDisplayName.Trim();

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallCommand { get; }

    public ModPackPreviewViewModel(ModPack pack, IModPackService modPackService, ILocalizationService loc, string platform = "steam")
    {
        _pack = pack;
        _modPackService = modPackService;
        _loc = loc;

        InstallAsNewLabel = loc.Get("UI.Packs.InstallAsNew");
        LocalNamePlaceholder = loc.Get("UI.Packs.LocalNamePlaceholder");
        ExternalDllWarning = loc.Get("ModPacks.ExternalDllWarning");
        ExternalDllCaution = loc.Get("ModPacks.ExternalDllCaution");
        RiskConsentLabel = pack.HasCustomContent
            ? loc.Get("ModPacks.CustomContent.InstallConsent")
            : loc.Get("ModPacks.RiskConsent");
        CancelButton = loc.Get("UI.Buttons.Cancel");
        InstallButton = loc.Get("UI.Packs.InstallAsNewButton");

        var catalog = ConfigManager.LoadConfig();
        var fullModName = pack.FullMod != null ? ResolveModName(pack.FullMod.Id, catalog) : null;
        var displayName = !string.IsNullOrWhiteSpace(pack.ModName)
            ? pack.ModName
            : fullModName ?? pack.PackCode;

        HeaderText = string.IsNullOrWhiteSpace(pack.PackCode)
            ? displayName
            : $"{displayName} ({pack.PackCode})";
        LocalDisplayName = displayName;
        DetailsText = BuildDetailsText(catalog, fullModName, platform);

        if (pack.HasCustomContent)
        {
            IsExternalWarningVisible = true;
            IsRiskConsentVisible = true;

            var vtKey = GetCustomContentStatusKey(pack);
            VtStatusText = loc.Get(vtKey);

            if (pack.Installable == false || pack.HasNonCleanExternalDll || pack.HasNonCleanCustomArtifact)
            {
                IsBlockedVisible = true;
                BlockedText = loc.Get(vtKey);
                IsInstallEnabled = false;
            }
            else
            {
                UpdateInstallButton();
            }
        }
        else
        {
            VtStatusText = string.Empty;
            IsInstallEnabled = true;
        }

        CancelCommand = ReactiveCommand.Create(() => Completed?.Invoke(this, false));
        InstallCommand = ReactiveCommand.Create(ConfirmInstall);
    }

    private void ConfirmInstall()
    {
        var validation = _modPackService.ValidatePack(_pack, RiskConsent);
        if (!validation.IsValid)
        {
            IsBlockedVisible = true;
            BlockedText = validation.ErrorMessage ?? _loc.Get("ModPacks.InstallFailed");
            return;
        }

        Completed?.Invoke(this, true);
    }

    private void UpdateInstallButton()
    {
        if (!_pack.HasCustomContent || _pack.HasNonCleanExternalDll || _pack.HasNonCleanCustomArtifact)
            return;

        if (_pack.HasCustomFullMod &&
            !string.Equals(_pack.CustomFullMod!.Status, "clean", StringComparison.OrdinalIgnoreCase))
            return;

        IsInstallEnabled = RiskConsent;
    }

    private static string GetCustomContentStatusKey(ModPack pack)
    {
        if (pack.HasSuspiciousExternalDll ||
            pack.CustomArtifacts.Any(a => string.Equals(a.Status, "suspicious", StringComparison.OrdinalIgnoreCase)))
            return "ModPacks.VirusTotalSuspicious";

        if (pack.CustomArtifacts.Any(a => string.Equals(a.Status, "rejected", StringComparison.OrdinalIgnoreCase)))
            return "ModPacks.CustomContent.InstallBlockedSuspicious";

        if (pack.HasCustomFullMod &&
            !string.Equals(pack.CustomFullMod!.Status, "clean", StringComparison.OrdinalIgnoreCase))
            return "ModPacks.CustomContent.InstallBlockedPendingScan";

        if (pack.Installable == false || pack.HasNonCleanExternalDll || pack.HasNonCleanCustomArtifact)
            return "ModPacks.CustomContent.InstallBlockedPendingScan";

        return "ModPacks.VirusTotalClean";
    }

    private string BuildDetailsText(System.Collections.Generic.List<ModConfiguration> catalog, string? fullModName, string platform)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(_pack.CreatorName))
            sb.AppendLine($"{_loc.Get("ModPacks.Author")}: {_pack.CreatorName}");
        if (_pack.FullMod != null)
        {
            var name = fullModName ?? $"#{_pack.FullMod.Id}";
            sb.AppendLine($"{_loc.Get("ModPacks.FullModLabel")}: {name} v{_pack.FullMod.Version}");
        }
        else if (_pack.HasCustomFullMod)
        {
            var cf = _pack.CustomFullMod!;
            var name = string.IsNullOrWhiteSpace(cf.DisplayName) ? cf.FileName : cf.DisplayName;
            sb.AppendLine($"{_loc.Get("ModPacks.FullModLabel")}: {name} ({_loc.Get("ModPacks.CustomFull.Title")}, {cf.Status})");
        }

        sb.AppendLine(_loc.GetFormatted("ModPacks.PlatformVariantLabel", platform));
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

        if (_pack.HasExternalDlls)
        {
            sb.AppendLine(_loc.Get("ModPacks.CustomDlls.Title") + ":");
            foreach (var ext in _pack.ExternalDlls)
                sb.AppendLine($"  • {ext.FileName} ({ext.VtStatus})");
        }

        if (_pack.HasCustomArtifacts)
        {
            sb.AppendLine(_loc.Get("ModPacks.CustomContent.ArtifactsLabel") + ":");
            foreach (var artifact in _pack.CustomArtifacts)
            {
                var source = artifact.SourceKind switch
                {
                    "github_dll" or "github_full" => "GitHub",
                    "uploaded_dll" => _loc.Get("ModPacks.CustomContent.SourceUploadedDll"),
                    _ => artifact.SourceKind
                };
                var name = string.IsNullOrWhiteSpace(artifact.DisplayName)
                    ? artifact.FileName
                    : artifact.DisplayName;
                sb.AppendLine($"  • {name} ({source}, {artifact.Status}, {artifact.Sha256[..Math.Min(12, artifact.Sha256.Length)]}…)");
            }
        }

        if (sb.Length == 0)
            sb.AppendLine(_loc.Get("ModPacks.PreviewEmptyFallback"));

        return sb.ToString().TrimEnd();
    }

    private static string? ResolveModName(int modId, System.Collections.Generic.List<ModConfiguration> catalog)
    {
        if (modId <= 0) return null;
        return catalog.FirstOrDefault(m => m.Id == modId)?.ModName;
    }
}
