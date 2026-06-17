using System;

namespace SUSModder.Core.Services.Discord;

/// <summary>
/// Błąd odszyfrowania danych uwierzytelniających (np. DPAPI po zmianie profilu Windows).
/// </summary>
public sealed class CredentialProtectionException : Exception
{
    public CredentialProtectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
