using System;
using System.Collections.Generic;
using UnityEngine;

// FarmCore data (Resources/Data/FarmCore/*.json, see the "notes" in 00_core.json for sources and units).
// Pure data and lookups, no scene access, so everything here is tested headless.

[Serializable] public class FcCropDef
{
    public string id = "", family = "";
    public float yieldT = 5f, growDays = 4f, harvestDays = 3f, seedBagHa = 0.01f;
    public int cuts = 1;
    public float regrowFrom = 0.6f, feedPerKg;
    public bool forage, rows;
    // agronomy (ported from soil.json / SoilModel)
    public float n = 90f, p = 25f, k = 80f, rn = 40f, rp = 10f, rk = 30f, fix;
    public float phMin = 5.5f, phMax = 8.3f, ecT = 3f, ecS = 10f;
    public float tBase = 6f, tOptLo = 16f, tOptHi = 28f, tMax = 35f, et = 450f, ky = 1f, residue = 0.5f;
    /// <summary>Paddy crop (rice): grows in standing water, so waterlogging costs nothing.</summary>
    public bool flood;
    /// <summary>Soil moisture (share of field capacity) below which the crop is water-stressed.</summary>
    public float waterMin = 0.35f;
    /// <summary>Sowing seasons (spring/summer/autumn/winter); empty = the economy pack's seasons.</summary>
    public string[] sow;
}

[Serializable] public class FcSoilStart { public float n = 200f, p = 55f, k = 250f, ph = 7.8f, om = 1f, ec = 1.5f, compaction = 0.15f; }

[Serializable] public class FcRegionDef
{
    public string id = "";
    public float[] temp = { 24f, 34f, 27f, 15f }, et0 = { 6f, 9f, 6f, 3.5f };
    public float rainMm = 8f, capacityMm = 90f, ecWater = 1.5f, waterCost = 0.1f, drainage = 1f, stones = 0.2f;
    public float gwStart = 80f, gwWellMax = 160f, gwDeepMax = 400f, gwDrawPerM3 = 0.001f, gwRecharge = 0.02f;
    public bool canal;
    public float canalFee;
    public FcSoilStart start = new FcSoilStart();
}

[Serializable] public class FcToolDef { public string id = ""; public float width = 3f, speedKmh = 12f, draft = 3000f, mass = 800f, tankKg; }

[Serializable] public class FcEmitterDef
{
    public string id = "";
    public float eff = 0.7f, salt = 1f, leach = 0.15f, evapDay = 0.15f, evapNight = 0.05f, flowHa = 8f, headM = 20f;
    public int price = 500;
    public float pricePerM, size, minSize, maxSize, revHours = 24f;
    public bool waterlog;
}

[Serializable] public class FcSourceDef { public string id = ""; public float capacity, storage; public int price; public string depthLimit = ""; }
[Serializable] public class FcPowerDef { public string id = ""; public int price; public float kwp, dieselLPerKwh; }
[Serializable] public class FcPipeDef { public int diameterMm = 75; public float capacity = 14f, pricePerM = 7f; }

[Serializable] public class FcAmendDef
{
    public string id = "", nameEn = "", nameAr = "", careId = "";
    public int price = 50;
    public float bagHa = 0.25f;
    public bool shop = true, herbicide, pesticide, mulch, growing, spread, spray;
    public float n, p, k, om, ph, ec, compaction, manureKg;
}

[Serializable] public class FcRotationDef { public string prev = "", next = ""; public float mult = 1f; }

[Serializable] public class FarmCoreFile
{
    public int kitPrice = -1;
    public float leachMm = -1f, sameFamilyMult = -1f, otherFamilyMult = -1f, seedM2PerBag = -1f;
    public FcCropDef[] crops;
    public FcRegionDef[] regions;
    public FcToolDef[] tools;
    public FcEmitterDef[] emitters;
    public FcSourceDef[] sources;
    public FcPowerDef[] power;
    public FcPipeDef[] pipes;
    public FcAmendDef[] amendments;
    public FcRotationDef[] rotation;
}

/// <summary>Merged FarmCore data packs and lookups (crops by id and by compact index, regions, tools, irrigation parts).</summary>
public static class FcData
{
    private static FarmCoreFile data;
    private static readonly Dictionary<string, FcCropDef> cropById = new Dictionary<string, FcCropDef>();
    private static readonly List<string> cropIds = new List<string> { "" };   // index 0 = no crop

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { data = null; cropById.Clear(); cropIds.Clear(); cropIds.Add(""); }

    public static FarmCoreFile File
    {
        get
        {
            if (data != null) return data;
            var d = new FarmCoreFile
            {
                kitPrice = 25, leachMm = 150f, sameFamilyMult = 0.9f, otherFamilyMult = 1.1f, seedM2PerBag = 100f,
                crops = new FcCropDef[0], regions = new FcRegionDef[0], tools = new FcToolDef[0], emitters = new FcEmitterDef[0],
                sources = new FcSourceDef[0], power = new FcPowerDef[0], pipes = new FcPipeDef[0], amendments = new FcAmendDef[0], rotation = new FcRotationDef[0],
            };
            foreach (var pack in DataPacks.LoadAs<FarmCoreFile>("FarmCore"))
            {
                if (pack.kitPrice >= 0) d.kitPrice = pack.kitPrice;
                if (pack.leachMm >= 0f) d.leachMm = pack.leachMm;
                if (pack.sameFamilyMult >= 0f) d.sameFamilyMult = pack.sameFamilyMult;
                if (pack.otherFamilyMult >= 0f) d.otherFamilyMult = pack.otherFamilyMult;
                if (pack.seedM2PerBag > 0f) d.seedM2PerBag = pack.seedM2PerBag;
                d.crops = Merge(d.crops, pack.crops, x => x.id);
                d.regions = Merge(d.regions, pack.regions, x => x.id);
                d.tools = Merge(d.tools, pack.tools, x => x.id);
                d.emitters = Merge(d.emitters, pack.emitters, x => x.id);
                d.sources = Merge(d.sources, pack.sources, x => x.id);
                d.power = Merge(d.power, pack.power, x => x.id);
                d.pipes = Merge(d.pipes, pack.pipes, x => x.diameterMm.ToString());
                d.amendments = Merge(d.amendments, pack.amendments, x => x.id);
                if (pack.rotation != null && pack.rotation.Length > 0) d.rotation = Merge(d.rotation, pack.rotation, x => x.prev + ">" + x.next);
            }
            if (d.regions.Length == 0) d.regions = new[] { new FcRegionDef { id = Regions.DefaultId } };
            if (d.pipes.Length == 0) d.pipes = new[] { new FcPipeDef() };
            Array.Sort(d.pipes, (a, b) => a.diameterMm.CompareTo(b.diameterMm));
            data = d;
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

    // ------------------------------------------------------------------ crops

    /// <summary>Agronomy of a crop. Crops only in the economy pack get defaults scaled from their old plot numbers.</summary>
    public static FcCropDef Crop(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        FcCropDef c;
        if (cropById.TryGetValue(id, out c)) return c;
        c = Array.Find(File.crops, x => x.id == id);
        if (c == null)
        {
            var eco = EconomyManager.Instance != null ? EconomyManager.Instance.FieldCrop(id) : null;
            if (eco == null) return null;
            c = new FcCropDef { id = id, family = CropsData.Family(id), growDays = Mathf.Clamp(eco.growSeconds / 60f, 1.5f, 8f), yieldT = Mathf.Clamp(eco.yieldKg / 10f, 2f, 60f) };
        }
        if (string.IsNullOrEmpty(c.family)) c.family = CropsData.Family(id);
        cropById[id] = c;
        return c;
    }

    /// <summary>Compact index of a crop id (1..255) for the per-cell crop layer; 0 = none. Stable for the session; saves store their own table.</summary>
    public static byte CropIndex(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        int i = cropIds.IndexOf(id);
        if (i < 0) { if (cropIds.Count >= 255) return 0; cropIds.Add(id); i = cropIds.Count - 1; }
        return (byte)i;
    }

    public static string CropId(byte index) => index < cropIds.Count ? cropIds[index] : "";

    public static string Family(string cropId)
    {
        var c = Crop(cropId);
        return c != null && !string.IsNullOrEmpty(c.family) ? c.family : CropsData.Family(cropId);
    }

    /// <summary>Sowing seasons of a crop: its FarmCore "sow" list, else the economy pack's seasons.</summary>
    public static string[] SowSeasons(string cropId)
    {
        var c = Crop(cropId);
        if (c != null && c.sow != null && c.sow.Length > 0) return c.sow;
        var eco = EconomyManager.Instance != null ? EconomyManager.Instance.FieldCrop(cropId) : null;
        return eco != null ? eco.seasons : null;
    }

    public static bool InSowingWindow(string cropId, Season season)
    {
        var s = SowSeasons(cropId);
        if (s == null || s.Length == 0) return true;
        string now = WorldClock.SeasonId(season);
        foreach (var x in s) if (x == now) return true;
        return false;
    }

    // ------------------------------------------------------------------ regions

    public static FcRegionDef Region(string id)
    {
        foreach (var r in File.regions) if (r.id == id) return r;
        return File.regions[0];
    }

    public static FcRegionDef CurrentRegion => Region(Regions.Current != null ? Regions.Current.id : Regions.DefaultId);

    // ------------------------------------------------------------------ tools, irrigation parts, amendments

    public static FcToolDef Tool(FieldTool t)
    {
        string id = t.ToString();
        foreach (var x in File.tools) if (x.id == id) return x;
        return new FcToolDef { id = id };
    }

    public static FcEmitterDef Emitter(IrrKind k)
    {
        string id = k.ToString();
        foreach (var x in File.emitters) if (x.id == id) return x;
        return new FcEmitterDef { id = id };
    }

    public static FcSourceDef Source(IrrKind k)
    {
        string id = k.ToString();
        foreach (var x in File.sources) if (x.id == id) return x;
        return new FcSourceDef { id = id };
    }

    public static FcPowerDef Power(IrrPower p)
    {
        string id = p.ToString();
        foreach (var x in File.power) if (x.id == id) return x;
        return new FcPowerDef { id = id };
    }

    public static FcPipeDef Pipe(int diameterMm)
    {
        FcPipeDef best = File.pipes[0];
        foreach (var x in File.pipes) if (x.diameterMm <= diameterMm) best = x;
        return best;
    }

    public static FcAmendDef Amendment(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var a in File.amendments) if (a.id == id) return a;
        return null;
    }

    public static string AmendName(FcAmendDef a) => a == null ? "" : Loc.IsArabic && !string.IsNullOrEmpty(a.nameAr) ? a.nameAr : a.nameEn;

    /// <summary>Yield multiplier for crop family 'next' after 'prev' (1 when there was no previous crop).</summary>
    public static float Rotation(string prev, string next)
    {
        if (string.IsNullOrEmpty(prev) || string.IsNullOrEmpty(next)) return 1f;
        foreach (var r in File.rotation) if (r.prev == prev && r.next == next) return r.mult;
        return prev == next ? File.sameFamilyMult : File.otherFamilyMult;
    }
}

/// <summary>Text for FarmCore: English keys with Arabic in Resources/Data/Loc/89_farmcore.json (tests check every key).</summary>
public static class FcText
{
    /// <summary>Translated, not shaped (for building a line that is shaped once).</summary>
    public static string R(string english) => Loc.Raw(english);
    /// <summary>Translated and shaped.</summary>
    public static string T(string english) => Loc.T(english);
    public static string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%";
    public static void Say(string translatedUnshaped) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(translatedUnshaped)); }

    public static string CropName(string id)
    {
        if (string.IsNullOrEmpty(id)) return R("none");
        var eco = EconomyManager.Instance;
        var def = eco != null ? eco.FieldCrop(id) : null;
        return def != null ? FieldCropText.Name(def) : id;
    }

    /// <summary>"0.12 ha" or "850 m2".</summary>
    public static string Area(float m2) => m2 >= 1000f ? (m2 / 10000f).ToString("0.00") + " " + R("ha") : m2.ToString("F0") + " " + R("m2");
}
