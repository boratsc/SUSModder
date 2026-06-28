using System;

namespace SUSModder.Core.Lobby
{
    /// <summary>
    /// Konwerter 6-znakowych kodów lobby Among Us ↔ gameId integer.
    /// Port algorytmu z DOC/POC/Lobby-searcher/lookup_lobby.py.
    /// Używa alfabetu V2 (QWXRTYLPESDFGHUJKZOCVBINMA).
    /// </summary>
    public static class LobbyCodeConverter
    {
        private const string V2 = "QWXRTYLPESDFGHUJKZOCVBINMA";

        /// <summary>
        /// Konwertuje 6-znakowy kod lobby (np. "PRIMAL") na gameId integer.
        /// Rzuca FormatException dla nieprawidłowego kodu.
        /// </summary>
        public static int GameNameToInt(string code)
        {
            code = code.Trim().ToUpperInvariant();

            if (code.Length != 6)
                throw new FormatException($"Nieprawidłowa długość kodu lobby: {code.Length} (oczekiwano 6)");

            foreach (char ch in code)
            {
                if (V2.IndexOf(ch) == -1)
                    throw new FormatException($"Nieprawidłowy znak w kodzie lobby: '{ch}'. Dozwolone: {V2}");
            }

            int a = V2.IndexOf(code[0]);
            int b = V2.IndexOf(code[1]);
            int c = V2.IndexOf(code[2]);
            int d = V2.IndexOf(code[3]);
            int e = V2.IndexOf(code[4]);
            int f = V2.IndexOf(code[5]);

            int one = (a + (26 * b)) & 0x3FF;
            int two = c + (26 * (d + (26 * (e + (26 * f)))));
            int value = one | ((two << 10) & 0x3FFFFC00) | unchecked((int)0x80000000);

            // Python: if value >= 2**31: value -= 2**32
            // W C# używamy unchecked i long do bezpiecznego porównania
            long signedValue = value;
            if (signedValue >= 2147483648L) // 2^31
                value = unchecked(value - (int)4294967296); // 2^32

            return value;
        }

        /// <summary>
        /// Konwertuje gameId integer z powrotem na 6-znakowy kod lobby.
        /// </summary>
        public static string IntToGameName(int gameId)
        {
            long value = gameId & 0xFFFFFFFFL;
            int a = (int)(value & 0x3FF);
            int b = (int)((value >> 10) & 0xFFFFF);

            return string.Concat(
                V2[a % 26],
                V2[a / 26],
                V2[b % 26],
                V2[(b / 26) % 26],
                V2[(b / (26 * 26)) % 26],
                V2[(b / (26 * 26 * 26)) % 26]
            );
        }
    }
}
