using System.Collections.Generic;
using System.Text;

/// <summary>
/// Minimal Arabic shaper for TextMeshPro (which has no built-in Arabic shaping).
/// Converts logical Arabic text into joined presentation forms and visual (left-to-right) order,
/// keeping Latin words and numbers readable inside Arabic sentences.
/// </summary>
public static class ArabicShaper
{
    // isolated, final, initial, medial. 0 = form does not exist (right-joining letters).
    private struct Forms
    {
        public char iso, fin, ini, med;
        public bool Dual => ini != 0;
        public Forms(int i, int f, int n = 0, int m = 0) { iso = (char)i; fin = (char)f; ini = (char)n; med = (char)m; }
    }

    private static readonly Dictionary<char, Forms> map = new Dictionary<char, Forms>
    {
        { 'آ', new Forms(0xFE81, 0xFE82) }, { 'أ', new Forms(0xFE83, 0xFE84) },
        { 'ؤ', new Forms(0xFE85, 0xFE86) }, { 'إ', new Forms(0xFE87, 0xFE88) },
        { 'ئ', new Forms(0xFE89, 0xFE8A, 0xFE8B, 0xFE8C) }, { 'ا', new Forms(0xFE8D, 0xFE8E) },
        { 'ب', new Forms(0xFE8F, 0xFE90, 0xFE91, 0xFE92) }, { 'ة', new Forms(0xFE93, 0xFE94) },
        { 'ت', new Forms(0xFE95, 0xFE96, 0xFE97, 0xFE98) }, { 'ث', new Forms(0xFE99, 0xFE9A, 0xFE9B, 0xFE9C) },
        { 'ج', new Forms(0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0) }, { 'ح', new Forms(0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4) },
        { 'خ', new Forms(0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8) }, { 'د', new Forms(0xFEA9, 0xFEAA) },
        { 'ذ', new Forms(0xFEAB, 0xFEAC) }, { 'ر', new Forms(0xFEAD, 0xFEAE) },
        { 'ز', new Forms(0xFEAF, 0xFEB0) }, { 'س', new Forms(0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4) },
        { 'ش', new Forms(0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8) }, { 'ص', new Forms(0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC) },
        { 'ض', new Forms(0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0) }, { 'ط', new Forms(0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4) },
        { 'ظ', new Forms(0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8) }, { 'ع', new Forms(0xFEC9, 0xFECA, 0xFECB, 0xFECC) },
        { 'غ', new Forms(0xFECD, 0xFECE, 0xFECF, 0xFED0) }, { 'ف', new Forms(0xFED1, 0xFED2, 0xFED3, 0xFED4) },
        { 'ق', new Forms(0xFED5, 0xFED6, 0xFED7, 0xFED8) }, { 'ك', new Forms(0xFED9, 0xFEDA, 0xFEDB, 0xFEDC) },
        { 'ل', new Forms(0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0) }, { 'م', new Forms(0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4) },
        { 'ن', new Forms(0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8) }, { 'ه', new Forms(0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC) },
        { 'و', new Forms(0xFEED, 0xFEEE) }, { 'ى', new Forms(0xFEEF, 0xFEF0) },
        { 'ي', new Forms(0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4) },
    };

    // Lam + alef ligatures: isolated, final
    private static readonly Dictionary<char, (char iso, char fin)> lamAlef = new Dictionary<char, (char, char)>
    {
        { 'آ', ('ﻵ', 'ﻶ') }, { 'أ', ('ﻷ', 'ﻸ') },
        { 'إ', ('ﻹ', 'ﻺ') }, { 'ا', ('ﻻ', 'ﻼ') },
    };

    private const char Tatweel = 'ـ';

    public static bool HasArabic(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s) if (c >= '؀' && c <= 'ۿ') return true;
        return false;
    }

    private static bool IsHaraka(char c) => (c >= 'ً' && c <= 'ٟ') || c == 'ٰ';
    private static bool CanJoinNext(char c) => c == Tatweel || (map.TryGetValue(c, out var f) && f.Dual);
    private static bool IsJoining(char c) => c == Tatweel || map.ContainsKey(c);

    /// <summary>Shape and reorder a (possibly multi-line) string for display.</summary>
    public static string Shape(string input)
    {
        if (!HasArabic(input)) return input;
        string[] lines = input.Split('\n');
        for (int i = 0; i < lines.Length; i++) lines[i] = Reorder(Join(lines[i]));
        return string.Join("\n", lines);
    }

    private static string Join(string text)
    {
        // Drop diacritics: TMP can't position them reliably.
        var chars = new List<char>(text.Length);
        foreach (char c in text) if (!IsHaraka(c)) chars.Add(c);

        var sb = new StringBuilder(chars.Count);
        for (int i = 0; i < chars.Count; i++)
        {
            char c = chars[i];
            char prev = i > 0 ? chars[i - 1] : '\0';
            char next = i < chars.Count - 1 ? chars[i + 1] : '\0';
            bool joinsPrev = CanJoinNext(prev) && IsJoining(c);

            if (c == 'ل' && lamAlef.TryGetValue(next, out var lig))
            {
                sb.Append(joinsPrev ? lig.fin : lig.iso);
                i++; // consume alef
                continue;
            }
            if (!map.TryGetValue(c, out var f)) { sb.Append(c); continue; }

            bool joinsNext = f.Dual && IsJoining(next);
            if (joinsPrev && joinsNext) sb.Append(f.med);
            else if (joinsPrev) sb.Append(f.fin);
            else if (joinsNext) sb.Append(f.ini);
            else sb.Append(c); // base code point renders the isolated form in every font
        }
        return sb.ToString();
    }

    private static bool IsLtrStrong(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    private static bool IsLtrJoiner(char c) => c == '.' || c == ',' || c == ':' || c == '/' || c == '-' || c == '+' || c == ' ' || c == '%';

    private static char Mirror(char c)
    {
        switch (c)
        {
            case '(': return ')'; case ')': return '(';
            case '[': return ']'; case ']': return '[';
            case '{': return '}'; case '}': return '{';
            case '<': return '>'; case '>': return '<';
            default: return c;
        }
    }

    /// <summary>Logical RTL paragraph -> visual LTR string, keeping Latin/number runs intact.</summary>
    private static string Reorder(string s)
    {
        var tokens = new List<(bool ltr, string text)>();
        int i = 0;
        while (i < s.Length)
        {
            if (IsLtrStrong(s[i]))
            {
                int j = i + 1, lastStrong = i;
                while (j < s.Length && (IsLtrStrong(s[j]) || IsLtrJoiner(s[j])))
                {
                    if (IsLtrStrong(s[j])) lastStrong = j;
                    j++;
                }
                int end = lastStrong + 1;
                if (end < s.Length && s[end] == '%') end++; // keep "75%" together
                tokens.Add((true, s.Substring(i, end - i)));
                i = end;
            }
            else
            {
                int j = i + 1;
                while (j < s.Length && !IsLtrStrong(s[j])) j++;
                tokens.Add((false, s.Substring(i, j - i)));
                i = j;
            }
        }

        var sb = new StringBuilder(s.Length);
        for (int t = tokens.Count - 1; t >= 0; t--)
        {
            if (tokens[t].ltr) { sb.Append(tokens[t].text); continue; }
            string txt = tokens[t].text;
            for (int k = txt.Length - 1; k >= 0; k--) sb.Append(Mirror(txt[k]));
        }
        return sb.ToString();
    }
}
