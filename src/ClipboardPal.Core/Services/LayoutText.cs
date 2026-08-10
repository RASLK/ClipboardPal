namespace ClipboardPal.Core.Services;

/// <summary>
/// QWERTY ↔ ЙЦУКЕН layout folding so search finds «play» when typing «здфн».
/// </summary>
public static class LayoutText
{
    private const string En = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
    private const string Ru = "йцукенгшщзхъфывапролджэячсмитьбюё";

    private static readonly Dictionary<char, char> EnToRu = BuildMap(En, Ru);
    private static readonly Dictionary<char, char> RuToEn = BuildMap(Ru, En);

    private static Dictionary<char, char> BuildMap(string from, string to)
    {
        if (from.Length != to.Length)
            throw new InvalidOperationException("QWERTY/ЙЦУКЕН layout tables must be the same length.");

        var map = new Dictionary<char, char>(from.Length);
        for (var i = 0; i < from.Length; i++)
            map[from[i]] = to[i];
        return map;
    }

    public static IReadOnlyList<string> SearchVariants(string query)
    {
        // Keep the typed casing for the literal variant so case-sensitive search still
        // matches «Адм» against «Адм». Layout maps are lowercase-only, so fold via
        // ToLowerInvariant (Cyrillic-safe; not culture-dependent like Turkish i).
        var variants = new List<string>(3) { query };
        var q = query.ToLowerInvariant();

        var asRu = Convert(q, EnToRu);
        if (asRu != q)
            variants.Add(asRu);

        var asEn = Convert(q, RuToEn);
        if (asEn != q)
            variants.Add(asEn);

        return variants;
    }

    private static string Convert(string s, Dictionary<char, char> map)
    {
        Span<char> buffer = stackalloc char[s.Length];
        for (var i = 0; i < s.Length; i++)
            buffer[i] = map.TryGetValue(s[i], out var mapped) ? mapped : s[i];
        return new string(buffer);
    }
}
