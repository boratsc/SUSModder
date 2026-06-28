using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.Versioning;
using SUSModder.Core.Diagnostics.Launch;

namespace SUSModder.Core.Diagnostics;

/// <summary>
/// Best-effort odczyt zdarzeń Windows Defender i Controlled Folder Access.
/// Koreluje zdarzenia w oknie czasowym z folderem moda lub Among Us.exe.
/// Jeśli brak uprawnień lub kanał event log jest niedostępny, zwraca events_unavailable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSecurityDiagnostics
{
    private const string DefenderOperationalLog = "Microsoft-Windows-Windows Defender/Operational";

    // Interesujące Event ID wg dokumentacji Microsoft
    private static readonly HashSet<int> RelevantEventIds =
    [
        1116,  // Malware detected
        1118,  // Malware remediation started
        1119,  // Malware remediation succeeded
        1160,  // PUA detected
        1121,  // Attack Surface Reduction (ASR) block
        1123,  // Controlled Folder Access – block
        1124,  // Controlled Folder Access – audit
        1127,  // Controlled Folder Access – block (alternate)
        1128,  // Controlled Folder Access – audit (alternate)
        5007   // Settings changed (e.g., exclusion added/removed)
    ];

    /// <summary>
    /// Wyszukuje zdarzenia Defender w oknie czasowym wokół launchu.
    /// Zwraca kody diagnozy i znalezione Event ID.
    /// </summary>
    /// <param name="modPath">Ścieżka folderu moda (do dopasowania w payload zdarzenia).</param>
    /// <param name="exePath">Ścieżka do Among Us.exe (do dopasowania w payload zdarzenia).</param>
    /// <param name="startedAtUtc">Czas startu gry – okno od -2min do +1min.</param>
    /// <param name="maxResults">Maksymalna liczba zwróconych zdarzeń.</param>
    public WindowsSecurityCorrelationResult QueryDefenderEvents(
        string? modPath,
        string? exePath,
        DateTimeOffset startedAtUtc,
        int maxResults = 50)
    {
        var result = new WindowsSecurityCorrelationResult();

        try
        {
            // Sprawdź czy kanał event log jest dostępny (best-effort przez próbę odczytu)
            var fromTime = startedAtUtc.AddMinutes(-2);
            var toTime = startedAtUtc.AddMinutes(1);

            // Budowa query XPath dla Windows Event Log
            var eventIdsFilter = string.Join(" or ", RelevantEventIds.Select(id =>
                $"Event/System/EventID={id}"));

            var timeFilter = $"@SystemTime&gt;='{fromTime.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.000Z}' " +
                             $"and @SystemTime&lt;='{toTime.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.000Z}'";

            var query = $"*[System[({eventIdsFilter}) and {timeFilter}]]";

            using var reader = new EventLogReader(
                new EventLogQuery(DefenderOperationalLog, PathType.LogName, query)
                {
                    ReverseDirection = false // od najstarszych
                });

            var count = 0;
            EventRecord? eventRecord;
            while ((eventRecord = reader.ReadEvent()) != null && count < maxResults)
            {
                var eventId = eventRecord.Id;
                var timeCreated = eventRecord.TimeCreated;
                var eventData = ExtractEventData(eventRecord);

                // Korelacja: czy payload zawiera ścieżkę moda lub exe?
                var isRelevant = IsPathRelevant(eventData, modPath, exePath);

                result.FoundEvents.Add(new SecurityEventInfo
                {
                    EventId = (int)eventId,
                    TimeCreated = timeCreated ?? DateTime.MinValue,
                    Data = eventData.Length > 500 ? eventData[..500] : eventData,
                    IsRelevant = isRelevant
                });

                if (isRelevant)
                {
                    result.RelevantEvents.Add((int)eventId);

                    // Klasyfikacja do kodów diagnozy
                    if (eventId == 1116 || eventId == 1160)
                        result.DiagnosisCodes.Add(DiagnosisCode.DefenderThreatDetected);
                    else if (eventId == 1123 || eventId == 1127)
                        result.DiagnosisCodes.Add(DiagnosisCode.DefenderCfaBlocked);
                    else if (eventId == 1121)
                        result.DiagnosisCodes.Add(DiagnosisCode.DefenderCfaBlocked);
                    else if (eventId == 5007)
                        result.DiagnosisCodes.Add(DiagnosisCode.DefenderEventsUnavailable); // settings changed – info
                }

                count++;
            }
        }
        catch (EventLogReadingException)
        {
            // Brak uprawnień lub kanał niedostępny
            result.EventsUnavailable = true;
            result.DiagnosisCodes.Add(DiagnosisCode.DefenderEventsUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            result.EventsUnavailable = true;
            result.DiagnosisCodes.Add(DiagnosisCode.DefenderEventsUnavailable);
        }
        catch (Exception)
        {
            // Best-effort – nie rzucamy dalej
            result.EventsUnavailable = true;
        }

        return result;
    }

    /// <summary>
    /// Wyciąga tekst z payload zdarzenia (EventData lub Message).
    /// </summary>
    private static string ExtractEventData(EventRecord record)
    {
        try
        {
            // Spróbuj odczytać XML eventu jako string
            var xml = record.ToXml();
            return xml ?? string.Empty;
        }
        catch
        {
            try { return record.FormatDescription() ?? string.Empty; }
            catch { return string.Empty; }
        }
    }

    /// <summary>
    /// Sprawdza czy payload zdarzenia zawiera ścieżkę moda lub Among Us.exe.
    /// </summary>
    private static bool IsPathRelevant(string eventData, string? modPath, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(eventData))
            return false;

        // Sprawdź czy event dotyczy ścieżki moda
        if (!string.IsNullOrWhiteSpace(modPath) &&
            eventData.Contains(modPath, StringComparison.OrdinalIgnoreCase))
            return true;

        // Sprawdź czy event dotyczy Among Us.exe
        if (!string.IsNullOrWhiteSpace(exePath) &&
            eventData.Contains(exePath, StringComparison.OrdinalIgnoreCase))
            return true;

        // Sprawdź obecność "Among Us" lub "BepInEx" w zdarzeniu
        if (eventData.Contains("Among Us", StringComparison.OrdinalIgnoreCase) ||
            eventData.Contains("BepInEx", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}

/// <summary>
/// Wynik korelacji z Windows Event Log.
/// </summary>
public sealed class WindowsSecurityCorrelationResult
{
    /// <summary>Czy nie udało się odczytać event logu (brak uprawnień, kanał wyłączony).</summary>
    public bool EventsUnavailable { get; set; }

    /// <summary>Kody diagnozy.</summary>
    public List<string> DiagnosisCodes { get; init; } = [];

    /// <summary>Wszystkie znalezione zdarzenia w oknie czasowym.</summary>
    public List<SecurityEventInfo> FoundEvents { get; init; } = [];

    /// <summary>Event ID zdarzeń skorelowanych z mod/AmongUs.</summary>
    public List<int> RelevantEvents { get; init; } = [];
}

/// <summary>
/// Informacja o pojedynczym zdarzeniu z Windows Event Log.
/// </summary>
public sealed class SecurityEventInfo
{
    public int EventId { get; set; }
    public DateTime TimeCreated { get; set; }
    public string Data { get; set; } = string.Empty;
    public bool IsRelevant { get; set; }
}
