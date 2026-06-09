using System.Collections.Generic;

namespace SUSModder.Core.Diagnostics;

/// <summary>
/// Buforuje linie logu instalacji i opcjonalnie przekazuje je do innego sinka.
/// </summary>
public sealed class BufferingDiagnosticsOutput : IDiagnosticsOutput
{
    private readonly IDiagnosticsOutput? _inner;
    private readonly List<string> _lines = [];

    public BufferingDiagnosticsOutput(IDiagnosticsOutput? inner = null)
    {
        _inner = inner;
    }

    public IReadOnlyList<string> Lines => _lines;

    public void Write(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _lines.Add(message);

        _inner?.Write(message);
    }
}
