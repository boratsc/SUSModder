using System.Collections.Generic;
using SUSModder.Core.Models;

namespace SUSModder.Views;

public sealed class ModPackCreatorDialogResult
{
    public ModPackCreatorMode Mode { get; init; }
    public ModPackCreateResult? ShareResult { get; init; }
    public string? CreatedInstanceId { get; init; }
    public IReadOnlyList<int> InstalledDllModIds { get; init; } = [];
    public IReadOnlyList<string> FailedDllNames { get; init; } = [];
}
