using System;
using System.Collections.Generic;
using System.Linq;

namespace SUSModder.Core.Validators
{
    /// <summary>
    /// Lekka, deterministyczna heurystyka klienta dla globalnego czatu.
    ///
    /// Rola: wyłącznie <b>ostrzeżenie przed wysłaniem</b> o treści wyglądającej na spam.
    /// Nie blokuje wiadomości i nie zastępuje moderacji serwera — backend pozostaje
    /// źródłem decyzji, a zmodyfikowany klient nie może być jedyną linią obrony.
    ///
    /// Zasady projektowe:
    /// - brak sieci, DB i logowania treści — czysta funkcja nad tekstem;
    /// - konserwatywne progi, aby nie straszyć użytkowników zwykłymi zdaniami;
    /// - pojedyncze przekleństwo, CAPS LOCK ani jedna emotka nie są sygnałem;
    /// - tokeny wyglądające na link/e-mail są pomijane, aby nie analizować adresów URL.
    /// </summary>
    public static class CommunityChatHeuristics
    {
        /// <summary>Powtórzenia tych samych znaków lub jednostek (np. „aaaaaa”, „chujchujchuj”).</summary>
        public const string RepeatedCharacters = "REPEATED_CHARACTERS";

        /// <summary>To samo słowo powtórzone wiele razy (np. „siema siema siema siema”).</summary>
        public const string RepeatedWords = "REPEATED_WORDS";

        /// <summary>Seria znaków interpunkcyjnych (np. „!!!!!!”).</summary>
        public const string ExcessivePunctuation = "EXCESSIVE_PUNCTUATION";

        private const int RepeatedCharacterRunThreshold = 6;
        private const int RepeatedUnitMinLength = 2;
        private const int RepeatedUnitMaxLength = 8;
        private const int RepeatedUnitMinRepeats = 3;
        private const int RepeatedWordMinOccurrences = 4;
        private const int RepeatedPunctuationRunThreshold = 6;

        /// <summary>
        /// Analizuje treść i zwraca stabilne kody sygnałów. Pusta lista = brak ostrzeżenia.
        /// </summary>
        public static CommunityChatHeuristicResult Analyze(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return CommunityChatHeuristicResult.None;

            var signals = new List<string>(3);

            var tokens = content
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(IsAnalyzableToken)
                .ToList();

            if (tokens.Count > 0)
            {
                if (tokens.Any(HasRepeatedCharacters) || tokens.Any(HasRepeatedUnit))
                    signals.Add(RepeatedCharacters);

                if (HasRepeatedWords(tokens))
                    signals.Add(RepeatedWords);
            }

            if (HasExcessivePunctuation(content))
                signals.Add(ExcessivePunctuation);

            return signals.Count == 0
                ? CommunityChatHeuristicResult.None
                : new CommunityChatHeuristicResult(signals);
        }

        /// <summary>
        /// Tokeny zawierające fragment adresu są pomijane — analiza nie może traktować
        /// „https://aaa…” albo adresu e-mail jako powtarzającej się treści.
        /// </summary>
        private static bool IsAnalyzableToken(string token)
        {
            if (token.Length == 0)
                return false;

            if (token.Contains("://", StringComparison.Ordinal))
                return false;

            if (token.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                return false;

            if (token.Contains('@') || token.Contains('/') || token.Contains('\\'))
                return false;

            return true;
        }

        private static bool HasRepeatedCharacters(string token)
        {
            var run = 0;
            char previous = '\0';
            foreach (var c in token)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    run = 0;
                    previous = '\0';
                    continue;
                }

                run = run > 0 && char.ToLowerInvariant(c) == char.ToLowerInvariant(previous) ? run + 1 : 1;
                previous = c;

                if (run >= RepeatedCharacterRunThreshold)
                    return true;
            }

            return false;
        }

        private static bool HasRepeatedUnit(string token)
        {
            var maxLength = Math.Min(RepeatedUnitMaxLength, token.Length / RepeatedUnitMinRepeats);
            for (var unitLength = RepeatedUnitMinLength; unitLength <= maxLength; unitLength++)
            {
                for (var start = 0; start + (unitLength * RepeatedUnitMinRepeats) <= token.Length; start++)
                {
                    if (!UnitIsAlphanumeric(token, start, unitLength))
                        continue;

                    var repeats = 1;
                    while (start + ((repeats + 1) * unitLength) <= token.Length &&
                           UnitEquals(token, start, start + (repeats * unitLength), unitLength))
                    {
                        repeats++;
                    }

                    if (repeats >= RepeatedUnitMinRepeats)
                        return true;
                }
            }

            return false;
        }

        private static bool UnitIsAlphanumeric(string token, int start, int length)
        {
            for (var i = 0; i < length; i++)
            {
                if (!char.IsLetterOrDigit(token[start + i]))
                    return false;
            }

            return true;
        }

        private static bool UnitEquals(string token, int left, int right, int length)
        {
            for (var i = 0; i < length; i++)
            {
                if (char.ToLowerInvariant(token[left + i]) != char.ToLowerInvariant(token[right + i]))
                    return false;
            }

            return true;
        }

        private static bool HasRepeatedWords(IReadOnlyCollection<string> tokens)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in tokens)
            {
                var word = raw.Trim(
                    '(', ')', '[', ']', '{', '}', '<', '>', '"', '\'', '`',
                    ',', '.', '!', '?', ';', ':', '…');

                if (word.Length < 3)
                    continue;

                counts.TryGetValue(word, out var count);
                count++;
                counts[word] = count;

                if (count >= RepeatedWordMinOccurrences)
                    return true;
            }

            return false;
        }

        private static bool HasExcessivePunctuation(string content)
        {
            var run = 1;
            for (var i = 1; i < content.Length; i++)
            {
                var current = content[i];
                if (IsRepeatedPunctuation(current) && current == content[i - 1])
                {
                    run++;
                    if (run >= RepeatedPunctuationRunThreshold)
                        return true;
                }
                else
                {
                    run = 1;
                }
            }

            return false;
        }

        private static bool IsRepeatedPunctuation(char c) => c is '!' or '?' or '…';
    }

    /// <summary>
    /// Wynik lokalnej heurystyki czatu. Kody są stabilne i mapowane na tekst w UI.
    /// </summary>
    public sealed record CommunityChatHeuristicResult(IReadOnlyList<string> SignalCodes)
    {
        /// <summary>Brak sygnałów — wiadomość nie wymaga dodatkowego potwierdzenia.</summary>
        public static CommunityChatHeuristicResult None { get; } =
            new(Array.Empty<string>());

        /// <summary>True, gdy UI powinno poprosić o potwierdzenie przed wysłaniem.</summary>
        public bool ShouldWarn => SignalCodes.Count > 0;
    }
}
