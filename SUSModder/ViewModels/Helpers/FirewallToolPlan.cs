using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Views;

namespace SUSModder.ViewModels.Helpers;

/// <summary>Decyzja narzędzia firewalla po odczycie istniejących reguł (bez elewacji).</summary>
public enum FirewallToolDecision
{
    /// <summary>Brak własnych reguł — tylko dodaj.</summary>
    Add,
    /// <summary>Własne reguły istnieją — usuń swoje i dodaj ponownie.</summary>
    Replace,
    /// <summary>Reguła o tej nazwie wskazuje na inny program — nie ruszamy cudzych ustawień.</summary>
    Conflict
}

/// <summary>
/// Faza 3b: czysta logika narzędzia firewalla (kandydaci, ownership pre-check, mapowanie wyniku).
/// Testowalna bez UI i bez netsh — wykonanie z elewacją zostaje w MainWindowViewModel.
/// </summary>
public static class FirewallToolPlan
{
    /// <summary>
    /// Którykolwiek odczyt istnieje i wskazuje inny program → Conflict (nic nie usuwamy).
    /// Oba zgodne lub jeden zgodny a drugi null → Replace. Oba null → Add.
    /// </summary>
    public static FirewallToolDecision Decide(string? inboundOutput, string? outboundOutput, string targetExePath)
    {
        if ((inboundOutput is not null && !FirewallRuleService.IsOwnRule(inboundOutput, targetExePath)) ||
            (outboundOutput is not null && !FirewallRuleService.IsOwnRule(outboundOutput, targetExePath)))
            return FirewallToolDecision.Conflict;

        return inboundOutput is not null || outboundOutput is not null
            ? FirewallToolDecision.Replace
            : FirewallToolDecision.Add;
    }

    /// <summary>
    /// Lista kandydatów: vanilla bieżącego trybu + mody full. Steam wymaga istniejącego exe;
    /// Epic dopuszcza predykcję (exe może jeszcze nie istnieć — IsPredicted).
    /// </summary>
    public static List<FirewallModItem> BuildCandidates(
        string mode,
        string? steamVanillaInstallDir,
        string modsInstallPath,
        IEnumerable<(string Name, string? InstallPath)> fullMods,
        string vanillaDisplayName,
        Func<string, bool> fileExists)
    {
        var isEpic = string.Equals(mode, "epic", StringComparison.OrdinalIgnoreCase);
        var items = new List<FirewallModItem>();

        // Vanilla: reguły "Among Us - Vanilla" (nazwa reguły tworzona przez to narzędzie).
        string? vanillaExe = isEpic
            ? EpicInstallPathResolver.GetVanillaExpectedExePath(modsInstallPath)
            : string.IsNullOrWhiteSpace(steamVanillaInstallDir)
                ? null
                : Path.Combine(steamVanillaInstallDir, "Among Us.exe");
        if (vanillaExe is not null && (isEpic || fileExists(vanillaExe)))
        {
            items.Add(new FirewallModItem
            {
                Name = vanillaDisplayName,
                ExePath = vanillaExe,
                IsPredicted = !fileExists(vanillaExe),
                IsVanilla = true
            });
        }

        foreach (var (name, installPath) in fullMods)
        {
            if (string.IsNullOrWhiteSpace(installPath))
                continue;

            string exePath;
            if (isEpic)
            {
                var baseDir = EpicInstallPathResolver.GetBaseInstallDirectory(installPath, modsInstallPath, name);
                exePath = EpicInstallPathResolver.GetExpectedGameExePath(baseDir);
            }
            else
            {
                exePath = Path.Combine(installPath, "Among Us.exe");
                if (!fileExists(exePath))
                    continue;
            }

            items.Add(new FirewallModItem
            {
                Name = name,
                ExePath = exePath,
                IsPredicted = !fileExists(exePath)
            });
        }

        return items;
    }

    /// <summary>Mapuje wynik skryptu na klucz komunikatu (konflikt ma priorytet).</summary>
    public static string OutcomeMessageKey(FirewallRuleOutcome outcome) =>
        outcome.NameConflict ? "UI.Repair.Firewall.NameConflict"
        : outcome.InboundOk && outcome.OutboundOk ? "UI.Repair.Firewall.Success"
        : "UI.Repair.Firewall.PartialSuccess";

    /// <summary>
    /// Czyta plik wyjścia cmd. Acceptance 2026-11-16 (Win11 26300 PL): netsh pisze wyjście jako UTF-8,
    /// więc najpierw strict UTF-8; fallback OEM dla starszych systemów / locale netsh (jak DepotDownloaderRunner).
    /// Brak pliku = pusty wynik (neutralnie).
    /// </summary>
    public static string ReadOutputFile(string path)
    {
        if (!File.Exists(path))
            return string.Empty;

        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart((char)0xFEFF);
        }
        catch (DecoderFallbackException)
        {
            // Niepoprawny UTF-8 → wyjście bajtami OEM (starsze systemy / locale netsh).
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var oemCodePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            if (oemCodePage > 0)
                return Encoding.GetEncoding(oemCodePage).GetString(bytes);
        }
        catch (ArgumentException)
        {
            // Niedostępna strona kodowa — fallback poniżej.
        }

        return File.ReadAllText(path);
    }
}
