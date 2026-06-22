using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using SUSModder.Core.Api.Support;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Diagnostics.Launch;
using SUSModder.Views;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    // ── Launch diagnostics panel (i18n) ─────────────────────

    private string _launchDiagBtnOpenModFolder = string.Empty;
    private string _launchDiagBtnOpenLogs = string.Empty;
    private string _launchDiagBtnSupportBundle = string.Empty;
    private string _launchDiagBtnAiSupport = string.Empty;
    private string _launchDiagBtnClose = string.Empty;

    public string LaunchDiagBtnOpenModFolder
    {
        get => _launchDiagBtnOpenModFolder;
        private set => this.RaiseAndSetIfChanged(ref _launchDiagBtnOpenModFolder, value);
    }

    public string LaunchDiagBtnOpenLogs
    {
        get => _launchDiagBtnOpenLogs;
        private set => this.RaiseAndSetIfChanged(ref _launchDiagBtnOpenLogs, value);
    }

    public string LaunchDiagBtnSupportBundle
    {
        get => _launchDiagBtnSupportBundle;
        private set => this.RaiseAndSetIfChanged(ref _launchDiagBtnSupportBundle, value);
    }

    public string LaunchDiagBtnAiSupport
    {
        get => _launchDiagBtnAiSupport;
        private set => this.RaiseAndSetIfChanged(ref _launchDiagBtnAiSupport, value);
    }

    public string LaunchDiagBtnClose
    {
        get => _launchDiagBtnClose;
        private set => this.RaiseAndSetIfChanged(ref _launchDiagBtnClose, value);
    }

    // ── AI Support panel (i18n) ─────────────────────────────

    private string _aiSupportTitle = string.Empty;
    private string _aiSupportPrivacyNoticeText = string.Empty;
    private string _aiSupportAcceptPrivacyBtn = string.Empty;
    private string _aiSupportProblemPlaceholder = string.Empty;
    private string _aiSupportIncludeDiagnosticsLabel = string.Empty;
    private string _aiSupportAnalyzeBtn = string.Empty;
    private string _aiSupportHelpedBtn = string.Empty;
    private string _aiSupportNotHelpedBtn = string.Empty;
    private string _aiSupportGenerateReportBtn = string.Empty;
    private string _aiSupportCloseBtn = string.Empty;

    public string AiSupportTitle
    {
        get => _aiSupportTitle;
        private set => this.RaiseAndSetIfChanged(ref _aiSupportTitle, value);
    }

    public string AiSupportPrivacyNoticeText
    {
        get => _aiSupportPrivacyNoticeText;
        private set => this.RaiseAndSetIfChanged(ref _aiSupportPrivacyNoticeText, value);
    }

    public string AiSupportAcceptPrivacyBtn
    {
        get => _aiSupportAcceptPrivacyBtn;
        private set => this.RaiseAndSetIfChanged(ref _aiSupportAcceptPrivacyBtn, value);
    }

    public string AiSupportProblemPlaceholder
    {
        get => _aiSupportProblemPlaceholder;
        private set => this.RaiseAndSetIfChanged(ref _aiSupportProblemPlaceholder, value);
    }

    public string AiSupportIncludeDiagnosticsLabel
    {
        get => _aiSupportIncludeDiagnosticsLabel;
        private set => this.RaiseAndSetIfChanged(ref _aiSupportIncludeDiagnosticsLabel, value);
    }

    public string AiSupportAnalyzeBtn
    {
        get => _aiSupportAnalyzeBtn;
        private set => this.RaiseAndSetIfChanged(ref _aiSupportAnalyzeBtn, value);
    }

    public string AiSupportHelpedBtn
    {
        get => _aiSupportHelpedBtn;
        private set => this.RaiseAndSetIfChanged(ref _aiSupportHelpedBtn, value);
    }

    public string AiSupportNotHelpedBtn
    {
        get => _aiSupportNotHelpedBtn;
        private set => this.RaiseAndSetIfChanged(ref _aiSupportNotHelpedBtn, value);
    }

    public string AiSupportGenerateReportBtn
    {
        get => _aiSupportGenerateReportBtn;
        private set => this.RaiseAndSetIfChanged(ref _aiSupportGenerateReportBtn, value);
    }

    public string AiSupportCloseBtn
    {
        get => _aiSupportCloseBtn;
        private set => this.RaiseAndSetIfChanged(ref _aiSupportCloseBtn, value);
    }

    public void RefreshLaunchDiagnosticsPanelStrings()
    {
        LaunchDiagBtnOpenModFolder = _localizationService.Get("LaunchDiagnostics.Actions.OpenModFolder");
        LaunchDiagBtnOpenLogs = _localizationService.Get("LaunchDiagnostics.Actions.OpenLogs");
        LaunchDiagBtnSupportBundle = _localizationService.Get("LaunchDiagnostics.Actions.CreateSupportBundle");
        LaunchDiagBtnAiSupport = _localizationService.Get("LaunchDiagnostics.Actions.OpenAiSupport");
        LaunchDiagBtnClose = _localizationService.Get("LaunchDiagnostics.Actions.Close");

        AiSupportTitle = _localizationService.Get("AiSupport.Title");
        AiSupportPrivacyNoticeText = _localizationService.Get("AiSupport.PrivacyNotice");
        AiSupportAcceptPrivacyBtn = _localizationService.Get("AiSupport.Actions.AcceptPrivacy");
        AiSupportProblemPlaceholder = _localizationService.Get("AiSupport.ProblemPlaceholder");
        AiSupportIncludeDiagnosticsLabel = _localizationService.Get("AiSupport.IncludeDiagnostics");
        AiSupportAnalyzeBtn = _localizationService.Get("AiSupport.AnalyzeButton");
        AiSupportHelpedBtn = _localizationService.Get("AiSupport.Actions.Helped");
        AiSupportNotHelpedBtn = _localizationService.Get("AiSupport.Actions.NotHelped");
        AiSupportGenerateReportBtn = _localizationService.Get("AiSupport.Actions.GenerateReport");
        AiSupportCloseBtn = _localizationService.Get("AiSupport.Actions.Dismiss");
    }

    // ── AI Support visibility ───────────────────────────────

    private bool _isAiSupportVisible;

    public bool IsAiSupportVisible
    {
        get => _isAiSupportVisible;
        set
        {
            this.RaiseAndSetIfChanged(ref _isAiSupportVisible, value);
            NotifyToolModalStateChanged();
        }
    }

    // ── Launch diagnostics visibility ───────────────────────

    private bool _isLaunchDiagnosticsVisible;
    private string _launchDiagnosticsTitle = string.Empty;
    private string _launchDiagnosticsSummary = string.Empty;
    private string _launchDiagnosticsCodes = string.Empty;
    private string _launchDiagnosticsSeverity = string.Empty;
    private LaunchResult? _lastLaunchResult;

    public bool IsLaunchDiagnosticsVisible
    {
        get => _isLaunchDiagnosticsVisible;
        set => this.RaiseAndSetIfChanged(ref _isLaunchDiagnosticsVisible, value);
    }

    public string LaunchDiagnosticsTitle
    {
        get => _launchDiagnosticsTitle;
        set => this.RaiseAndSetIfChanged(ref _launchDiagnosticsTitle, value);
    }

    public string LaunchDiagnosticsSummary
    {
        get => _launchDiagnosticsSummary;
        set => this.RaiseAndSetIfChanged(ref _launchDiagnosticsSummary, value);
    }

    public string LaunchDiagnosticsCodes
    {
        get => _launchDiagnosticsCodes;
        set => this.RaiseAndSetIfChanged(ref _launchDiagnosticsCodes, value);
    }

    public string LaunchDiagnosticsSeverity
    {
        get => _launchDiagnosticsSeverity;
        set => this.RaiseAndSetIfChanged(ref _launchDiagnosticsSeverity, value);
    }

    private void ShowLaunchDiagnostics(LaunchResult result)
    {
        _lastLaunchResult = result;

        IsPostInstallSuccessVisible = false;
        PostInstallSuccessViewModel = null;
        IsPostInstallFailureVisible = false;
        PostInstallFailureViewModel = null;

        LaunchDiagnosticsTitle = result.IsSuccessful
            ? _localizationService.Get("LaunchDiagnostics.TitleSuccess")
            : _localizationService.Get("LaunchDiagnostics.TitleFailure");

        LaunchDiagnosticsSeverity = result.Severity switch
        {
            DiagnosisSeverity.Critical => "🔴 " + _localizationService.Get("LaunchDiagnostics.Severity.Critical"),
            DiagnosisSeverity.Warning => "🟡 " + _localizationService.Get("LaunchDiagnostics.Severity.Warning"),
            _ => "🟢 " + _localizationService.Get("LaunchDiagnostics.Severity.Info")
        };

        LaunchDiagnosticsCodes = string.Join(", ", result.DiagnosisCodes);

        var summaryKey = result.DiagnosisCodes.FirstOrDefault() switch
        {
            DiagnosisCode.ProcessStartFailed => "LaunchDiagnostics.Summary.ProcessStartFailed",
            DiagnosisCode.ProcessExitedEarly => "LaunchDiagnostics.Summary.ExitedEarly",
            DiagnosisCode.BepInExLogMissing => "LaunchDiagnostics.Summary.BepInExMissing",
            DiagnosisCode.BepInExLogStale => "LaunchDiagnostics.Summary.BepInExStale",
            DiagnosisCode.BepInExPluginLoadFailed => "LaunchDiagnostics.Summary.PluginLoadFailed",
            DiagnosisCode.BepInExAccessDenied => "LaunchDiagnostics.Summary.AccessDenied",
            DiagnosisCode.DefenderThreatDetected => "LaunchDiagnostics.Summary.DefenderPossible",
            DiagnosisCode.DefenderCfaBlocked => "LaunchDiagnostics.Summary.DefenderCfaBlocked",
            DiagnosisCode.DefenderEventsUnavailable => "LaunchDiagnostics.Summary.DefenderEventsUnavailable",
            DiagnosisCode.FirewallRuleMissingOrBlocked => "LaunchDiagnostics.Summary.FirewallPossible",
            _ => "LaunchDiagnostics.Summary.Unknown"
        };

        LaunchDiagnosticsSummary = result.IsSuccessful
            ? result.TechnicalSummary
            : _localizationService.Get(summaryKey);

        IsLaunchDiagnosticsVisible = true;
    }

    public void HideLaunchDiagnostics()
    {
        IsLaunchDiagnosticsVisible = false;
    }

    // ── AI Support ──────────────────────────────────────────

    private string _aiSupportProblem = string.Empty;
    private bool _aiSupportIncludeDiagnostics = true;
    private string _aiSupportResultSummary = string.Empty;
    private string _aiSupportResultSteps = string.Empty;
    private string _aiSupportResultWarnings = string.Empty;
    private bool _aiSupportIsLoading;
    private string? _aiSupportSessionId;
    private bool _aiSupportPrivacyAccepted;

    public string AiSupportProblem
    {
        get => _aiSupportProblem;
        set => this.RaiseAndSetIfChanged(ref _aiSupportProblem, value);
    }

    public bool AiSupportIncludeDiagnostics
    {
        get => _aiSupportIncludeDiagnostics;
        set => this.RaiseAndSetIfChanged(ref _aiSupportIncludeDiagnostics, value);
    }

    public string AiSupportResultSummary
    {
        get => _aiSupportResultSummary;
        set => this.RaiseAndSetIfChanged(ref _aiSupportResultSummary, value);
    }

    public string AiSupportResultSteps
    {
        get => _aiSupportResultSteps;
        set => this.RaiseAndSetIfChanged(ref _aiSupportResultSteps, value);
    }

    public string AiSupportResultWarnings
    {
        get => _aiSupportResultWarnings;
        set => this.RaiseAndSetIfChanged(ref _aiSupportResultWarnings, value);
    }

    public bool AiSupportIsLoading
    {
        get => _aiSupportIsLoading;
        set => this.RaiseAndSetIfChanged(ref _aiSupportIsLoading, value);
    }

    public bool AiSupportPrivacyAccepted
    {
        get => _aiSupportPrivacyAccepted;
        set => this.RaiseAndSetIfChanged(ref _aiSupportPrivacyAccepted, value);
    }

    public void ShowAiSupport()
    {
        IsAiSupportVisible = true;
        AiSupportResultSummary = string.Empty;
        AiSupportResultSteps = string.Empty;
        AiSupportResultWarnings = string.Empty;
        AiSupportProblem = string.Empty;
    }

    public void ShowAiSupportForInstallFailure(string modName, string errorMessage, string logText)
    {
        IsAiSupportVisible = true;
        AiSupportResultSummary = string.Empty;
        AiSupportResultSteps = string.Empty;
        AiSupportResultWarnings = string.Empty;
        AiSupportIncludeDiagnostics = false;

        var safeLog = BuildInstallFailureLogSummary(logText);
        AiSupportProblem = _localizationService.GetFormatted(
            "AiSupport.InstallFailureProblemTemplate",
            modName,
            errorMessage,
            safeLog);
    }

    private static string BuildInstallFailureLogSummary(string? logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
            return string.Empty;

        var lines = logText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(RedactInstallFailureLogLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(20);

        var summary = string.Join(Environment.NewLine, lines);
        const int maxLength = 4000;
        return summary.Length <= maxLength ? summary : summary[^maxLength..];
    }

    private static string RedactInstallFailureLogLine(string line)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            line = line.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            line = line.Replace(appData, "%APPDATA%", StringComparison.OrdinalIgnoreCase);

        return line;
    }

    public void HideAiSupport()
    {
        IsAiSupportVisible = false;
    }

    public async Task AnalyzeProblemAsync()
    {
        if (string.IsNullOrWhiteSpace(AiSupportProblem) || AiSupportProblem.Length < 10)
        {
            AiSupportResultSummary = _localizationService.Get("AiSupport.Errors.Validation");
            return;
        }

        AiSupportIsLoading = true;
        AiSupportResultSummary = string.Empty;
        AiSupportResultSteps = string.Empty;

        try
        {
            var userSettings = _userSettingsService.LoadUserSettings();
            var language = SupportDiagnosticContextBuilder.NormalizeLanguage(userSettings.Language);

            // Zbuduj kontekst diagnostyczny jeśli checkbox włączony
            SUSModder.Core.Api.Support.SupportDiagnosticsInfo? diagnostics = null;
            if (AiSupportIncludeDiagnostics && _lastLaunchResult != null)
            {
                var ctxBuilder = new SUSModder.Core.Api.Support.SupportDiagnosticContextBuilder();
                diagnostics = ctxBuilder.BuildFrom(_lastLaunchResult);
            }

            var request = new SUSModder.Core.Api.Support.SupportQueryRequest
            {
                Language = language,
                Problem = SUSModder.Core.Api.Support.SupportDiagnosticContextBuilder.RedactProblem(AiSupportProblem),
                App = new SUSModder.Core.Api.Support.SupportAppInfo
                {
                    Version = _appVersion,
                    PlatformMode = userSettings.Mode ?? "steam",
                    UpdateChannel = userSettings.UpdateChannel ?? "release"
                },
                Diagnostics = diagnostics
            };

            // Użyj tymczasowego HTTP clienta jeśli nie mamy API clienta
            var supportBaseUrl = _configuration?["Configuration:BaseUrl"] ?? "https://susmodder.app";
            supportBaseUrl = supportBaseUrl.TrimEnd('/') + "/api/v2/support";

            var client = new SUSModder.Core.Api.Support.SupportAssistantClient(
                supportBaseUrl, _diagnosticsOutput ?? new SUSModder.Core.Diagnostics.BufferingDiagnosticsOutput());

            var response = await client.QueryAsync(request);

            if (response == null)
            {
                AiSupportResultSummary = _localizationService.Get("AiSupport.Errors.ServiceUnavailable");
                AiSupportResultSteps = _localizationService.Get("AiSupport.Errors.Timeout");
                return;
            }

            _aiSupportSessionId = response.SupportSessionId;

            var resultText = $"📋 {response.Summary}\n\n";
            resultText += $"🔄 {_localizationService.Get("AiSupport.Result.Source")}: {response.Source}\n";
            resultText += $"✅ {_localizationService.Get("AiSupport.Result.Confidence")}: {response.Confidence}\n\n";

            if (response.Steps.Count > 0)
            {
                resultText += $"── {_localizationService.Get("AiSupport.Result.Steps")} ──\n";
                for (int i = 0; i < response.Steps.Count; i++)
                {
                    var step = response.Steps[i];
                    var adminBadge = step.RequiresAdmin
                        ? $" ⚠️ {_localizationService.Get("AiSupport.Result.RequiresAdmin")}" : "";
                    resultText += $"{i + 1}. {step.Text}{adminBadge}\n";
                }
            }

            AiSupportResultSummary = resultText;
            AiSupportResultWarnings = response.Warnings != null && response.Warnings.Count > 0
                ? "⚠️ " + string.Join("\n⚠️ ", response.Warnings) : string.Empty;

            if (response.SafetyNotice.Length > 0)
                AiSupportResultWarnings += $"\n\n🛡️ {response.SafetyNotice}";

            AiSupportResultSteps = response.NeedsDiagnosticReport
                ? _localizationService.Get("AiSupport.Actions.GenerateReport") : string.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiSupport] Error: {ex.Message}");
            AiSupportResultSummary = _localizationService.Get("AiSupport.Errors.ServiceUnavailable");
        }
        finally
        {
            AiSupportIsLoading = false;
        }
    }

    public async Task SendAiSupportFeedbackAsync(bool helped)
    {
        if (string.IsNullOrWhiteSpace(_aiSupportSessionId))
            return;

        try
        {
            var supportBaseUrl = (_configuration?["Configuration:BaseUrl"] ?? "https://susmodder.app")
                .TrimEnd('/') + "/api/v2/support";

            var client = new SUSModder.Core.Api.Support.SupportAssistantClient(
                supportBaseUrl, _diagnosticsOutput ?? new BufferingDiagnosticsOutput());

            await client.SendFeedbackAsync(new SUSModder.Core.Api.Support.SupportFeedbackRequest
            {
                SupportSessionId = _aiSupportSessionId,
                Result = helped ? "helped" : "not_helped",
                DiagnosisCodes = _lastLaunchResult?.DiagnosisCodes?.Take(10).ToList(),
                Language = SupportDiagnosticContextBuilder.NormalizeLanguage(
                    _userSettingsService.LoadUserSettings().Language)
            });
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Otwiera folder moda w Eksploratorze.
    /// </summary>
    public void OpenModFolder()
    {
        var modConfig = GetCurrentModConfig();
        if (modConfig != null && !string.IsNullOrWhiteSpace(modConfig.InstallPath))
        {
            Process.Start("explorer.exe", modConfig.InstallPath);
        }
    }

    /// <summary>
    /// Otwiera folder BepInEx/logi.
    /// </summary>
    public void OpenBepInExLogs()
    {
        var modConfig = GetCurrentModConfig();
        if (modConfig != null && !string.IsNullOrWhiteSpace(modConfig.InstallPath))
        {
            var bepInExDir = Path.Combine(modConfig.InstallPath, "BepInEx");
            if (Directory.Exists(bepInExDir))
                Process.Start("explorer.exe", bepInExDir);
        }
    }

    /// <summary>
    /// Generuje support bundle ZIP.
    /// </summary>
    public async Task GenerateSupportBundleAsync()
    {
        if (_lastLaunchResult == null) return;

        var outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SUSModder", "support");

        var bundleService = new SupportBundleService();
        var bundlePath = await bundleService.GenerateBundleAsync(_lastLaunchResult, outputDir, anonymize: true);

        if (bundlePath != null)
        {
            Process.Start("explorer.exe", $"/select,\"{bundlePath}\"");
        }
    }

    private ModConfiguration? GetCurrentModConfig()
    {
        if (SelectedMod == null) return null;
        var configService = new SUSModder.Core.Services.ConfigService();
        return configService.LoadConfig()
            .FirstOrDefault(c => c.ModName == SelectedMod.Name);
    }
}
