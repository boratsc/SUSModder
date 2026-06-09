namespace SUSModder.Core.Services;

/// <summary>
/// Udostępnia singleton CatalogSyncService dla kodu statycznego (ConfigManager, ModUpdateManager).
/// </summary>
public static class CatalogSyncServiceProvider
{
    private static CatalogSyncService? _default;

    public static void SetDefault(CatalogSyncService service) =>
        _default = service ?? throw new ArgumentNullException(nameof(service));

    public static CatalogSyncService? TryGetDefault() => _default;
}
