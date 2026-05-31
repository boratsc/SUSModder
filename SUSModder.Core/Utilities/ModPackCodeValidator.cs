using System.Text.RegularExpressions;

namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Walidacja formatu kodu paczki: XXXX-XXXX-XXXX (alfabet bez I,O,0,1).
    /// </summary>
    public static partial class ModPackCodeValidator
    {
        private static readonly Regex PackCodeRegex = PackCodeRegexFactory();

        [GeneratedRegex(@"^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", RegexOptions.CultureInvariant)]
        private static partial Regex PackCodeRegexFactory();

        public static bool IsValid(string? packCode) =>
            !string.IsNullOrWhiteSpace(packCode) && PackCodeRegex.IsMatch(packCode.Trim().ToUpperInvariant());

        public static string Normalize(string packCode) => packCode.Trim().ToUpperInvariant();
    }
}
