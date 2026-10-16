using System;
using System.Collections.Generic;
using UnityEngine;

// Data for the farm life features (Resources/Data/farmlife.json). All texts are bilingual (en / ar) in the data.

[Serializable] public class FLText { public string en, ar; public string T => FarmLife.Pick(en, ar); }
[Serializable] public class FLXp { public string id; public float xp; }
[Serializable] public class FLReward { public int money, xp, seedCount; public float feed; public string seeds, decor; }
[Serializable] public class FLFeature { public string id, en, ar; }
[Serializable] public class FLDecor { public string id, en, ar, model; public int price, minLevel, beauty; }
[Serializable] public class FLMasteryCrop { public string id, en, ar; public int[] stars; }
[Serializable] public class FLMastery { public int[] stars = { 200, 800, 2000 }; public float yieldBonus = 0.05f, orderBonus = 0.1f; public int[] starXp = { 50, 120, 250 }; public FLMasteryCrop[] crops = new FLMasteryCrop[0]; }
[Serializable] public class FLHeartEvent : FLReward { public int hearts; public string en, ar; }
[Serializable] public class FLVillager
{
    public string id, en, ar, @short, shortAr, model, spot, bdSeason;
    public bool old, female;
    public int bdDay;
    public string[] loves = new string[0], likes = new string[0], dislikes = new string[0];
    public FLText[] talk = new FLText[0];
    public FLHeartEvent[] events = new FLHeartEvent[0];
    public FLReward dailyGift;
}
[Serializable] public class FLOrderTemplate { public string id, festival, en, ar; public string[] goods; public int minLines = 1, maxLines = 1, min = 1, max = 5, minLevel = 1; public float pay = 1.5f; public int xp = 10; }
[Serializable] public class FLOrders { public float refreshHours = 4f; public FLText[] names = new FLText[0]; public FLOrderTemplate[] templates = new FLOrderTemplate[0]; }
[Serializable] public class FLCompetition { public string kind, good, en, ar; public float amount = 1f; }
[Serializable] public class FLFestival
{
    public string id, linked, season, en, ar, blurbEn, blurbAr;
    public int from, to, ordersGoal;
    public FLReward goalReward;
    public FLCompetition competition;
    public FLReward[] prizes = new FLReward[0];
    public bool HasCompetition => competition != null && !string.IsNullOrEmpty(competition.kind);
}
[Serializable] public class FLBeautyDecor { public string id; public int beauty; }
[Serializable] public class FLBeauty
{
    public float decorDefault = 2f, decorCap = 60f, fields = 20f, animals = 20f, noAnimals = 8f, treeEach = 1.5f, treeCap = 12f, festival = 5f;
    public FLBeautyDecor[] oldDecor = new FLBeautyDecor[0];
    public float[] stars = { 10, 25, 45, 70, 95 };
    public float pricePerStar = 0.01f, ordersPerStar = 0.05f;
}
[Serializable] public class FLLevelRow : FLReward { public int level; public float need; public string[] unlocks = new string[0]; }
[Serializable]
public class FarmLifeFile
{
    public FLXp[] xp = new FLXp[0];
    public float extraLevelGrowth = 1.18f;
    public FLLevelRow[] levels = new FLLevelRow[0];
    public FLFeature[] features = new FLFeature[0];
    public FLDecor[] decor = new FLDecor[0];
    public FLMastery mastery = new FLMastery();
    public int pointsPerHeart = 100;
    public FLVillager[] villagers = new FLVillager[0];
    public FLOrders orders = new FLOrders();
    public FLFestival[] festivals = new FLFestival[0];
    public FLText[] rivals = new FLText[0];
    public FLReward[] login = new FLReward[0];
    public FLBeauty beauty = new FLBeauty();
}

/// <summary>
/// Shared helpers for the farm life features (level and XP, crop mastery, roadside stand orders, friendships,
/// festivals, daily gift, farm beauty, photo mode): the data file, rewards, celebration cards and save helpers.
/// Everything is saved in the farm save (SaveData "mazraa.fl.*", cleared by a new game) except the daily gift
/// streak, which is account-wide (PlayerPrefs "mazraa.account.gift.*").
/// </summary>
public static class FarmLife
{
    /// <summary>
    /// Passive bonuses (beauty market bonus, crop mastery extra harvest). The headless tests turn them off for the
    /// older sections whose checks compare exact prices and amounts, like EventDirector.RandomEventsEnabled.
    /// </summary>
    public static bool PassiveBonuses = true;

    private static FarmLifeFile data;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { data = null; }

    public static FarmLifeFile Data
    {
        get
        {
            if (data != null) return data;
            var t = Resources.Load<TextAsset>("Data/farmlife");
            try { data = t != null ? JsonUtility.FromJson<FarmLifeFile>(t.text) : null; }
            catch (Exception e) { Debug.LogError("[FarmLife] Invalid farmlife.json: " + e.Message); }
            if (data == null) data = new FarmLifeFile();
            return data;
        }
    }

    public static string Pick(string en, string ar) => Loc.IsArabic && !string.IsNullOrEmpty(ar) ? ar : en ?? "";

    public static float Now => WorldClock.Instance != null ? WorldClock.Instance.Day + WorldClock.Instance.DayProgress : 1f;
    public static int DaysPerSeason => WorldClock.Instance != null ? Mathf.Max(1, WorldClock.Instance.daysPerSeason) : 5;
    public static Season SeasonOf(int day) => (Season)(((day - 1) / DaysPerSeason) % 4);
    public static int DayOfSeason(int day) => (day - 1) % DaysPerSeason + 1;
    public static int Year(int day) => (day - 1) / (DaysPerSeason * 4);

    public static Season ParseSeason(string s)
    {
        switch ((s ?? "").ToLowerInvariant()) { case "summer": return Season.Summer; case "autumn": return Season.Autumn; case "winter": return Season.Winter; default: return Season.Spring; }
    }

    public static string Stars(int n, int max) => new string('*', Mathf.Clamp(n, 0, max)) + new string('-', Mathf.Max(0, max - Mathf.Clamp(n, 0, max)));

    public static string GoodName(string id)
    {
        var eco = EconomyManager.Instance;
        var g = eco != null ? eco.Good(id) : null;
        return g != null ? EconomyManager.GoodName(g) : id;
    }

    public static string GoodUnit(string id)
    {
        var eco = EconomyManager.Instance;
        var g = eco != null ? eco.Good(id) : null;
        return g != null ? EconomyManager.Unit(g) : "";
    }

    public static FLDecor Decor(string id) => Array.Find(Data.decor, d => d.id == id);

    // ---------------- rewards ----------------

    /// <summary>Give a reward to the farm. Returns a translated (unshaped) summary, e.g. "+100 SAR, Feed +5".</summary>
    public static string Grant(FLReward r, string xpSource = "reward")
    {
        if (r == null) return "";
        var parts = new List<string>();
        var eco = EconomyManager.Instance;
        if (r.money > 0 && eco != null) { eco.AddMoney(r.money); parts.Add("+" + r.money + " " + Loc.Raw("SAR")); }
        if (r.feed > 0f && eco != null) { eco.AddFeed(r.feed); parts.Add(Loc.Raw("Feed") + " +" + r.feed.ToString("0")); }
        if (!string.IsNullOrEmpty(r.seeds) && r.seedCount > 0 && eco != null && eco.FieldCrop(r.seeds) != null)
        {
            eco.AddSeeds(r.seeds, r.seedCount);
            parts.Add(Loc.Raw("Seeds") + ": " + FieldCropText.Name(eco.FieldCrop(r.seeds)) + " +" + r.seedCount);
        }
        if (!string.IsNullOrEmpty(r.decor) && Decorations.Instance != null && Decor(r.decor) != null)
        {
            Decorations.Instance.AddToStock(r.decor);
            parts.Add(Pick(Decor(r.decor).en, Decor(r.decor).ar) + " (" + Loc.Raw("Decor") + ")");
        }
        if (r.xp > 0 && FarmLevel.Instance != null) { FarmLevel.Instance.AddXp(r.xp); parts.Add("+" + r.xp + " " + Loc.Raw("XP")); }
        return string.Join(", ", parts.ToArray());
    }

    public static string Describe(FLReward r)
    {
        if (r == null) return "";
        var parts = new List<string>();
        var eco = EconomyManager.Instance;
        if (r.money > 0) parts.Add("+" + r.money + " " + Loc.Raw("SAR"));
        if (r.feed > 0f) parts.Add(Loc.Raw("Feed") + " +" + r.feed.ToString("0"));
        if (!string.IsNullOrEmpty(r.seeds) && r.seedCount > 0 && eco != null && eco.FieldCrop(r.seeds) != null) parts.Add(Loc.Raw("Seeds") + ": " + FieldCropText.Name(eco.FieldCrop(r.seeds)) + " +" + r.seedCount);
        if (!string.IsNullOrEmpty(r.decor) && Decor(r.decor) != null) parts.Add(Pick(Decor(r.decor).en, Decor(r.decor).ar));
        if (r.xp > 0) parts.Add("+" + r.xp + " " + Loc.Raw("XP"));
        return string.Join(", ", parts.ToArray());
    }

    // ---------------- save helpers ----------------

    public static T Load<T>(string key) where T : class, new()
    {
        try { var s = SaveData.GetString(key, ""); if (!string.IsNullOrEmpty(s)) { var o = JsonUtility.FromJson<T>(s); if (o != null) return o; } }
        catch (Exception) { }
        return new T();
    }

    public static void Store(string key, object o) { SaveData.SetString(key, JsonUtility.ToJson(o)); SaveData.Save(); }

    // ---------------- cards ----------------

    /// <summary>Show a celebration card (non-blocking, top of the screen). Texts already translated (unshaped).</summary>
    public static void Card(string title, string body, string footer = null)
    {
        CelebrationCard.Show(title, body, footer);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
    }
}
