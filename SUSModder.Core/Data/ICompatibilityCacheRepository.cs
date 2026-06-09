namespace SUSModder.Core.Data;

public interface ICompatibilityCacheRepository
{
    CompatibilityCacheEntry? GetPair(int fullModId, string fullModVersion, int dllModId, string dllModVersion);

    IReadOnlyList<CompatibilityCacheEntry> GetForFullMod(int fullModId, string fullModVersion);

    IReadOnlyList<CompatibilityCacheEntry> GetForDllMod(int dllModId, string dllModVersion);

    void SaveSnapshot(IEnumerable<CompatibilityCacheEntry> entries, string? revision, DateTime fetchedAtUtc);

    int Count();
}
