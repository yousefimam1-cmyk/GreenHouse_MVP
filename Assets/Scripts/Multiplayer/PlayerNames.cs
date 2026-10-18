using UnityEngine;

/// <summary>
/// Player names for online play are picked from a list of friendly farm names plus a number ("Golden Palm 42"), never
/// typed: strangers see them, so nothing rude or personal can be shown. The English name travels over the network and
/// each player sees it in their own language. Online hosts refuse names that aren't from this list.
/// </summary>
public static class PlayerNames
{
    public const int MaxNumber = 99;
    private const string IndexKey = "mazraa.mp.nameIndex", NumberKey = "mazraa.mp.nameNumber";

    /// <summary>English / Arabic.</summary>
    public static readonly string[][] All =
    {
        new[] { "Happy Farmer", "المزارع السعيد" },
        new[] { "Golden Palm", "النخلة الذهبية" },
        new[] { "Desert Falcon", "صقر الصحراء" },
        new[] { "Date Picker", "جامع التمر" },
        new[] { "Green Tractor", "الجرار الأخضر" },
        new[] { "Busy Bee", "النحلة النشيطة" },
        new[] { "Morning Star", "نجمة الصباح" },
        new[] { "Calm Camel", "الجمل الهادئ" },
        new[] { "Clever Goat", "الماعز الذكي" },
        new[] { "Brave Rooster", "الديك الشجاع" },
        new[] { "Sunny Field", "الحقل المشمس" },
        new[] { "Olive Tree", "شجرة الزيتون" },
        new[] { "Silver Moon", "القمر الفضي" },
        new[] { "Little Lamb", "الخروف الصغير" },
        new[] { "Swift Gazelle", "الغزال السريع" },
        new[] { "Blue Oasis", "الواحة الزرقاء" },
        new[] { "Wheat Harvest", "حصاد القمح" },
        new[] { "Red Tomato", "الطماطم الحمراء" },
        new[] { "Sweet Melon", "البطيخ الحلو" },
        new[] { "Lucky Seed", "البذرة المحظوظة" },
        new[] { "Kind Shepherd", "الراعي الطيب" },
        new[] { "Strong Ox", "الثور القوي" },
        new[] { "Wise Owl", "البومة الحكيمة" },
        new[] { "Bright Lantern", "الفانوس المضيء" },
        new[] { "Fresh Mint", "النعناع الطازج" },
        new[] { "Cool Breeze", "النسمة العليلة" },
        new[] { "Rain Cloud", "غيمة المطر" },
        new[] { "Fig Garden", "بستان التين" },
        new[] { "Honey Jar", "جرة العسل" },
        new[] { "Pomegranate", "الرمانة" },
    };

    public static int Count => All.Length;

    public static int Index
    {
        get { int i = PlayerPrefs.GetInt(IndexKey, -1); if (i < 0) { i = Random.Range(0, Count); PlayerPrefs.SetInt(IndexKey, i); } return Mathf.Clamp(i, 0, Count - 1); }
        set { PlayerPrefs.SetInt(IndexKey, ((value % Count) + Count) % Count); PlayerPrefs.Save(); }
    }

    public static int Number
    {
        get { int n = PlayerPrefs.GetInt(NumberKey, 0); if (n < 1) { n = Random.Range(1, MaxNumber + 1); PlayerPrefs.SetInt(NumberKey, n); } return Mathf.Clamp(n, 1, MaxNumber); }
        set { PlayerPrefs.SetInt(NumberKey, ((value - 1) % MaxNumber + MaxNumber) % MaxNumber + 1); PlayerPrefs.Save(); }
    }

    /// <summary>The name picked in the settings, as sent over the network ("Golden Palm 42").</summary>
    public static string Current => Make(Index, Number);

    public static string Make(int index, int number) => All[((index % Count) + Count) % Count][0] + " " + Mathf.Clamp(number, 1, MaxNumber);

    /// <summary>A new random name (the "Random" button).</summary>
    public static string Randomize()
    {
        Index = Random.Range(0, Count);
        Number = Random.Range(1, MaxNumber + 1);
        return Current;
    }

    /// <summary>Is this a name from the list (base name + number 1-99)?</summary>
    public static bool IsGenerated(string name) => Find(name, out _, out _);

    private static bool Find(string name, out int index, out int number)
    {
        index = -1; number = 0;
        if (string.IsNullOrEmpty(name)) return false;
        int sp = name.LastIndexOf(' ');
        if (sp <= 0 || !int.TryParse(name.Substring(sp + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number)) return false;
        if (number < 1 || number > MaxNumber || name.Substring(sp + 1) != number.ToString(System.Globalization.CultureInfo.InvariantCulture)) return false;
        string head = name.Substring(0, sp);
        for (int i = 0; i < All.Length; i++) if (All[i][0] == head) { index = i; return true; }
        return false;
    }

    /// <summary>A generated name in the UI language (others unchanged).</summary>
    public static string Display(string name)
    {
        if (!Loc.IsArabic || !Find(name, out int i, out int n)) return name ?? "";
        return All[i][1] + " " + n;
    }
}
