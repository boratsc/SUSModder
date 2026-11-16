using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SUSModder.Core.Services;

public sealed record FirewallRuleRequest(string RuleBaseName, string ProgramPath);
public sealed record FirewallRuleOutcome(bool InboundOk, bool OutboundOk, bool NameConflict, string RawOutput);

public static class FirewallRuleService
{
    // netsh labels ("Rule Name:", "Program:", "Ok.", "Deleted N rule(s).", "No rules match...") were empirically verified as English/invariant on Polish Windows 11 build 26300 (acceptance 2026-11-16; susmodder-fw-acceptance-out.txt and enc-test). Other OS languages are unverified; if netsh localizes these, parsing fails closed (false/false), never as success.
    private static readonly Regex ProgramLine = new(@"^Program:\s+(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex RuleNameLine = new(@"^Rule Name:\s+(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static string BuildRuleBaseName(string modDisplayName)
    {
        var safeName = new string((modDisplayName ?? string.Empty)
            .Select(c => char.IsControl(c) || c == '"' ? ' ' : c).ToArray()).Trim();
        return $"Among Us - {(string.IsNullOrWhiteSpace(safeName) ? "Among Us" : safeName)}";
    }

    public static bool IsValidProgramPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) &&
        !path.Any(c => c == '"' || char.IsControl(c));

    public static string BuildNetshScript(FirewallRuleRequest request, bool deleteOwnFirst)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidProgramPath(request.ProgramPath))
            throw new ArgumentException("Invalid program path.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RuleBaseName) || request.RuleBaseName.Any(c => c == '"' || char.IsControl(c)))
            throw new ArgumentException("Invalid rule base name.", nameof(request));
        var inbound = $"{request.RuleBaseName} (Inbound)";
        var outbound = $"{request.RuleBaseName} (Outbound)";
        var lines = new List<string>();
        if (deleteOwnFirst)
        {
            // shortcut: pre-check przed elewacją — milisekundowe okno TOCTOU na podmianę reguły; akceptowalne, delete tylko po matchu ścieżki.
            lines.Add($"advfirewall firewall delete rule name=\"{inbound}\"");
            lines.Add($"advfirewall firewall delete rule name=\"{outbound}\"");
        }
        lines.Add($"advfirewall firewall add rule name=\"{inbound}\" dir=in action=allow program=\"{request.ProgramPath}\" enable=yes");
        lines.Add($"advfirewall firewall add rule name=\"{outbound}\" dir=out action=allow program=\"{request.ProgramPath}\" enable=yes");
        lines.Add($"advfirewall firewall show rule name=\"{inbound}\" verbose");
        lines.Add($"advfirewall firewall show rule name=\"{outbound}\" verbose");
        return string.Join(Environment.NewLine, lines);
    }

    public static string? ParseProgramPath(string showVerboseOutput) =>
        ProgramLine.Match(showVerboseOutput ?? string.Empty) is { Success: true } match
            ? match.Groups[1].Value.Trim()
            : null;

    public static bool IsOwnRule(string? showVerboseOutput, string expectedProgramPath) =>
        ParseProgramPath(showVerboseOutput ?? string.Empty) is { } path &&
        string.Equals(path, expectedProgramPath, StringComparison.OrdinalIgnoreCase);

    public static FirewallRuleOutcome ParseScriptOutput(string output, string expectedProgramPath)
    {
        output ??= string.Empty;
        var matches = RuleNameLine.Matches(output);
        var blocks = matches.Cast<Match>().Select((match, index) =>
        {
            var end = index + 1 < matches.Count ? matches[index + 1].Index : output.Length;
            return (Name: match.Groups[1].Value.Trim(), Text: output[match.Index..end]);
        }).ToArray();
        var inboundBlock = blocks.FirstOrDefault(b => b.Name.EndsWith("(Inbound)", StringComparison.OrdinalIgnoreCase));
        var outboundBlock = blocks.FirstOrDefault(b => b.Name.EndsWith("(Outbound)", StringComparison.OrdinalIgnoreCase));
        string? Program((string Name, string Text) block) => block.Name is null ? null : ParseProgramPath(block.Text);
        var inboundProgram = Program(inboundBlock);
        var outboundProgram = Program(outboundBlock);
        var conflict = blocks.Any(b => ParseProgramPath(b.Text) is { } path &&
            !string.Equals(path, expectedProgramPath, StringComparison.OrdinalIgnoreCase));
        return new(
            inboundProgram is not null && string.Equals(inboundProgram, expectedProgramPath, StringComparison.OrdinalIgnoreCase),
            outboundProgram is not null && string.Equals(outboundProgram, expectedProgramPath, StringComparison.OrdinalIgnoreCase),
            conflict, output);
    }

    public static async Task<string?> TryShowRuleVerboseAsync(string ruleBaseName, string direction, CancellationToken ct = default)
    {
        Process? process = null;
        try
        {
            using (process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall show rule name=\"{ruleBaseName} ({direction})\" verbose",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            })
            {
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0 && ParseProgramPath(output) is not null ? output : null;
            }
        }
        catch
        {
            if (ct.IsCancellationRequested && process is not null)
                try { process.Kill(entireProcessTree: true); } catch { }
            return null;
        }
    }
}
