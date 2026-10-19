using System;
using System.Collections.Generic;
using UnityEngine;

// Data for the Saudi crops feature, read from Resources/Data/Economy/30_crops.json (the same file also adds
// goods and field crops to the economy through the EconomyFile pack format; the extra sections are read here).

[Serializable] public class CropMetaDef { public string id, family = ""; public int cuts = 1; public float regrowFrom = 0.6f, feedPerKg; }

[Serializable]
public class TreeDef
{
    public string id, nameEn, nameAr, good, fruitSeason = "summer", model = "";
    public int saplingPrice = 300, ripenDay = 1;
    public float matureSeasons = 3f, yieldKg = 20f, r = 0.5f, g = 0.3f, b = 0.2f;
    public bool palm;
}

[Serializable]
public class PestDef
{
    public string id, nameEn, nameAr;
    public string[] seasons, families;
    public float chance = 0.05f, heatwave = 1f, rain = 1f, bedChance, growthMult = 0.6f, damagePerDay = 0.03f;
    /// <summary>Extra cures that treat this pest (cure ids), so a pack can add a pest without redefining the cures.</summary>
    public string[] curedBy;
}

[Serializable]
public class CureDef
{
    public string id, nameEn, nameAr;
    public int price = 50;
    public string[] cures;
    public int protectDays;
    public float fertility;
}

[Serializable]
public class CropsPackFile
{
    public CropMetaDef[] cropMeta;
    public TreeDef[] trees;
    public PestDef[] pests;
    public CureDef[] cures;
}

public static class CropsData
{
    private static CropsPackFile data;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => data = null;

    public static CropsPackFile Data
    {
        get
        {
            if (data != null) return data;
            // Every economy pack can add crop meta, trees, pests and cures (30_crops = Saudi crops, 93_egypt = Nile Delta DLC);
            // later packs add to or replace earlier entries by id.
            data = new CropsPackFile { cropMeta = new CropMetaDef[0], trees = new TreeDef[0], pests = new PestDef[0], cures = new CureDef[0] };
            foreach (var pack in DataPacks.LoadAs<CropsPackFile>("Economy"))
            {
                data.cropMeta = Merge(data.cropMeta, pack.cropMeta, m => m.id);
                data.trees = Merge(data.trees, pack.trees, t => t.id);
                data.pests = Merge(data.pests, pack.pests, x => x.id);
                data.cures = Merge(data.cures, pack.cures, c => c.id);
            }
            return data;
        }
    }

    private static T[] Merge<T>(T[] baseArr, T[] add, Func<T, string> id)
    {
        if (add == null || add.Length == 0) return baseArr;
        var list = new List<T>(baseArr);
        foreach (var x in add) { if (x == null) continue; int i = list.FindIndex(y => id(y) == id(x)); if (i >= 0) list[i] = x; else list.Add(x); }
        return list.ToArray();
    }

    public static CropMetaDef Meta(string cropId) => string.IsNullOrEmpty(cropId) ? null : Array.Find(Data.cropMeta, m => m.id == cropId);
    public static string Family(string cropId) { var m = Meta(cropId); return m != null && !string.IsNullOrEmpty(m.family) ? m.family : (cropId ?? ""); }
    public static TreeDef Tree(string id) => string.IsNullOrEmpty(id) ? null : Array.Find(Data.trees, t => t.id == id);
    public static PestDef Pest(string id) => string.IsNullOrEmpty(id) ? null : Array.Find(Data.pests, p => p.id == id);
    public static CureDef Cure(string id) => string.IsNullOrEmpty(id) ? null : Array.Find(Data.cures, c => c.id == id);

    public static bool Has(string[] list, string v) { if (list == null) return false; foreach (var x in list) if (x == v) return true; return false; }

    /// <summary>Pick the English or Arabic name (unshaped).</summary>
    public static string Name(string en, string ar) => Loc.IsArabic && !string.IsNullOrEmpty(ar) ? ar : (en ?? "");
    public static string Name(TreeDef t) => t == null ? "" : Name(t.nameEn, t.nameAr);
    public static string Name(PestDef p) => p == null ? "" : Name(p.nameEn, p.nameAr);
    public static string Name(CureDef c) => c == null ? "" : Name(c.nameEn, c.nameAr);
}

/// <summary>
/// Text for the crops feature. English keys with Arabic in Resources/Data/Loc/30_crops.json
/// (the headless tests check every key used through these helpers has a translation there).
/// </summary>
public static class CropsText
{
    /// <summary>Translated, not shaped (for building a line that is shaped once).</summary>
    public static string R(string english) => Loc.Raw(english);
    /// <summary>Translated and shaped for display.</summary>
    public static string T(string english) => Loc.T(english);
    public static void Say(string translatedUnshaped) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(translatedUnshaped)); }
    public static string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%";
}
