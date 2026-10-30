using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SUSModder.Core.Models;

namespace SUSModder.Core.Validators
{
    /// <summary>
    /// Walidacja treści wiadomości globalnego komunikatora.
    /// Jedno źródło prawdy dla Core i UI: limity długości (10–280 jednostek UTF-16 po trim),
    /// zakaz znaków sterujących oraz reguła: dozwolony jest wyłącznie jeden link zaproszenia discord.gg.
    ///
    /// Wykrywanie linków jest oparte o skanowanie kandydatów w tekście (a nie o dzielenie na słowa),
    /// dzięki czemu linki przylegające do interpunkcji (np. "discord.gg/abc,discord.gg/def")
    /// są wykrywane pojedynczo — inaczej limit jednego zaproszenia można by obejść.
    ///
    /// Walidacja kliencka jest pierwszą linią obrony — backend weryfikuje niezależnie.
    /// </summary>
    public static class CommunityChatMessageValidator
    {
        /// <summary>Minimalna długość treści (jednostki UTF-16 po trim).</summary>
        public const int MinLength = CommunityChatLimits.MinMessageLength;

        /// <summary>Maksymalna długość treści (jednostki UTF-16 po trim).</summary>
        public const int MaxLength = CommunityChatLimits.MaxMessageLength;

        private const string DiscordInviteHost = "discord.gg";

        private static readonly Regex InviteCodeRegex =
            new(@"^[A-Za-z0-9_-]{2,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Kandydaci na link. Każda alternatywa:
        /// - schemat z "://" (http, https, ftp, ...),
        /// - host zaczynający się od "www.",
        /// - niebezpieczne schematy bez "://" (mailto, data, javascript, vbscript),
        /// - host z domeną (etykiety mogą zawierać cyfry i myślniki, TLD to 2–24 litery).
        /// Ścieżka kończy się na przecinku lub średniku, dzięki czemu interpunkcja rozdziela linki.
        /// </summary>
        private static readonly Regex LinkCandidateRegex = new(
            @"(?<![A-Za-z0-9@\-])(?:[A-Za-z][A-Za-z0-9+.\-]*://|www\.)[^\s,;]+" +
            @"|(?<![A-Za-z0-9@\-])(?:mailto|data|javascript|vbscript):[^\s,;]+" +
            @"|(?<![A-Za-z0-9@\-])(?:[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?\.)+(?:[A-Za-z]{2,24})(?::\d{1,5})?(?:/[^\s,;]+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Rozszerzenia plików, które wyglądają jak domena (np. "readme.md"), ale nie są linkiem.
        /// </summary>
        private static readonly HashSet<string> FileLikeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "txt", "json", "yaml", "yml", "xml", "csv", "tsv", "log", "md", "markdown", "rtf",
            "cs", "csproj", "sln", "slnx", "vb", "fs", "fsx", "razor", "cshtml",
            "ini", "cfg", "conf", "config", "toml", "env", "props", "targets", "editorconfig", "gitignore",
            "png", "jpg", "jpeg", "gif", "webp", "bmp", "svg", "ico", "psd", "ai", "eps", "fig", "sketch", "blend", "obj", "unity",
            "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "dll", "exe", "msi", "apk", "iso", "dmg", "deb", "rpm",
            "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods",
            "mp3", "wav", "flac", "ogg", "m4a", "mp4", "mkv", "avi", "mov", "webm", "wmv", "flv",
            "py", "rb", "go", "rs", "java", "kt", "kts", "swift", "php", "c", "cpp", "cc", "h", "hpp", "m", "mm",
            "sh", "bash", "zsh", "ps1", "bat", "cmd", "sql", "db", "sqlite", "bak", "tmp", "pem", "key", "crt", "cer", "pfx",
            "html", "htm", "css", "scss", "sass", "less", "js", "jsx", "ts", "tsx", "vue", "svelte", "map"
        };

        /// <summary>
        /// Popularne domeny najwyższego poziomu — token z takim TLD jest traktowany jak link,
        /// nawet bez ścieżki (np. "discord.gg", "example.com").
        /// </summary>
        private static readonly HashSet<string> LinkLikeTopLevelDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "gg", "com", "net", "org", "io", "co", "app", "dev", "xyz", "link", "site", "online", "me", "tv", "cc",
            "us", "uk", "de", "fr", "es", "it", "nl", "se", "no", "fi", "dk", "cz", "sk", "ua", "be", "at", "ch",
            "pt", "gr", "hu", "ro", "bg", "hr", "lt", "lv", "ee", "ca", "au", "nz", "jp", "cn", "in", "br", "mx",
            "ar", "cl", "za", "kr", "tr", "il", "ae", "sa", "sg", "my", "id", "th", "ph", "vn", "tw", "hk", "ru",
            "info", "biz", "name", "pro", "top", "vip", "club", "shop", "store", "fun", "live", "life", "world",
            "today", "space", "website", "press", "wiki", "chat"
        };

        /// <summary>
        /// Normalizuje treść do postu — usuwa spacje brzegowe (trim).
        /// Wysyłany jest znormalizowany tekst, długość liczona jest na nim samym.
        /// </summary>
        public static string? Normalize(string? content) => content?.Trim();

        /// <summary>
        /// Długość znormalizowanej treści w jednostkach UTF-16 (zgodnie z licznikiem w UI).
        /// </summary>
        public static int MeasureLength(string? content) => Normalize(content)?.Length ?? 0;

        /// <summary>
        /// Waliduje treść wiadomości. Zwraca (true, null) gdy poprawna, (false, errorCode) gdy nie.
        ///
        /// Kolejność jest istotna: znaki sterujące i znaki nowej linii są sprawdzane w **oryginalnej**
        /// treści, przed trimem i pomiarem długości. Dzięki temu wklejone na początku lub na końcu
        /// znaki LF/CRLF/tab nie są cicho obcinane — są jawnie odrzucane.
        /// Spacje brzegowe są nadal obcinane i nie są liczone w długości (jednostki UTF-16).
        /// </summary>
        public static (bool IsValid, string? ErrorCode) Validate(string? content)
        {
            if (content is null)
                return (false, CommunityChatErrorCodes.ContentTooShort);

            // 1) Kontrola znaków sterujących w oryginalnej treści (przed trimem).
            foreach (var c in content)
            {
                if (c is '\n' or '\r')
                    return (false, CommunityChatErrorCodes.ContentContainsNewline);
                if (char.IsControl(c))
                    return (false, CommunityChatErrorCodes.ContentContainsControlCharacter);
            }

            // 2) Normalizacja i pomiar długości (jednostki UTF-16 po trim).
            var normalized = Normalize(content);
            if (string.IsNullOrEmpty(normalized))
                return (false, CommunityChatErrorCodes.ContentTooShort);

            var length = normalized.Length;
            if (length > MaxLength)
                return (false, CommunityChatErrorCodes.ContentTooLong);
            if (length < MinLength)
                return (false, CommunityChatErrorCodes.ContentTooShort);

            // 3) Reguła linków.
            var links = FindLinks(normalized);
            if (links.Count > 0)
            {
                foreach (var link in links)
                {
                    if (!IsDiscordInviteLink(link))
                        return (false, CommunityChatErrorCodes.DisallowedUrl);
                }

                if (links.Count > CommunityChatLimits.MaxDiscordLinks)
                    return (false, CommunityChatErrorCodes.TooManyLinks);
            }

            return (true, null);
        }

        /// <summary>
        /// Zwraca listę kandydatów na linki w treści.
        /// Wykrywane są: dowolny schemat z "://", linki "www.", adresy "mailto:"/"data:"/"javascript:",
        /// oraz hosty z domeną (z obsługą cyfr, myślników, wielkości liter i ścieżek).
        /// Interpunkcja rozdziela linki, więc "link1,link2" to dwa osobne kandydaty.
        /// </summary>
        public static IReadOnlyList<string> FindLinks(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return Array.Empty<string>();

            var links = new List<string>();
            foreach (Match match in LinkCandidateRegex.Matches(content))
            {
                var candidate = TrimTrailingPunctuation(match.Value);
                if (candidate.Length == 0)
                    continue;

                if (LooksLikeLink(candidate))
                    links.Add(candidate);
            }

            return links;
        }

        /// <summary>
        /// Sprawdza, czy token jest prawdziwym linkiem zaproszenia discord.gg
        /// (dokładnie host discord.gg + jeden segment kodu zaproszenia).
        /// Chroni przed podszytymi domenami (discord.gg.evil.com, xdiscord.gg, discordapp.com).
        /// </summary>
        public static bool IsDiscordInviteLink(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var value = token.Trim();

            var schemeSeparator = value.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparator > 0)
            {
                var scheme = value[..schemeSeparator];
                if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                    !scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                value = value[(schemeSeparator + 3)..];
            }

            var slash = value.IndexOf('/');
            if (slash <= 0)
                return false;

            var host = value[..slash];
            if (!host.Equals(DiscordInviteHost, StringComparison.OrdinalIgnoreCase))
                return false;

            var rest = value[(slash + 1)..];

            // Zaproszenie nie może nieść dodatkowej ścieżki, query ani fragmentu.
            var cut = rest.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0)
                rest = rest[..cut];

            rest = rest.TrimEnd('/');
            if (rest.Length == 0 || rest.Contains('/'))
                return false;

            return InviteCodeRegex.IsMatch(rest);
        }

        private static string TrimTrailingPunctuation(string token)
        {
            var end = token.Length;
            while (end > 0 && IsWrappingPunctuation(token[end - 1]))
                end--;

            return end == token.Length ? token : token[..end];
        }

        private static bool IsWrappingPunctuation(char c) =>
            c is '(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' or '"' or '\'' or '`' or
                ',' or ';' or ':' or '!' or '?' or '.' or '*' or '~' or '#' or '@' or '$' or '%' or '^' or '&';

        /// <summary>
        /// Kwalifikuje kandydata jako link. Host z domeną jest linkiem, chyba że przypomina
        /// nazwę pliku (np. "readme.md") albo jest zwykłym tekstem ("5.The").
        /// </summary>
        private static bool LooksLikeLink(string candidate)
        {
            if (candidate.Contains("://", StringComparison.Ordinal))
                return true;

            if (candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                return true;

            if (candidate.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var hostEnd = candidate.IndexOfAny(new[] { '/', '?', '#' });
            var hasPath = hostEnd >= 0;
            var host = hasPath ? candidate[..hostEnd] : candidate;

            var colon = host.IndexOf(':');
            if (colon > 0)
                host = host[..colon];

            if (!host.Contains('.'))
                return false;

            var labels = host.Split('.');
            if (labels.Length < 2)
                return false;

            // Etykiety mogą zawierać cyfry i myślniki, ale muszą zaczynać i kończyć się znakiem alfanumerycznym.
            foreach (var label in labels)
            {
                if (label.Length == 0)
                    return false;
                if (!char.IsLetterOrDigit(label[0]) || !char.IsLetterOrDigit(label[^1]))
                    return false;
                if (!label.All(c => char.IsLetterOrDigit(c) || c == '-'))
                    return false;
            }

            var tld = labels[^1];
            if (tld.Length < 2 || tld.Length > 24)
                return false;
            if (!tld.All(char.IsLetter))
                return false;

            var lowerTld = tld.ToLowerInvariant();

            if (LinkLikeTopLevelDomains.Contains(lowerTld))
                return true;

            if (FileLikeExtensions.Contains(lowerTld))
                return hasPath;

            if (hasPath)
                return true;

            // "5.The" / "word.Word" — wielka litera w TLD sugeruje zwykły tekst, nie domenę.
            return !tld.Any(char.IsUpper);
        }
    }
}
