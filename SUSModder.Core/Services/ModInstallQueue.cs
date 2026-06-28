namespace SUSModder.Core.Services;

public enum ModQueueOperation
{
    Install,
    Uninstall,
    Update
}

public sealed class ModQueueItem
{
    public required int ModId { get; init; }
    public required string ModName { get; init; }
    public ModQueueOperation Operation { get; init; }
}

public sealed class ModInstallQueueProgress
{
    public int CurrentIndex { get; init; }
    public int Total { get; init; }
    public string CurrentModName { get; init; } = string.Empty;
    public ModQueueOperation Operation { get; init; }
}

public sealed class ModInstallQueueItemResult
{
    public required string ModName { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Sekwencyjna kolejka operacji na modach (bulk install / uninstall / update).
/// Błąd jednego elementu nie przerywa pozostałych.
/// </summary>
public sealed class ModInstallQueue
{
    public async Task<IReadOnlyList<ModInstallQueueItemResult>> RunAsync(
        IReadOnlyList<ModQueueItem> items,
        Func<ModQueueItem, CancellationToken, Task<ModInstallQueueItemResult>> processItemAsync,
        IProgress<ModInstallQueueProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ModInstallQueueItemResult>(items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];

            progress?.Report(new ModInstallQueueProgress
            {
                CurrentIndex = i + 1,
                Total = items.Count,
                CurrentModName = item.ModName,
                Operation = item.Operation
            });

            try
            {
                results.Add(await processItemAsync(item, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                results.Add(new ModInstallQueueItemResult
                {
                    ModName = item.ModName,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        return results;
    }
}
