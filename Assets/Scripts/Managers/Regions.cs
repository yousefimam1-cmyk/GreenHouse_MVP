using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class RegionDef
{
    public string id, nameEn, nameAr, countryEn, countryAr, currency, currencyAr, descEn, descAr, storeId;
    public bool dlc, comingSoon;
    public float heat = 1f, rain, market = 1f;
    /// <summary>true = the map has its own story: only missions, campaign, events and daily quests tagged with its id.</summary>
    public bool ownContent;
    /// <summary>Extra seed bags of a new career on this map (crop ids, one bag each per entry).</summary>
    public string[] startSeeds;
    /// <summary>English Loc keys for the market pad and the farm shop stall on this map (empty = "Market" / "Farm Shop").</summary>
    public string marketName, shopName;
    /// <summary>Canal water turn (nobat el-mayya): every N days the "canalEvent" event is forecast for the next day (0 = never).</summary>
    public int canalEvery;
    public string canalEvent;
    // Selling points (cotton gin, rice mill, village souq...) and contract clients of a map are region-gated entries in
    // Resources/Data/market.json (MarketManager), not here.
    public RegionLook look;
}

/// <summary>
/// How the map looks (DesertDressing, DesertWeatherFX, GraphicsEnhancer). Defaults are the Najd desert, so a region
/// without a "look" block looks exactly like the base game. Colours are [r, g, b].
/// </summary>
[Serializable]
public class RegionLook
{
    public float[] ground = { 0.84f, 0.7f, 0.5f }, dune = { 0.87f, 0.74f, 0.53f }, track = { 0.72f, 0.6f, 0.44f };
    public float[] fog = { 0.86f, 0.78f, 0.64f };
    public bool dunes = true, canals, pigeonTower;
    public string trees = "desert";            // desert = acacia + sidr; eucalyptus = rows of eucalyptus (windbreaks) + a few sidr
    public float treeCount = 34f;
    public float dust = 1f, wind = 1f;         // blowing dust and wind multipliers (Delta: mild, no big dust storms)
    public float fogBase = 0.0025f, humidity;  // base fog density; humidity adds a morning river haze (0..1)
    public float saturation = 12f, temperature = 8f;   // post-processing grade (greener, cooler in the Delta)

    public static Color C(float[] c, Color fallback) => c != null && c.Length >= 3 ? new Color(c[0], c[1], c[2]) : fallback;
}

[Serializable]
public class RegionFile { public RegionDef[] regions; }

/// <summary>
/// Maps of the Arab world (Resources/Data/regions.json). The base game is Najd (Saudi Arabia); Egypt's Nile Delta is
/// the first DLC; more regions are listed as "coming soon". The chosen region is stored in the save and changes the
/// currency shown, the weather odds and market prices. DLC ownership comes from Dlc.Owns.
/// </summary>
public static class Regions
{
    public const string DefaultId = "najd";
    private const string Key = "mazraa.region";
    private static RegionDef[] all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { all = null; }

    public static RegionDef[] All
    {
        get
        {
            if (all != null) return all;
            var ta = Resources.Load<TextAsset>("Data/regions");
            try { all = ta != null ? JsonUtility.FromJson<RegionFile>(ta.text).regions : null; }
            catch (Exception e) { Debug.LogError("[Regions] Invalid regions.json: " + e.Message); }
            if (all == null || all.Length == 0) all = new[] { new RegionDef { id = DefaultId, nameEn = "Najd Oasis", nameAr = "واحة نجد", countryEn = "Saudi Arabia", countryAr = "السعودية", currency = "SAR", currencyAr = "ريال" } };
            return all;
        }
    }

    public static RegionDef Find(string id) { foreach (var r in All) if (r.id == id) return r; return null; }
    public static RegionDef Current => Find(SaveData.GetString(Key, DefaultId)) ?? All[0];
    public static bool IsPlayable(RegionDef r) => r != null && !r.comingSoon && (!r.dlc || Dlc.Owns(r.id));

    public static void Select(string id)
    {
        var r = Find(id);
        if (!IsPlayable(r)) return;
        SaveData.SetString(Key, r.id);
        GameEvents.RaiseRegionChanged(r.id);
    }

    public static string CurrentId => Current != null ? Current.id : DefaultId;
    public static bool IsEgypt => CurrentId == "nile_delta";

    /// <summary>Region-gated items (crops, goods): allowed when the list is empty or names the current map.</summary>
    public static bool Allows(string[] regions) => regions == null || regions.Length == 0 || Array.IndexOf(regions, CurrentId) >= 0;

    /// <summary>Story content (missions, campaign, events, daily quests): untagged items belong to maps without their own
    /// content (Najd); a map with ownContent only gets the items tagged with its id.</summary>
    public static bool HasContent(string[] regions)
    {
        var r = Current;
        if (regions == null || regions.Length == 0) return r == null || !r.ownContent;
        return Array.IndexOf(regions, CurrentId) >= 0;
    }

    private static readonly RegionLook DefaultLook = new RegionLook();
    public static RegionLook Look => Current != null && Current.look != null ? Current.look : DefaultLook;

    public static string Name(RegionDef r) => r == null ? "" : Loc.IsArabic && !string.IsNullOrEmpty(r.nameAr) ? r.nameAr : r.nameEn;
    public static string Country(RegionDef r) => r == null ? "" : Loc.IsArabic && !string.IsNullOrEmpty(r.countryAr) ? r.countryAr : r.countryEn;
    public static string Desc(RegionDef r) => r == null ? "" : Loc.IsArabic && !string.IsNullOrEmpty(r.descAr) ? r.descAr : r.descEn;

    /// <summary>Currency label for the current region (replaces "SAR" everywhere through Loc).</summary>
    public static string CurrencyRaw(bool arabic)
    {
        var r = Current;
        if (r == null || string.IsNullOrEmpty(r.currency)) return arabic ? "ريال" : "SAR";
        return arabic && !string.IsNullOrEmpty(r.currencyAr) ? r.currencyAr : r.currency;
    }
}

/// <summary>
/// DLC ownership. Unlocked when: the editor / a development build, the id is listed in persistentDataPath/mazraa_dlc.txt
/// (tester and partner keys), or a store provider says so (Provider: set by a store integration, e.g. the Microsoft
/// Store add-on check).
/// </summary>
public static class Dlc
{
    public static Func<string, bool> Provider;
    public static bool UnlockAllInDevBuilds = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Provider = null; cache = null; }

    private static HashSet<string> cache;
    public static string KeyFilePath => Path.Combine(Application.persistentDataPath, "mazraa_dlc.txt");

    public static bool Owns(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (UnlockAllInDevBuilds && (Application.isEditor || Debug.isDebugBuild)) return true;
        if (Provider != null) { try { if (Provider(id)) return true; } catch (Exception) { } }
        if (cache == null)
        {
            cache = new HashSet<string>();
            try { if (File.Exists(KeyFilePath)) foreach (var l in File.ReadAllLines(KeyFilePath)) if (l.Trim().Length > 0) cache.Add(l.Trim()); }
            catch (Exception) { }
        }
        return cache.Contains(id);
    }

    /// <summary>Forget the cached key file (after it changed).</summary>
    public static void Refresh() => cache = null;
}
