using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public enum Language { English, Arabic }

/// <summary>
/// English/Arabic localization. English strings are the keys, so scene texts translate automatically.
/// Arabic text is shaped (letters joined, right-to-left) and rendered with the Tajawal font.
/// </summary>
public static class Loc
{
    private const string PrefKey = "mazraa.language";
    private const string ArabicFontPath = "Fonts/Tajawal-Bold";

    public static event Action OnLanguageChanged;
    public static Language Current { get; private set; } = Language.English;
    public static bool IsArabic => Current == Language.Arabic;

    private static readonly Dictionary<string, string> arabic = new Dictionary<string, string>
    {
        // Scene UI
        { "Climate Control", "التحكم في المناخ" },
        { "Crop Stats", "بيانات المحصول" },
        { "Energy", "الطاقة" },
        { "Health", "الصحة" },
        { "Nutrient", "التسميد" },
        { "Nutrients", "التسميد" },
        { "Water", "المياه" },
        { "Climate", "المناخ" },
        { "Optimize", "تحسين" },
        { "Satisfied", "مستوفى" },
        { "Not Satisfied", "غير مستوفى" },
        { "Statistics", "الإحصائيات" },
        { "Value", "القيمة" },
        { "Yield Projection", "الإنتاج المتوقع" },
        { "Start well to optimize the modules!", "شغّل البئر لتحسين الوحدات!" },
        { "Start the well first to optimize modules!", "شغّل البئر أولاً لتحسين الوحدات!" },

        // Prompts
        { "[E] Control Station", "[E] محطة التحكم" },
        { "[E] Inspect", "[E] فحص" },
        { "[E] Start Well", "[E] تشغيل البئر" },
        { "[E] Use", "[E] استخدام" },
        { "[E] Harvest", "[E] حصاد" },

        // HUD / gameplay
        { "Mazraa Greenhouse", "صوبة المزرعة" },
        { "Greenhouse", "الصوبة" },
        { "Training", "التدريب" },
        { "[T] Start   [N] Next scenario", "[T] ابدأ   [N] السيناريو التالي" },
        { "[T] Stop training", "[T] إيقاف التدريب" },
        { "Training stopped", "تم إيقاف التدريب" },
        { "Time's up - training failed", "انتهى الوقت - لم ينجح التدريب" },
        { "Harvested {0}/{1} kg", "الحصاد {0}/{1} كجم" },
        { "Healthy beds {0}/{1}", "الأحواض السليمة {0}/{1}" },
        { "Time left {0}s", "الوقت المتبقي {0} ث" },
        { "Best: {0}s", "الأفضل: {0} ث" },
        { "Training complete: {0}", "اكتمل التدريب: {0}" },
        { "Optimizations: {0}", "عدد التحسينات: {0}" },
        { "Gold", "ذهبي" },
        { "Silver", "فضي" },
        { "Bronze", "برونزي" },
        { "Partners", "الشركاء" },
        { "All missions complete - free play", "كل المهمات اكتملت - لعب حر" },
        { "Bigger truck bed", "صندوق عربية أكبر" },
        { "Bought", "تم الشراء" },
        { "Bump!", "خبطة!" },
        { "Cargo {0}/{1} kg", "الحمولة {0}/{1} كجم" },
        { "Collected", "تم الجمع" },
        { "EGP", "جنيه" },
        { "Farm Shop", "محل المزرعة" },
        { "Farm storage is empty - harvest first", "مخزن المزرعة فاضي - احصد الأول" },
        { "Faster growth", "نمو أسرع" },
        { "Fully upgraded", "تمت الترقية بالكامل" },
        { "Graphics: High", "الجرافيكس: عالي" },
        { "Graphics: Low", "الجرافيكس: منخفض" },
        { "Loaded {0} kg onto the truck", "تم تحميل {0} كجم على العربية" },
        { "Loading bay", "منطقة التحميل" },
        { "Market", "السوق" },
        { "Mission", "مهمة" },
        { "Mission complete", "المهمة اكتملت" },
        { "Money: {0} EGP", "الفلوس: {0} جنيه" },
        { "Money: {0} EGP   Feed: {1}", "الفلوس: {0} جنيه   العلف: {1}" },
        { "New game started", "بدأت لعبة جديدة" },
        { "No feed - buy feed at the shop", "مفيش علف - اشتري علف من المحل" },
        { "Not enough money", "الفلوس مش كفاية" },
        { "Nothing to collect yet", "مفيش حاجة تتجمع لسه" },
        { "Paused", "إيقاف مؤقت" },
        { "Reward", "المكافأة" },
        { "Sold the load for {0} EGP", "اتباعت الحمولة بـ {0} جنيه" },
        { "Storage: {0} kg   Feed: {1}", "المخزن: {0} كجم   العلف: {1}" },
        { "The pen is full for this animal", "الحظيرة مليانة من الحيوان ده" },
        { "Truck is full - drive to the market", "العربية مليانة - روح السوق" },
        { "[1-9] or click to buy   [Esc] close", "[1-9] أو اضغط للشراء   [Esc] إغلاق" },
        { "[Backspace] New game (clears saved progress)", "[Backspace] لعبة جديدة (يمسح التقدم المحفوظ)" },
        { "[E] Collect animal products", "[E] اجمع منتجات الحيوانات" },
        { "[E] Drive truck", "[E] سوق العربية" },
        { "[E] Exit   [Space] Brake   [R] Reset truck", "[E] انزل   [Space] فرامل   [R] عدّل العربية" },
        { "[E] Open shop", "[E] افتح المحل" },
        { "[G] Graphics   [P] Pause", "[G] الجرافيكس   [P] إيقاف" },
        { "[P] Resume", "[P] استكمال" },
        { "{0} km/h", "{0} كم/س" },
        { "Animal products", "منتجات الحيوانات" },
        { "Harvested: {0} kg", "المحصول: {0} كجم" },
        { "{0}: {1}/{2} beds healthy", "{0}: {1}/{2} أحواض سليمة" },
        { "[H] Help   [L] Arabic   [T] Training", "[H] مساعدة   [L] English   [T] تدريب" },
        { "Harvested {0} kg of {1}", "تم حصاد {0} كجم من {1}" },
        { "All crop beds are healthy!", "كل الأحواض سليمة!" },
        { "Growing {0}%", "النمو {0}%" },
        { "Ready to harvest", "جاهز للحصاد" },
        { "Dead - optimize modules", "ميت - حسّن الوحدات" },
        { "Ideal growing conditions", "الظروف المثالية للنمو" },
        { "Day temp", "حرارة النهار" },
        { "Night temp", "حرارة الليل" },
        { "Humidity", "الرطوبة" },
        { "EC", "EC" },
        { "pH", "pH" },
        { "Water / plant", "مياه / نبات" },
        { "Crop cycle", "دورة المحصول" },
        { "Reference ranges - confirm with your agronomist.", "قيم مرجعية - راجعها مع المهندس الزراعي." },
        { "HELP", "مساعدة" },
    };

    // Long English texts that are keyed by an id instead of their own text
    private static readonly Dictionary<string, string> englishLong = new Dictionary<string, string>
    {
        { "HELP_BODY",
            "WASD - move    Mouse - look    E - interact\n" +
            "Start the well, then use the Control Station to optimize\n" +
            "Irrigation, Nutrients, Energy and Climate.\n" +
            "Healthy (green) beds grow - press E on a grown bed to harvest.\n" +
            "Esc - close panels    H - help    L - Arabic/English\n" +
            "T - start/stop a timed training scenario    N - next scenario\n" +
            "Truck: E get in/out, WASD drive, Space brake, R reset. Green pad loads, gold pad sells.\n" +
            "Shops sell feed, animals and upgrades. Collect eggs, milk and wool at the pen crate.\n" +
            "G - graphics quality    P - pause / new game" },
    };

    private static readonly Dictionary<string, string> arabicLong = new Dictionary<string, string>
    {
        { "HELP_BODY",
            "WASD - حركة    الماوس - نظر    E - تفاعل\n" +
            "شغّل البئر ثم استخدم محطة التحكم لتحسين\n" +
            "الري والتسميد والطاقة والمناخ.\n" +
            "الأحواض السليمة (الخضراء) تنمو - اضغط E على الحوض الناضج للحصاد.\n" +
            "Esc - إغلاق    H - مساعدة    L - عربي/إنجليزي\n" +
            "T - بدء/إيقاف سيناريو تدريب    N - السيناريو التالي\n" +
            "العربية: E ركوب/نزول، WASD قيادة، Space فرامل، R تعديل. الأخضر تحميل، الذهبي بيع.\n" +
            "المحلات فيها علف وحيوانات وترقيات. اجمع البيض واللبن والصوف من صندوق الحظيرة.\n" +
            "G - جودة الجرافيكس    P - إيقاف / لعبة جديدة" },
    };

    private static TMP_FontAsset arabicFont;
    private static bool fontLoadFailed;
    private static readonly Dictionary<TMP_Text, TMP_FontAsset> originalFonts = new Dictionary<TMP_Text, TMP_FontAsset>();
    private static readonly Dictionary<TMP_Text, string> sceneOriginals = new Dictionary<TMP_Text, string>();
    private static readonly HashSet<TMP_Text> excluded = new HashSet<TMP_Text>();

    /// <summary>Texts whose owner sets them in code (alerts, values) are skipped by LocalizeScene.</summary>
    public static void ExcludeFromScene(TMP_Text text) { if (text != null) excluded.Add(text); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnLanguageChanged = null;
        originalFonts.Clear();
        sceneOriginals.Clear();
        excluded.Clear();
        Current = Language.English;
    }

    /// <summary>Restore the language the player chose last time.</summary>
    public static void LoadSaved() => SetLanguage((Language)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, 1));

    public static void SetLanguage(Language lang)
    {
        Current = lang;
        PlayerPrefs.SetInt(PrefKey, (int)lang);
        PlayerPrefs.Save();
        LocalizeScene();
        OnLanguageChanged?.Invoke();
    }

    public static void Toggle() => SetLanguage(IsArabic ? Language.English : Language.Arabic);

    /// <summary>Translate an English key (unshaped). Unknown keys are returned as-is.</summary>
    public static string Raw(string english)
    {
        if (english == null) return "";
        if (!IsArabic) return englishLong.TryGetValue(english, out var e) ? e : english;
        if (arabicLong.TryGetValue(english, out var l)) return l;
        return arabic.TryGetValue(english, out var a) ? a : english;
    }

    /// <summary>Translate (and shape for display).</summary>
    public static string T(string english) => Display(Raw(english));

    public static string T(string english, params object[] args) => Display(string.Format(Raw(english), args));

    /// <summary>Shape already-translated text for display when Arabic is active.</summary>
    public static string Display(string text) => IsArabic ? ArabicShaper.Shape(text) : text;

    public static bool HasTranslation(string english) => english != null && arabic.ContainsKey(english.Trim());

    public static TMP_FontAsset ArabicFont
    {
        get
        {
            if (arabicFont == null && !fontLoadFailed)
            {
                Font f = Resources.Load<Font>(ArabicFontPath);
                if (f != null) arabicFont = TMP_FontAsset.CreateFontAsset(f);
                if (arabicFont == null)
                {
                    fontLoadFailed = true;
                    Debug.LogWarning("[Loc] Arabic font could not be loaded from Resources/" + ArabicFontPath);
                }
                else arabicFont.name = "Tajawal-Bold (runtime)";
            }
            return arabicFont;
        }
    }

    /// <summary>Switch a text component to the Arabic font when needed (and back).</summary>
    public static void ApplyFont(TMP_Text text)
    {
        if (text == null) return;
        if (!originalFonts.ContainsKey(text)) originalFonts[text] = text.font;
        TMP_FontAsset target = IsArabic && ArabicFont != null ? ArabicFont : originalFonts[text];
        if (target != null && text.font != target) text.font = target;
    }

    /// <summary>Set a text component from an English key, translated + correct font.</summary>
    public static void Set(TMP_Text text, string english, params object[] args)
    {
        if (text == null) return;
        ApplyFont(text);
        text.text = args != null && args.Length > 0 ? T(english, args) : T(english);
    }

    public static void SetDisplay(TMP_Text text, string alreadyTranslated)
    {
        if (text == null) return;
        ApplyFont(text);
        text.text = Display(alreadyTranslated);
    }

    /// <summary>Translate every static text in the loaded scenes whose English text has a translation.</summary>
    public static void LocalizeScene()
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (excluded.Contains(t)) continue;
            if (!sceneOriginals.ContainsKey(t))
            {
                if (!HasTranslation(t.text)) continue;
                sceneOriginals[t] = t.text.Trim();
            }
            Set(t, sceneOriginals[t]);
        }
    }

    /// <summary>Crop name in the current language (unshaped).</summary>
    public static string CropName(CropProfile p) => p == null ? "" : (IsArabic && !string.IsNullOrEmpty(p.nameAr) ? p.nameAr : p.nameEn);
}
