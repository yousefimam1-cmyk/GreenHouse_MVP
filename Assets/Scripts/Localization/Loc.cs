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
        { "Mazraa Greenhouse", "البيت المحمي" },
        { "Greenhouse", "البيت المحمي" },
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
        { "Mazraa", "المزرعة" },
        { "[Enter] Start   [L] Arabic / English", "[Enter] ابدأ   [L] عربي / إنجليزي" },
        { "A new animal was born", "انولد حيوان جديد" },
        { "Water trough filled", "تعبّى حوض الماء" },
        { "Pen cleaned", "تنظّفت الحظيرة" },
        { "Healthy", "صحته زينة" },
        { "Weak", "ضعيف" },
        { "Sick", "مريض" },
        { "Animals", "الحيوانات" },
        { "Clean", "النظافة" },
        { "No feed", "ما فيه علف" },
        { "[E] Fill water trough", "[E] عبّي حوض الماء" },
        { "[E] Clean the pen", "[E] نظّف الحظيرة" },
        { "[E] Drive tractor", "[E] سُق الجرار" },
        { "Implement", "المعدّة" },
        { "No implement", "بدون معدّة" },
        { "Plow", "محراث" },
        { "Seeder", "بذّارة" },
        { "Harvester", "حصّادة" },
        { "[Q] Change implement   [E] Exit   [R] Reset", "[Q] غيّر المعدّة   [E] انزل   [R] تعديل" },
        { "Fuel {0}%", "الوقود {0}%" },
        { "Out of fuel - press R to tow (150 SAR)", "خلص الوقود - اضغط R للسحب (150 ريال)" },
        { "Refuelled {0} L for {1} SAR", "تم التموين {0} لتر بـ {1} ريال" },
        { "Towed to the loading bay for {0} SAR", "انسحبت لمنطقة التحميل بـ {0} ريال" },
        { "Contract complete", "العقد اكتمل" },
        { "Contract expired", "العقد انتهى" },
        { "New contract", "عقد جديد" },
        { "Market prices today", "أسعار السوق اليوم" },
        { "Contracts", "العقود" },
        { "due day", "آخر يوم" },
        { "[M] close", "[M] إغلاق" },
        { "[G] Graphics   [P] Pause   [M] Prices", "[G] الجرافيكس   [P] إيقاف   [M] الأسعار" },
        { "Day", "اليوم" },
        { "Spring", "الربيع" },
        { "Summer", "الصيف" },
        { "Autumn", "الخريف" },
        { "Winter", "الشتاء" },
        { "Sunny", "مشمس" },
        { "Cloudy", "غائم" },
        { "Rain", "مطر" },
        { "Heatwave", "موجة حر" },
        { "[E] Plow field", "[E] احرث الأرض" },
        { "[E] Plant", "[E] ازرع" },
        { "[E] Water", "[E] اسقِ" },
        { "No seeds - buy them at a Farm Shop", "ما فيه بذور - اشترها من محل المزرعة" },
        { "In season", "في موسمه" },
        { "Out of season - slow", "مو موسمه - بطيء" },
        { "Field plowed", "تم حرث الأرض" },
        { "Planted", "تمت زراعة" },
        { "Start the well first to get water", "شغّل البئر أول عشان الماء" },
        { "Field watered", "تم ري الحقل" },
        { "Harvested", "تم حصاد" },
        { "kg", "كجم" },
        { "Seeds", "بذور" },
        { "[0-9] or click to buy   [Esc] close", "[0-9] أو اضغط للشراء   [Esc] إغلاق" },
        { "All missions complete - free play", "كل المهمات اكتملت - لعب حر" },
        { "Bigger truck bed", "صندوق وانيت أكبر" },
        { "Bought", "تم الشراء" },
        { "Bump!", "صدمة!" },
        { "Cargo {0}/{1} kg", "الحمولة {0}/{1} كجم" },
        { "Collected", "تم الجمع" },
        { "SAR", "ريال" },
        { "Farm Shop", "محل المزرعة" },
        { "Farm storage is empty - harvest first", "مخزن المزرعة فاضي - احصد أول" },
        { "Faster growth", "نمو أسرع" },
        { "Fully upgraded", "تمت الترقية بالكامل" },
        { "Graphics: High", "الجرافيكس: عالي" },
        { "Graphics: Low", "الجرافيكس: منخفض" },
        { "Loaded {0} kg onto the truck", "تم تحميل {0} كجم على الوانيت" },
        { "Loading bay", "منطقة التحميل" },
        { "Market", "السوق" },
        { "Mission", "مهمة" },
        { "Mission complete", "المهمة اكتملت" },
        { "Money: {0} SAR", "الرصيد: {0} ريال" },
        { "Money: {0} SAR   Feed: {1}", "الرصيد: {0} ريال   العلف: {1}" },
        { "New game started", "بدأت لعبة جديدة" },
        { "No feed - buy feed at the shop", "ما فيه علف - اشترِ علف من المحل" },
        { "Not enough money", "الفلوس ما تكفي" },
        { "Nothing to collect yet", "ما فيه شي تجمعه للحين" },
        { "Paused", "إيقاف مؤقت" },
        { "Reward", "المكافأة" },
        { "Sold the load for {0} SAR", "انباعت الحمولة بـ {0} ريال" },
        { "Storage: {0} kg   Feed: {1}", "المخزن: {0} كجم   العلف: {1}" },
        { "The pen is full for this animal", "الحظيرة مليانة من هالحيوان" },
        { "Truck is full - drive to the market", "الوانيت مليان - رح السوق" },
        { "[Backspace] New game (clears saved progress)", "[Backspace] لعبة جديدة (يمسح التقدم المحفوظ)" },
        { "[E] Collect animal products", "[E] اجمع منتجات الحيوانات" },
        { "[E] Drive truck", "[E] سُق الوانيت" },
        { "[E] Exit   [Space] Brake   [R] Reset truck", "[E] انزل   [Space] فرامل   [R] عدّل الوانيت" },
        { "[E] Open shop", "[E] افتح المحل" },
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
        { "STORY_BODY",
            "Your grandfather left you his small farm in Al-Qassim: two greenhouses, an old well and empty land.\n\n" +
            "Get the water running, bring the greenhouses back to life, plow and plant the fields, raise animals " +
            "and drive your harvest to the market. Follow the orange marker for your next task.\n\n" +
            "Make Mazraa the finest farm in Al-Qassim." },
        { "HELP_BODY",
            "WASD - move    Mouse - look    E - interact\n" +
            "Start the well, then use the Control Station to optimize\n" +
            "Irrigation, Nutrients, Energy and Climate.\n" +
            "Healthy (green) beds grow - press E on a grown bed to harvest.\n" +
            "Esc - close panels    H - help    L - Arabic/English\n" +
            "T - start/stop a timed training scenario    N - next scenario\n" +
            "Truck: E get in/out, WASD drive, Space brake, R reset. Green pad loads, gold pad sells.\n" +
            "Shops sell feed, animals and upgrades. Collect eggs, milk and wool at the pen crate.\n" +
            "G - graphics quality    P - pause / new game\n" +
            "Fields: E to plow, plant (buy seeds), water (well must run), harvest. Seasons and weather change growth.\n" +
            "M - market prices and buyer contracts (contracts pay a bonus when you sell at the market)\n" +
            "Tractor: reverse up to an implement in the yard, Q attaches / detaches it, V lowers / raises it - drive it over a field. Tab switches vehicles. Park on the green pad to refuel.\n" +
            "Animals need feed, a full water trough and a clean pen (barn door). Happy pairs have young." },
    };

    private static readonly Dictionary<string, string> arabicLong = new Dictionary<string, string>
    {
        { "STORY_BODY",
            "جدّك ترك لك مزرعته الصغيرة في القصيم: بيتين محميين وبئر قديم وأرض فاضية.\n\n" +
            "شغّل الماء، رجّع البيوت المحمية للحياة، احرث وازرع الحقول، ربِّ الحيوانات " +
            "ووصّل محصولك للسوق. اتبع العلامة البرتقالية عشان تعرف المهمة الجاية.\n\n" +
            "خلّ مزرعتك أفضل مزرعة في القصيم." },
        { "HELP_BODY",
            "WASD - حركة    الماوس - نظر    E - تفاعل\n" +
            "شغّل البئر ثم استخدم محطة التحكم لتحسين\n" +
            "الري والتسميد والطاقة والمناخ.\n" +
            "الأحواض السليمة (الخضراء) تنمو - اضغط E على الحوض الناضج للحصاد.\n" +
            "Esc - إغلاق    H - مساعدة    L - عربي/إنجليزي\n" +
            "T - بدء/إيقاف سيناريو تدريب    N - السيناريو التالي\n" +
            "الوانيت: E ركوب/نزول، WASD قيادة، Space فرامل، R تعديل. الأخضر تحميل، الذهبي بيع.\n" +
            "المحلات فيها علف وحيوانات وترقيات. اجمع البيض والحليب والصوف من صندوق الحظيرة.\n" +
            "G - جودة الجرافيكس    P - إيقاف / لعبة جديدة\n" +
            "الحقول: E للحرث والزراعة (اشترِ بذور) والري (البئر لازم يشتغل) والحصاد. المواسم والطقس تأثر على النمو.\n" +
            "M - أسعار السوق وعقود المشترين (العقود تدفع أكثر لما تبيع في السوق)\n" +
            "الجرار: ارجع إلى معدّة في الساحة، Q للتركيب / الفك، V للإنزال / الرفع - سُقه فوق الحقل. Tab لتبديل المركبة. وقّف على المنطقة الخضراء للتموين.\n" +
            "الحيوانات تحتاج علف وحوض ماء مليان وحظيرة نظيفة (باب الحظيرة). الأزواج المرتاحة تتكاثر." },
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
    public static void LoadSaved() => SetLanguage((Language)Mathf.Clamp(SaveData.GetInt(PrefKey, 0), 0, 1));

    public static void SetLanguage(Language lang)
    {
        Current = lang;
        SaveData.SetInt(PrefKey, (int)lang);
        SaveData.Save();
        LocalizeScene();
        OnLanguageChanged?.Invoke();
    }

    public static void Toggle() => SetLanguage(IsArabic ? Language.English : Language.Arabic);

    /// <summary>Translate an English key (unshaped). Unknown keys are returned as-is.</summary>
    private static bool packsLoaded;
    /// <summary>Load feature translations from Resources/Data/Loc/*.json once (never overrides built-in keys).</summary>
    private static void LoadPacks()
    {
        packsLoaded = true;
        foreach (var p in DataPacks.LoadAs<LocPack>("Loc"))
            if (p.entries != null)
                foreach (var e in p.entries)
                    if (!string.IsNullOrEmpty(e.en) && !string.IsNullOrEmpty(e.ar) && !arabic.ContainsKey(e.en)) arabic[e.en] = e.ar;
    }

    public static string Raw(string english)
    {
        if (english == null) return "";
        if (english == "SAR") return Regions.CurrencyRaw(IsArabic);   // the region's currency (Egypt: EGP / جنيه)
        if (!packsLoaded) LoadPacks();
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
