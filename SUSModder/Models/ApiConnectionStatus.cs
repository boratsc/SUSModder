namespace SUSModder.Models
{
    /// <summary>
    /// Status połączenia z serwerem API
    /// </summary>
    public enum ApiConnectionStatus
    {
        /// <summary>
        /// Połączenie działa prawidłowo
        /// </summary>
        Online,
        
        /// <summary>
        /// Brak połączenia z serwerem
        /// </summary>
        Offline,
        
        /// <summary>
        /// Sprawdzanie statusu połączenia
        /// </summary>
        Checking
    }
}
