using System;

namespace SUSModder.Core.Utilities;

/// <summary>
/// Mapuje kody wyjścia certutil/WinHTTP z naprawy certyfikatów na czytelne kategorie.
/// </summary>
public static class CertificateRepairErrorMapper
{
    public const string KindTimeout = "Timeout";
    public const string KindNameNotResolved = "NameNotResolved";
    public const string KindCannotConnect = "CannotConnect";
    public const string KindConnectionError = "ConnectionError";
    public const string KindSecureFailure = "SecureFailure";
    public const string KindAccessDenied = "AccessDenied";
    public const string KindCancelled = "Cancelled";
    public const string KindUnknown = "Unknown";

    public readonly record struct ClassifiedError(
        string Kind,
        int ExitCode,
        string HexCode,
        string TechnicalName);

    public static ClassifiedError Classify(int exitCode)
    {
        var code = unchecked((uint)exitCode);
        var hex = $"0x{code:X8}";

        return code switch
        {
            // ERROR_WINHTTP_TIMEOUT / ERROR_INTERNET_TIMEOUT
            0x80072EE2 => new(KindTimeout, exitCode, hex, "ERROR_WINHTTP_TIMEOUT"),
            // ERROR_WINHTTP_NAME_NOT_RESOLVED
            0x80072EE7 => new(KindNameNotResolved, exitCode, hex, "ERROR_WINHTTP_NAME_NOT_RESOLVED"),
            // ERROR_WINHTTP_CANNOT_CONNECT
            0x80072EFD => new(KindCannotConnect, exitCode, hex, "ERROR_WINHTTP_CANNOT_CONNECT"),
            // ERROR_WINHTTP_CONNECTION_ERROR
            0x80072EFE => new(KindConnectionError, exitCode, hex, "ERROR_WINHTTP_CONNECTION_ERROR"),
            // ERROR_WINHTTP_SECURE_FAILURE
            0x80072F8F => new(KindSecureFailure, exitCode, hex, "ERROR_WINHTTP_SECURE_FAILURE"),
            // ERROR_ACCESS_DENIED
            0x80070005 => new(KindAccessDenied, exitCode, hex, "ERROR_ACCESS_DENIED"),
            // ERROR_CANCELLED (UAC / user cancel — rzadko jako exit code procesu)
            0x800704C7 => new(KindCancelled, exitCode, hex, "ERROR_CANCELLED"),
            _ => new(KindUnknown, exitCode, hex, "UNKNOWN")
        };
    }

    /// <summary>
    /// Klucz lokalizacji szczegółu: UI.Repair.Certificates.Errors.{Kind}
    /// </summary>
    public static string GetDetailLocalizationKey(string kind) =>
        $"UI.Repair.Certificates.Errors.{kind}";
}
