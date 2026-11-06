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

    /// <summary>
    /// Kreator zamknięty/zatrzymany w trakcie wykonania — zwraca to, co REALNIE powstało
    /// (instancja, kod paczki), bez obietnicy rollbacku. Host odświeża listy i pokazuje stan częściowy.
    /// </summary>
    public bool WasCancelled { get; init; }

    /// <summary>Kod zestawu utworzonego na serwerze przed zatrzymaniem (finalize/upload nie doszły).</summary>
    public string? CreatedPackCodeBeforeStop { get; init; }
}
