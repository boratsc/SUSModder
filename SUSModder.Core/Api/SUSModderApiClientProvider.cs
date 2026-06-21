namespace SUSModder.Core.Api;

/// <summary>
/// Udostępnia domyślną instancję klienta API dla kodu statycznego i serwisów tworzonych przez new().
/// Ustawiana podczas startu aplikacji z kontenera DI.
/// </summary>
public static class SUSModderApiClientProvider
{
    private static ISUSModderApiClient? _default;

    public static ISUSModderApiClient Instance =>
        _default ?? throw new InvalidOperationException(
            "ISUSModderApiClient nie został zarejestrowany. Upewnij się, że App.ConfigureServices() wywołało SetDefault().");

    public static ISUSModderApiClient? TryGetDefault() => _default;

    public static void SetDefault(ISUSModderApiClient client)
    {
        _default = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Przywraca stan początkowy (brak domyślnego klienta). Przeznaczone wyłącznie
    /// dla testów jednostkowych, które muszą odizolować się od siebie.
    /// </summary>
    internal static void ResetForTests() => _default = null;
}
