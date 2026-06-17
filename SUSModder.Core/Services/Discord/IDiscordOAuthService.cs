using System.Threading.Tasks;

namespace SUSModder.Core.Services.Discord;

/// <summary>
/// Zarządza flow Discord OAuth2 PKCE: logowanie, odświeżanie tokena, wylogowanie.
/// </summary>
public interface IDiscordOAuthService
{
    /// <summary>
    /// Rozpoczyna flow logowania Discord OAuth2 PKCE.
    /// Generuje code_verifier, pobiera client_id z Clair API i zwraca URL do otwarcia w przeglądarce.
    /// </summary>
    Task<OAuthStartResult> StartLoginAsync();

    /// <summary>
    /// Kończy flow logowania — wymienia kod autoryzacyjny na tokeny, zapisuje je w bazie.
    /// </summary>
    /// <param name="code">Kod autoryzacyjny z callbacku OAuth</param>
    /// <param name="redirectUri">Redirect URI użyty w żądaniu autoryzacji</param>
    /// <param name="state">State parameter z callbacku OAuth (weryfikowany przeciw CSRF)</param>
    Task<OAuthCompleteResult> CompleteLoginAsync(string code, string redirectUri, string? state = null);

    /// <summary>
    /// Sprawdza, czy użytkownik ma ważny token Discord (automatycznie odświeża w razie potrzeby).
    /// </summary>
    Task<bool> IsLoggedInAsync();

    /// <summary>
    /// Wymusza odświeżenie tokena Discord OAuth2.
    /// </summary>
    Task<bool> RefreshTokenAsync();

    /// <summary>
    /// Wylogowuje użytkownika — odwołuje token po stronie Discord i czyści lokalną bazę.
    /// </summary>
    Task LogoutAsync();

    /// <summary>
    /// Pobiera nazwę użytkownika Discord do wyświetlenia w UI.
    /// </summary>
    Task<string?> GetUsernameAsync();
}

/// <summary>
/// Wynik rozpoczęcia flow OAuth. Zawiera URL do otwarcia w przeglądarce oraz dane potrzebne do dokończenia flow.
/// </summary>
/// <param name="AuthUrl">URL do otwarcia w przeglądarce (Discord OAuth authorize)</param>
/// <param name="Port">Port lokalnego serwera nasłuchującego na callback</param>
/// <param name="CodeVerifier">Code verifier do weryfikacji PKCE (wymagany przy wymianie kodu na token)</param>
/// <param name="State">OAuth state parameter do weryfikacji CSRF</param>
public record OAuthStartResult(string AuthUrl, int Port, string CodeVerifier, string State);

/// <summary>
/// Wynik zakończenia flow OAuth — wymiany kodu na token.
/// </summary>
/// <param name="Success">Czy operacja się powiodła</param>
/// <param name="ErrorMessage">Komunikat błędu (jeśli Success = false)</param>
public record OAuthCompleteResult(bool Success, string? ErrorMessage);
