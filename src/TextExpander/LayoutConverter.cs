namespace TextExpander;

/// <summary>Converts text typed in the wrong layout by physical key: US QWERTY ↔ Russian ЙЦУКЕН. No WinAPI.</summary>
public static class LayoutConverter
{
    // Same physical keys, position by position: unshifted row, Shift row, then Shift+digit keys.
    private const string LatinKeys =
        "`qwertyuiop[]asdfghjkl;'zxcvbnm,./" +
        "~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?" +
        "@#$^&|";
    private const string CyrillicKeys =
        "ёйцукенгшщзхъфывапролджэячсмитьбю." +
        "ЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ," +
        "\"№;:?/";

    private static readonly Dictionary<char, char> ToCyrillic = new();
    private static readonly Dictionary<char, char> ToLatin = new();

    static LayoutConverter()
    {
        for (var i = 0; i < LatinKeys.Length; i++)
        {
            ToCyrillic[LatinKeys[i]] = CyrillicKeys[i];
            // To Latin only Cyrillic letters are converted; punctuation is left as typed.
            if (IsCyrillicLetter(CyrillicKeys[i]))
                ToLatin[CyrillicKeys[i]] = LatinKeys[i];
        }
    }

    /// <summary>
    /// Direction: more Cyrillic than Latin letters → to Latin, otherwise → to Cyrillic.
    /// Text without letters is returned unchanged. Unmapped characters are kept.
    /// </summary>
    public static string Convert(string text, out bool toCyrillic)
    {
        int latin = 0, cyrillic = 0;
        foreach (var c in text)
        {
            if (IsLatinLetter(c)) latin++;
            else if (IsCyrillicLetter(c)) cyrillic++;
        }
        toCyrillic = cyrillic <= latin;
        if (latin == 0 && cyrillic == 0)
            return text;

        var map = toCyrillic ? ToCyrillic : ToLatin;
        var result = text.ToCharArray();
        for (var i = 0; i < result.Length; i++)
        {
            if (toCyrillic && IsTrailingPunctuation(text, i))
                continue;
            if (map.TryGetValue(text[i], out var converted))
                result[i] = converted;
        }
        return new string(result);
    }

    /// <summary>'.' or ',' right after a letter and before whitespace or the end ends a word or sentence: keep it.</summary>
    private static bool IsTrailingPunctuation(string text, int i) =>
        text[i] is '.' or ','
        && i > 0 && IsLatinLetter(text[i - 1])
        && (i == text.Length - 1 || char.IsWhiteSpace(text[i + 1]));

    private static bool IsLatinLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsCyrillicLetter(char c) => c is >= 'А' and <= 'я' or 'ё' or 'Ё';
}
