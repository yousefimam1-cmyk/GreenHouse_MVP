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
    }

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
