namespace SUSModder.Core.GameIntegration;

public sealed class ModInstallResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> LogLines { get; init; } = [];

    public static ModInstallResult Succeeded(IReadOnlyList<string>? logLines = null) => new()
    {
        Success = true,
        LogLines = logLines ?? []
    };

    public static ModInstallResult Failed(string errorMessage, IReadOnlyList<string>? logLines = null) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        LogLines = logLines ?? []
    };

    public string GetLogText() => LogLines.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, LogLines);
}
