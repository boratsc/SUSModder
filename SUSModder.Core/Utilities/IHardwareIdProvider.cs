namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Interfejs dostarczający anonimowy hash użytkownika (SHA256 z Hardware ID).
    /// Abstrakcja umożliwiająca DI i testowanie.
    /// </summary>
    public interface IHardwareIdProvider
    {
        /// <summary>
        /// Pobiera anonimowy hash użytkownika (SHA256 z Hardware ID).
        /// Zwraca 64-znakowy hex string.
        /// </summary>
        string GetAnonymousUserHash();
    }
}
