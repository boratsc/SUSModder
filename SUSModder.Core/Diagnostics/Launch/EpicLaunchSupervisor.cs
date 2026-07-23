using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Core.Diagnostics.Launch;

/// <summary>
/// Epic/Legendary launch supervisor.
/// Nie startuje Among Us.exe bezpośrednio — Legendary robi to sam.
/// Używaj <see cref="LaunchSupervisor.ObserveExternalLaunchAsync"/>.
/// </summary>
public sealed class EpicLaunchSupervisor : LaunchSupervisor
{
    protected override Task<Process?> StartProcessAsync(LaunchContext context, CancellationToken ct)
        => throw new NotSupportedException(
            "Epic launch uses ObserveExternalLaunchAsync — Legendary starts the game process.");
}
