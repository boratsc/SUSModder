using SUSModder.Core.Diagnostics;
using SUSModder.Core.Utilities;

namespace SUSModder.E2E.Tests;

/// <summary>
/// Test implementations for SUSModder.Core interfaces used in E2E tests.
/// </summary>

/// <summary>
/// Simple diagnostics output that writes to Debug.WriteLine and collects lines.
/// </summary>
internal sealed class E2EDiagnosticsOutput : IDiagnosticsOutput
{
    private readonly List<string> _lines = [];
    public IReadOnlyList<string> Lines => _lines;

    public void Write(string message)
    {
        _lines.Add(message);
        System.Diagnostics.Debug.WriteLine($"[E2E] {message}");
    }
}

/// <summary>
/// Simple progress reporter that records the last progress value and message.
/// </summary>
internal sealed class E2EProgressReporter : IProgressReporter
{
    public int LastPercent { get; private set; }
    public string LastMessage { get; private set; } = string.Empty;

    public void Report(int percent, string? message = null)
    {
        LastPercent = percent;
        LastMessage = message ?? string.Empty;
        System.Diagnostics.Debug.WriteLine($"[E2E Progress] {percent}%: {message ?? "(no message)"}");
    }
}
