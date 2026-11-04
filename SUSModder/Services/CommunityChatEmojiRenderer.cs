using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using EmojiToolkit;

namespace SUSModder.Services;

/// <summary>
/// Prezentacyjna zamiana klasycznych emotikon ASCII na emoji Unicode w treści czatu.
///
/// Zasady:
/// - działa wyłącznie na potrzeby wyświetlania — oryginalna treść wiadomości,
///   szkic i żądanie do backendu pozostają nietknięte;
/// - emoji pochodzą z biblioteki EmojiToolkit (brak własnej tabeli znaków Unicode);
/// - obsługiwany jest świadomie ograniczony zestaw emotikon, a nie dowolny shortcode;
/// - fragmenty wyglądające na link, adres e-mail lub niekompletny schemat są pomijane,
///   dzięki czemu „https://”, „s://” i ścieżki URL nie zawierają zamienionych emotikon.
/// </summary>
public static class CommunityChatEmojiRenderer
{
    private const int MaxSupportedTokenLength = 3;

    /// <summary>
    /// Emotikony, których zamianę świadomie wspieramy. Wartość <c>Lookup</c> jest kluczem
    /// w EmojiToolkit — tam też znajduje się właściwy znak Unicode.
    /// </summary>
    private static readonly (string Text, string Lookup)[] SupportedTokens =
    {
        (":-)", ":)"),
        (":-(", ":("),
        (":-D", ":D"),
        (":-P", ":P"),
        (":-p", ":p"),
        (":-O", ":O"),
        (":-o", ":o"),
        (":-/", ":/"),
        (":-|", ":|"),
        (":')", ":')"),
        (":'(", ":'("),
        (";-)", ";)"),
        (":)", ":)"),
        (":(", ":("),
        (":D", ":D"),
        (":P", ":P"),
        (":p", ":p"),
        (":O", ":O"),
        (":o", ":o"),
        (":/", ":/"),
        (":|", ":|"),
        (":$", ":$"),
        (":*", ":*"),
        (":3", ":3"),
        (";)", ";)"),
        ("<3", "<3"),
        ("8)", "8)"),
        ("xD", "xD"),
        ("XD", "xD"),
        ("=)", ":)")
    };

    /// <summary>Tokeny posortowane od najdłuższego, aby „:-)” nie zostało dopasowane jako „:”.</summary>
    private static readonly (string Text, string? Emoji)[] OrderedTokens = BuildOrderedTokens();

    /// <summary>Zakresy tekstu, w których nie wolno niczego zamieniać (adresy URL, e-mail).</summary>
    private static readonly Regex ProtectedSpanRegex = new(
        @"\S*://\S*" +
        @"|(?<![\p{L}\p{N}])www\.[^\s]+" +
        @"|(?<![\p{L}\p{N}])[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}" +
        @"|(?<![\p{L}\p{N}])(?:[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,24}(?::\d{1,5})?(?:/[^\s]*)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Zwraca tekst gotowy do wyświetlenia; dla braku zmian zwraca ten sam obiekt.</summary>
    public static string Render(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return content ?? string.Empty;

        if (!content.Any(c => c is ':' or ';' or '8' or '<' or 'x' or 'X' or '='))
            return content;

        var spans = FindProtectedSpans(content);
        var builder = new StringBuilder(content.Length);
        var spanIndex = 0;
        var changed = false;

        for (var i = 0; i < content.Length;)
        {
            // Pomiń cały chroniony zakres (link/e-mail) bez zaglądania do środka.
            while (spanIndex < spans.Count && spans[spanIndex].End <= i)
                spanIndex++;

            if (spanIndex < spans.Count && spans[spanIndex].Start <= i)
            {
                var span = spans[spanIndex];
                builder.Append(content, i, span.End - i);
                i = span.End;
                spanIndex++;
                continue;
            }

            if (TryMatchEmoticon(content, i, out var tokenLength, out var emoji))
            {
                builder.Append(emoji);
                i += tokenLength;
                changed = true;
                continue;
            }

            builder.Append(content[i]);
            i++;
        }

        return changed ? builder.ToString() : content;
    }

    private static (string Text, string? Emoji)[] BuildOrderedTokens()
    {
        return SupportedTokens
            .OrderByDescending(token => token.Text.Length)
            .Select(token => (token.Text, Emoji.Get(token.Lookup)?.Raw))
            .ToArray();
    }

    private static bool TryMatchEmoticon(string content, int index, out int tokenLength, out string emoji)
    {
        tokenLength = 0;
        emoji = string.Empty;

        if (!IsLeftBoundary(content, index))
            return false;

        foreach (var token in OrderedTokens)
        {
            if (token.Text.Length > MaxSupportedTokenLength)
                continue;

            if (string.IsNullOrEmpty(token.Emoji))
                continue;

            if (index + token.Text.Length > content.Length)
                continue;

            if (string.CompareOrdinal(content, index, token.Text, 0, token.Text.Length) != 0)
                continue;

            if (!IsRightBoundary(content, index + token.Text.Length))
                continue;

            tokenLength = token.Text.Length;
            emoji = token.Emoji!;
            return true;
        }

        return false;
    }

    private static bool IsLeftBoundary(string content, int index)
    {
        if (index == 0)
            return true;

        var previous = content[index - 1];
        if (char.IsWhiteSpace(previous))
            return true;

        return previous is '(' or '[' or '{' or '<' or '"' or '\'' or '„' or '”' or '‚' or '’'
            or ',' or '.' or '!' or '?' or ';' or ':';
    }

    private static bool IsRightBoundary(string content, int index)
    {
        if (index >= content.Length)
            return true;

        var next = content[index];
        if (char.IsWhiteSpace(next))
            return true;

        return next is ')' or ']' or '}' or '>' or '"' or '\'' or '”' or '’'
            or ',' or '.' or '!' or '?' or ';' or ':' or '…';
    }

    /// <summary>
    /// Zwraca posortowane, scalone zakresy adresów URL i e-mail. Obejmuje jawne schematy
    /// (w tym niekompletne „s://”), „www.”, adresy e-mail oraz domeny ze ścieżką.
    /// </summary>
    private static List<(int Start, int End)> FindProtectedSpans(string content)
    {
        var spans = new List<(int Start, int End)>();
        foreach (Match match in ProtectedSpanRegex.Matches(content))
        {
            if (match.Length == 0)
                continue;

            spans.Add((match.Index, match.Index + match.Length));
        }

        if (spans.Count <= 1)
            return spans;

        spans.Sort((left, right) => left.Start.CompareTo(right.Start));

        var merged = new List<(int Start, int End)>(spans.Count) { spans[0] };
        for (var i = 1; i < spans.Count; i++)
        {
            var current = spans[i];
            var last = merged[^1];
            if (current.Start <= last.End)
            {
                merged[^1] = (last.Start, Math.Max(last.End, current.End));
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }
}
